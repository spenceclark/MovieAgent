using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MovieAgent.Agent.Models;

namespace MovieAgent.Agent.Tools;

/// <summary>The count line of a truncated result: how many rows exist, how many were shown.</summary>
public sealed record TruncationNotice(int TotalRows, int ShownRows);

/// <summary>
/// The frozen tool output contract. Every tool, success or failure, produces text through
/// this class and nowhere else.
/// </summary>
/// <remarks>
/// This format is part of the run configuration. Changing it invalidates comparison against
/// previously recorded runs, so bump <see cref="Version"/> when you change anything here —
/// the version is written into every run record.
/// <para>
/// The rules, in order of how badly getting them wrong would corrupt the measurements:
/// </para>
/// <list type="number">
/// <item>Zero rows returns the literal <c>NO ROWS</c>, never an empty string.</item>
/// <item>Truncation is always stated, with the true total: <c>40 rows, showing first 20</c>.</item>
/// <item>Errors state whether a retry could possibly help.</item>
/// </list>
/// </remarks>
public static partial class ToolOutputFormat
{
    /// <summary>Bump on any change to the emitted text. Recorded per run.</summary>
    /// <remarks>
    /// 1.1 added <see cref="RepeatedCallError"/>.
    /// <para>
    /// 1.2 added <see cref="UnreachableGoalError"/> and rewrote the too-short-search-term message
    /// to use it. Previously that rejection ended "You may retry this tool with different
    /// arguments", which invited a model reaching for the whole table to keep guessing terms
    /// instead of concluding the total was unreachable — a harness artefact sitting directly on
    /// the refusal axis. Runs recorded at 1.1 are not comparable with 1.2 on the refusal metric.
    /// </para>
    /// <para>
    /// 1.3 added <see cref="ToolBudgetExhaustedError"/>, which a run only ever sees if it spends
    /// its whole tool-call budget. It says the budget is gone and cannot be retried, so it sits on
    /// the refusal axis too — but only for runs that reach it, unlike 1.2's change.
    /// </para>
    /// </remarks>
    public const string Version = "1.3";

    public const string Delimiter = " | ";

    /// <summary>
    /// Line ending, pinned to LF. Using Environment.NewLine would put CRLF in the text on
    /// Windows and LF on Linux, so the same run on two machines would send the model
    /// different bytes and cost different tokens. Not a difference worth having in the data.
    /// </summary>
    public const string LineEnding = "\n";

    public const string NoRowsMarker = "NO ROWS";

    private const string RetryHint = "You may retry this tool with different arguments.";

    private const string TerminalHint =
        "This is a configuration fault in the harness, not a problem with your arguments. " +
        "Retrying will not help.";

    private const string UnreachableGoalHint =
        "You may retry with a longer, more specific search term. If what you need is every row, " +
        "or a count of them, that is not reachable with the tools you have — say so rather than " +
        "guessing terms.";

    /// <summary>Renders a successful result: header row, data rows, then a count line.</summary>
    public static string Rows(QueryResult result, int maxRows, string? emptyResultHint = null)
    {
        if (result.RowCount == 0)
        {
            return emptyResultHint is null
                ? NoRowsMarker
                : $"{NoRowsMarker}. {emptyResultHint}";
        }

        var shown = Math.Min(maxRows, result.RowCount);

        var builder = new StringBuilder();
        builder.Append(string.Join(Delimiter, result.Columns)).Append(LineEnding);

        for (var i = 0; i < shown; i++)
        {
            builder.Append(string.Join(Delimiter, result.Rows[i].Select(FormatValue))).Append(LineEnding);
        }

        builder.Append(shown == result.RowCount
            ? $"{result.RowCount} rows"
            : $"{result.RowCount} rows, showing first {shown}");

        return builder.ToString();
    }

    /// <summary>
    /// A fault the model caused and could plausibly fix — bad argument, unknown tool,
    /// out-of-range identifier.
    /// </summary>
    public static string RetryableError(string message) => $"ERROR: {message} {RetryHint}";

    /// <summary>
    /// A fault the model cannot fix — database unreachable, malformed descriptor. Saying so
    /// explicitly stops the model burning its iteration budget retrying.
    /// </summary>
    public static string TerminalError(string message) => $"ERROR: {message} {TerminalHint}";

