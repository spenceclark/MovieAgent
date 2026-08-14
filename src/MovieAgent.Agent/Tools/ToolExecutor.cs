using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MovieAgent.Agent.Abstractions;

namespace MovieAgent.Agent.Tools;

public sealed record ToolInvocation(string ToolName, IReadOnlyDictionary<string, object?>? Arguments);

public sealed record ToolInvocationResult(
    string ToolName,
    string Output,
    bool IsError,
    bool IsTerminal,
    int RowsReturned,
    long ElapsedMilliseconds);

/// <summary>
/// Executes one tool call: validate arguments, run the descriptor's parameterised SQL,
/// render through the frozen output contract. Never throws for a model mistake — a bad call
/// comes back as tool output the model can read and react to, because that reaction is part
/// of what is being measured.
/// </summary>
public sealed class ToolExecutor
{
    private readonly ISqlQueryExecutor _sql;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(ISqlQueryExecutor sql, ILogger<ToolExecutor> logger)
    {
        _sql = sql;
        _logger = logger;
    }

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolSurface surface,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!ToolLookup.ByName.TryGetValue(invocation.ToolName, out var tool)
            || !surface.ToolNames.Contains(invocation.ToolName, StringComparer.Ordinal))
        {
            // Not in the surface is indistinguishable from not existing, from the model's
            // point of view, and should stay that way — otherwise the surface leaks.
            var available = string.Join(", ", surface.ToolNames);
            return Error(
                invocation.ToolName,
                ToolOutputFormat.RetryableError($"There is no tool called '{invocation.ToolName}'. Available tools: {available}."),
                isTerminal: false,
                stopwatch);
        }

        if (tool.Kind == ToolKind.Schema)
        {
            stopwatch.Stop();
            return new ToolInvocationResult(
                tool.Name,
                SqlShortcutCatalogue.SchemaListing,
                IsError: false,
                IsTerminal: false,
                RowsReturned: 0,
                stopwatch.ElapsedMilliseconds);
        }

        var binding = ToolArgumentBinder.Bind(tool, invocation.Arguments);
        if (!binding.Success)
        {
            return Error(tool.Name, ToolOutputFormat.RetryableError(binding.Error!), isTerminal: false, stopwatch);
        }

        if (tool.Kind == ToolKind.FreeSql)
        {
            return await ExecuteFreeSqlAsync(tool, binding.Values, stopwatch, cancellationToken);
        }

        try
        {
            var result = await _sql.QueryAsync(tool.Sql, binding.Values, cancellationToken);
            var output = ToolOutputFormat.Rows(result, tool.MaxRows, tool.EmptyResultHint);

            stopwatch.Stop();
            return new ToolInvocationResult(
                tool.Name,
                output,
                IsError: false,
                IsTerminal: false,
                RowsReturned: result.RowCount,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A SQL failure here is a harness fault, not a model fault: the model cannot
            // rewrite the descriptor's SQL, so retrying the same call is pointless and
            // saying so stops it burning iterations.
            _logger.LogError(ex, "Tool {Tool} failed to execute.", tool.Name);

            return Error(
                tool.Name,
                ToolOutputFormat.TerminalError($"The tool '{tool.Name}' failed to run against the database."),
                isTerminal: true,
                stopwatch);
        }
    }

    /// <summary>
    /// The <c>sql-shortcut</c> path: screen the model's SQL, run it read-only, and render it
    /// through the same output contract as every other tool.
    /// </summary>
    /// <remarks>
    /// A database error is returned to the model verbatim and marked retryable, which is the
    /// opposite of the descriptor path's behaviour and deliberate: there, a SQL failure is a
    /// harness fault the model cannot fix, so retrying is pointless. Here the model wrote the
    /// query, so Postgres's complaint is legitimate feedback and acting on it is part of what
    /// this surface measures.
    /// </remarks>
    private async Task<ToolInvocationResult> ExecuteFreeSqlAsync(
        ToolDescriptor tool,
        IReadOnlyDictionary<string, object?>? values,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var query = values is not null && values.TryGetValue("query", out var raw) ? raw as string : null;
        var verdict = SqlShortcutGuard.Inspect(query);

        if (!verdict.Allowed)
        {
            return Error(tool.Name, ToolOutputFormat.RetryableError(verdict.Reason!), isTerminal: false, stopwatch);
        }

        try
        {
            var result = await _sql.QueryReadOnlyAsync(verdict.Sql, cancellationToken);
            var output = ToolOutputFormat.Rows(result, tool.MaxRows);

            stopwatch.Stop();
            return new ToolInvocationResult(
                tool.Name,
                output,
                IsError: false,
                IsTerminal: false,
                RowsReturned: result.RowCount,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Model-supplied SQL failed.");

            // Postgres's own message, unedited. Column-does-not-exist is exactly the feedback a
            // model needs to fix its next attempt, and whether it uses it is the measurement.
            var message = ex.Message.ReplaceLineEndings(" ").Trim();
            return Error(
                tool.Name,
                ToolOutputFormat.RetryableError($"The database rejected the query: {message}"),
                isTerminal: false,
                stopwatch);
        }
    }

    private static ToolInvocationResult Error(string toolName, string output, bool isTerminal, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new ToolInvocationResult(toolName, output, IsError: true, isTerminal, RowsReturned: 0, stopwatch.ElapsedMilliseconds);
    }
}
