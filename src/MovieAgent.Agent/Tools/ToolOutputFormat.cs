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
    /// <remarks>1.1 added <see cref="RepeatedCallError"/>.</remarks>
    public const string Version = "1.1";

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