    /// <summary>
    /// The argument was rejected, and the thing the model appears to be attempting is not
    /// reachable on this surface — even though a differently-shaped call to the same tool is.
    /// </summary>
    /// <remarks>
    /// Sits between the other two on purpose. <see cref="TerminalError"/> would be wrong: the
    /// tool is fine and a longer search term works. <see cref="RetryableError"/> was what this
    /// used to emit, and it was actively misleading — "you may retry this tool with different
    /// arguments" appended to "this tool will not list every row" reads as an invitation to keep
    /// guessing terms, and that is exactly what models did. qwen3.5:9b spent all ten iterations of
    /// <c>unreachable-total-film-count</c> on eight successive substring guesses after being told
    /// to try different arguments.
    /// <para>
    /// That matters because <c>unreachable-total-film-count</c> is a refusal question: the correct
    /// answer is to say the total is not reachable. A hint that encourages more searching depresses
    /// the refusal axis for harness reasons rather than model reasons.
    /// </para>
    /// </remarks>
    public static string UnreachableGoalError(string message) => $"ERROR: {message} {UnreachableGoalHint}";

    /// <summary>
    /// The model has issued a call it already made, byte for byte.
    /// </summary>
    /// <remarks>
    /// Observed in qwen3.5:4b, which repeated one identical <c>search_actor</c> call ten times
    /// and burned the whole iteration budget. The generic retry hint actively encourages this,
    /// so this message says the opposite in as few words as possible, and states what the call
    /// returned last time so the model does not have to search its own context for it.
    /// </remarks>
    public static string RepeatedCallError(string toolName, string argumentsRaw, string previousOutcome) =>
        $"ERROR: you have already called {toolName} with {argumentsRaw} and it returned {previousOutcome}. " +
        "The database has not changed, so this call will keep returning the same thing. " +
        "Do not repeat it. Either use different arguments, use a different tool, or answer with what you have.";

    /// <summary>
    /// The run has spent its whole tool-call budget. Terminal for tools, not for the run.
    /// </summary>
    /// <remarks>
    /// The budget is not stated in the system prompt, deliberately: telling a model up front how
    /// many calls it has hands it a ready-made reason to decline, and refusal is a headline axis.
    /// So the first the model hears of it is here, and the wording has to do two things at once —
    /// close off retrying, and not present giving up as the expected response. It names what the
    /// model can still do, in the same shape as <see cref="RepeatedCallError"/>.
    /// </remarks>
    public static string ToolBudgetExhaustedError(int budget) =>
        $"ERROR: no tool calls remain — this run allows {budget} and all of them are spent. " +
        "Retrying will not return one. Answer the question with what the results so far give you, " +
        "and if they are not enough, say specifically what is still missing.";

    /// <summary>
    /// The short form, for the second and subsequent refusals in the same turn.
    /// </summary>
    /// <remarks>
    /// The protocol needs a result for every call the model made, so a turn emitting 123 calls
    /// gets 123 results. Sending the full message each time put ~3,000 tokens of identical text
    /// into the next turn's context, which is the opposite of helping it recover.
    /// </remarks>
    public const string ToolBudgetExhaustedRepeat = "ERROR: no tool calls remain. See the first result in this batch.";

    /// <summary>
    /// Reads the count line back out of a tool result, if it truncated.
    /// </summary>
    /// <remarks>
    /// Exists so grading can ask a sharper question than "did the model say the right number":
    /// "did the tool output the model actually saw contain a truncation notice, and does the
    /// model's stated total match the one that notice gave it." Two runs can both answer a
    /// truncated-list question correctly; only one of them is distinguishable from a lucky
    /// guess without this, because the true total is not otherwise available anywhere in the
    /// recorded data except the harness's own <c>rows_returned</c> telemetry, which the model
    /// never sees.
    /// </remarks>
    public static TruncationNotice? TryParseTruncation(string toolOutput)
    {
        var match = TruncationLinePattern().Match(toolOutput);
        if (!match.Success)
        {
            return null;
        }

        return new TruncationNotice(
            int.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["shown"].Value, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"(?<total>\d+) rows, showing first (?<shown>\d+)\s*$")]
    private static partial Regex TruncationLinePattern();

    private static string FormatValue(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => s,
        bool b => b ? "true" : "false",
        // Second precision, no timezone suffix: rental and return dates need the time
        // component, but ISO-8601 round-trip format costs tokens for no analytical gain.
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
