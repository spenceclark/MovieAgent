using System.Globalization;
using System.Text;
using System.Text.Json;
using MovieAgent.Agent.Recording;

namespace MovieAgent.Evaluation;

/// <summary>
/// Renders a recorded JSONL file as a readable markdown transcript: every run, every iteration,
/// every tool call, with the stats at each level and the grade at the end.
/// </summary>
/// <remarks>
/// The JSONL is the dataset and stays authoritative; this is for reading it. Answering "what did
/// this model actually do on hop5" currently means a bespoke script every time, and the answers
/// in this project have repeatedly turned on detail that aggregate numbers hide — a model calling
/// <c>get_film(1)</c> without searching, an error the model never read, a payload in the wrong
/// channel. Those are all visible in a transcript and invisible in a summary.
/// <para>
/// Fields that a surface leaves undefined are omitted rather than printed as null or zero, for
/// the same reason the eval summary omits them: a zero reads as a measured failure.
/// </para>
/// </remarks>
public static class RunReport
{
    /// <summary>Long values are clipped so one enormous tool result cannot swamp the document.</summary>
    private const int MaxCellText = 300;

    private const int MaxBlockText = 1500;

    private static readonly JsonSerializerOptions _readOptions = new() { PropertyNameCaseInsensitive = true };

    public sealed record Result(int Runs, int Unreadable, string OutputPath);

    public static async Task<Result> WriteAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var runs = new List<RunRecord>();
        var unreadable = 0;

        foreach (var line in await File.ReadAllLinesAsync(inputPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var run = JsonSerializer.Deserialize<RunRecord>(line, _readOptions);
                if (run is null)
                {
                    unreadable++;
                    continue;
                }

                runs.Add(run);
            }
            catch (JsonException)
            {
                // Same tolerance as the regrader: a line from before a field existed must not
                // take down the whole report.
                unreadable++;
            }
        }

        var md = new StringBuilder();
        WriteHeader(md, inputPath, runs, unreadable);

        foreach (var run in runs)
        {
            WriteRun(md, run);
        }

        await File.WriteAllTextAsync(outputPath, md.ToString(), new UTF8Encoding(false), cancellationToken);
        return new Result(runs.Count, unreadable, outputPath);
    }

    private static void WriteHeader(StringBuilder md, string inputPath, List<RunRecord> runs, int unreadable)
    {
        md.Append("# Run report: ").Append(Path.GetFileName(inputPath)).Append("\n\n");

        if (runs.Count == 0)
        {
            md.Append("No readable runs in this file.\n");
            return;
        }

        var first = runs[0];
        var models = runs.Select(r => r.Model).Distinct().Order().ToArray();
        var surfaces = runs.Select(r => r.ToolSurface).Distinct().Order().ToArray();
        var graded = runs.Where(r => r.Grade is not null && r.Grade.Scored).ToArray();
        var scoredCorrect = graded.Count(r => r.Grade!.Correct);

        md.Append("| | |\n|---|---|\n");
        Row(md, "runs", runs.Count.ToString(CultureInfo.InvariantCulture)
            + (unreadable > 0 ? $" ({unreadable} unreadable line(s) skipped)" : string.Empty));
        Row(md, "model(s)", string.Join(", ", models));
        Row(md, "surface(s)", string.Join(", ", surfaces));
        Row(md, "questions", runs.Select(r => r.QuestionId).Distinct().Count().ToString(CultureInfo.InvariantCulture));
        if (graded.Length > 0)
        {
            Row(md, "correct", $"{scoredCorrect}/{graded.Length} scored run(s)");
        }

        Row(md, "outcomes", string.Join(", ", runs.GroupBy(r => r.Outcome)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count()}")));
        Row(md, "tool calls", $"{runs.Sum(r => r.ToolCallCount)} total, "
            + $"{Mean(runs.Select(r => (double)r.ToolCallCount)):0.00} per run");
        Row(md, "iterations", $"{runs.Sum(r => r.Iterations.Count)} total, "
            + $"{Mean(runs.Select(r => (double)r.Iterations.Count)):0.00} per run");
        Row(md, "tokens", $"in {Sum(runs.Select(r => r.TotalInputTokens))}, out {Sum(runs.Select(r => r.TotalOutputTokens))}");
        Row(md, "elapsed", $"{runs.Sum(r => r.ElapsedMilliseconds) / 1000.0:0.0}s total, "
            + $"{Mean(runs.Select(r => (double)r.ElapsedMilliseconds)) / 1000.0:0.0}s per run");
        Row(md, "config", $"seed {first.Seed?.ToString(CultureInfo.InvariantCulture) ?? "-"}, "
            + $"temp {first.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "-"}, "
            + $"max iterations {first.MaxIterations}, "
            + $"max output tokens {first.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "uncapped"}, "
            + $"thinking {(first.Thinking ? "on" : "off")}");
        Row(md, "output format", first.OutputFormatVersion);
        Row(md, "system prompt", "`" + first.SystemPromptSha256[..12] + "`");
        md.Append('\n');

        md.Append("## Contents\n\n");
        foreach (var run in runs)
        {
            var mark = run.Grade is null ? "·" : run.Grade.Correct ? "PASS" : "FAIL";
            md.Append("- [").Append(Escape(run.QuestionId)).Append(" (repeat ").Append(run.Repeat).Append(')')
              .Append("](#").Append(Anchor(run)).Append(") — ").Append(mark)
              .Append(", ").Append(run.ToolCallCount).Append(" call(s), ")
              .Append(run.Iterations.Count).Append(" iteration(s)\n");
        }

        md.Append('\n');
    }

    private static void WriteRun(StringBuilder md, RunRecord run)
    {
        md.Append("---\n\n");
        md.Append("## ").Append(Escape(run.QuestionId)).Append(" (repeat ").Append(run.Repeat).Append(")\n\n");
        md.Append("> ").Append(Escape(run.Question)).Append("\n\n");

        // ---- 1. question + run stats ----
        md.Append("### Stats\n\n| | |\n|---|---|\n");
        Row(md, "outcome", run.Outcome + (run.CapHit ? " (iteration cap hit)" : string.Empty));
        Row(md, "model", $"{run.Provider}/{run.Model}");
        Row(md, "surface", $"{run.ToolSurface} ({run.ToolNames.Count} tools)");
        if (run.ExpectedHops is { } hops)
        {
            Row(md, "expected hops", hops.ToString(CultureInfo.InvariantCulture));
        }

        Row(md, "iterations", $"{run.Iterations.Count}/{run.MaxIterations}");
        Row(md, "tool calls", run.ToolCallCount.ToString(CultureInfo.InvariantCulture));
        Row(md, "tokens", $"in {run.TotalInputTokens?.ToString(CultureInfo.InvariantCulture) ?? "?"}, "
            + $"out {run.TotalOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        Row(md, "elapsed", $"{run.ElapsedMilliseconds} ms");
        Row(md, "run id", "`" + run.RunId + "`");
        Row(md, "started", run.StartedAt.ToString("u", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(run.Error))
        {
            Row(md, "error", Cell(run.Error));
        }

        md.Append('\n');

        // ---- 2. iterations ----
        md.Append("### Iterations\n\n");
        if (run.Iterations.Count == 0)
        {
            md.Append("_No iterations recorded._\n\n");
        }

        foreach (var it in run.Iterations)
        {
            md.Append("#### Iteration ").Append(it.Iteration).Append("\n\n");
            md.Append("| | |\n|---|---|\n");
            Row(md, "finish reason", it.FinishReason ?? "-");
            Row(md, "tokens", $"in {it.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "?"}, "
                + $"out {it.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
            Row(md, "elapsed", $"{it.ElapsedMilliseconds} ms");
            Row(md, "tool calls", it.ToolCalls.Count.ToString(CultureInfo.InvariantCulture));
            if (it.ContentSha256 is { Length: > 12 } hash)
            {
                Row(md, "content hash", "`" + hash[..12] + "`");
            }

            md.Append('\n');

            if (!string.IsNullOrWhiteSpace(it.ReasoningText))
            {
                md.Append("<details><summary>reasoning (")
                  .Append(it.ReasoningText.Length).Append(" chars)</summary>\n\n```\n")
                  .Append(Block(it.ReasoningText)).Append("\n```\n\n</details>\n\n");
            }

            if (!string.IsNullOrWhiteSpace(it.AssistantText))
            {
                md.Append("**Said:**\n\n```\n").Append(Block(it.AssistantText)).Append("\n```\n\n");
            }

            if (it.ToolCalls.Count == 0)
            {
                md.Append("_No tool calls this iteration._\n\n");
                continue;
            }

            md.Append("##### Tool calls\n\n");
            foreach (var call in it.ToolCalls)
            {
                var flags = new List<string>();
                if (call.IsError)
                {
                    flags.Add("**ERROR**");
                }

                if (call.WasRepeat)
                {
                    flags.Add("repeat");
                }

                if (call.Blocked)
                {
                    flags.Add("blocked");
                }

                md.Append("- `").Append(call.ToolName).Append('`');
                if (flags.Count > 0)
                {
                    md.Append(" — ").Append(string.Join(", ", flags));
                }

                md.Append("\n\n  | | |\n  |---|---|\n");
                Row(md, "rows returned", call.RowsReturned.ToString(CultureInfo.InvariantCulture), "  ");
                Row(md, "elapsed", $"{call.ElapsedMilliseconds} ms", "  ");
                if (call.CallId is { } id)
                {
                    Row(md, "call id", "`" + id + "`", "  ");
                }

                Row(md, "arguments", Cell(call.ArgumentsRaw), "  ");
                Row(md, "result", Cell(call.ResultText), "  ");
                md.Append('\n');
            }
        }

        // ---- 3. grading ----
        md.Append("### Grading\n\n");
        if (run.Grade is not { } grade)
        {
            md.Append("_Ungraded (ad-hoc run)._\n\n");
            return;
        }

        md.Append("**Answer given:**\n\n```\n")
          .Append(string.IsNullOrWhiteSpace(run.FinalAnswer) ? "(no final answer)" : Block(run.FinalAnswer))
          .Append("\n```\n\n");

        md.Append("| | |\n|---|---|\n");
        Row(md, "result", grade.Correct ? "**PASS**" : "**FAIL**");
        Row(md, "expected", grade.ExpectedAnswer is null ? "_(a refusal)_" : Cell(grade.ExpectedAnswer));
        Row(md, "expected behaviour", grade.ExpectedBehaviour);
        Row(md, "declined", grade.Declined ? "yes" : "no");
        Row(md, "method", grade.Method);
        if (!grade.Scored)
        {
            Row(md, "scored", "no — qualitative exhibit, excluded from every denominator");
        }

        // Only meaningful where the surface defines them. On sql-shortcut they are null and are
        // left out entirely rather than shown as zero.
        if (grade.NavigationComplete is { } navigated)
        {
            Row(md, "navigation complete", navigated ? "yes" : "no");
            Row(md, "required tools", grade.RequiredTools.Count == 0 ? "-" : string.Join(", ", grade.RequiredTools));
            if (grade.RequiredToolsMissing.Count > 0)
            {
                Row(md, "never reached", "**" + string.Join(", ", grade.RequiredToolsMissing) + "**");
            }
        }

        if (grade.FabricatedArgumentCount is { } fabricated)
        {
            Row(md, "fabricated arguments",
                $"{fabricated} (invented id {grade.FabricatedIdCount ?? 0}, invented search term {grade.FabricatedTermCount ?? 0})");
        }

        if (grade.CallIdAsArgumentCount is { } callIds && callIds > 0)
        {
            Row(md, "call id as argument", callIds.ToString(CultureInfo.InvariantCulture));
        }

        if (grade.ArgumentTypeMismatchCount is { } mismatches && mismatches > 0)
        {
            Row(md, "type mismatches", mismatches.ToString(CultureInfo.InvariantCulture));
        }

        if (grade.SchemaErrorCount is { } schemaErrors && schemaErrors > 0)
        {
            Row(md, "schema errors", schemaErrors.ToString(CultureInfo.InvariantCulture));
        }

        if (grade.TruncationSeen)
        {
            Row(md, "truncation seen",
                $"yes, tool stated {grade.TruncationStatedTotal?.ToString(CultureInfo.InvariantCulture) ?? "?"} rows; "
                + $"answer matches: {grade.AnswerMatchesStatedTotal switch { true => "yes", false => "no", null => "n/a" }}");
        }

        if (!string.IsNullOrWhiteSpace(grade.Note))
        {
            Row(md, "note", Cell(grade.Note));
        }

        md.Append('\n');

        if (grade.FabricatedArguments.Count > 0)
        {
            md.Append("Fabricated:\n\n");
            foreach (var f in grade.FabricatedArguments)
            {
                md.Append("- `").Append(Escape(f)).Append("`\n");
            }

            md.Append('\n');
        }

        if (grade.SchemaErrors.Count > 0)
        {
            md.Append("Schema errors:\n\n");
            foreach (var e in grade.SchemaErrors)
            {
                md.Append("- ").Append(Escape(Truncate(e, MaxCellText))).Append('\n');
            }

            md.Append('\n');
        }
    }

    private static void Row(StringBuilder md, string name, string value, string indent = "") =>
        md.Append(indent).Append("| ").Append(name).Append(" | ").Append(value).Append(" |\n");

    private static double Mean(IEnumerable<double> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? 0 : array.Average();
    }

    private static string Sum(IEnumerable<long?> values)
    {
        var present = values.Where(v => v is not null).Select(v => v!.Value).ToArray();
        return present.Length == 0 ? "?" : present.Sum().ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Table-cell safe: single line, pipes escaped, clipped.</summary>
    private static string Cell(string? text) =>
        text is null
            ? "-"
            : "`" + Truncate(text.ReplaceLineEndings(" ⏎ "), MaxCellText).Replace("|", "\\|").Replace("`", "'") + "`";

    private static string Block(string text) => Truncate(text, MaxBlockText).Replace("```", "'''");

    private static string Escape(string text) => text.Replace("|", "\\|");

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + $"… (+{text.Length - max} chars)";

    private static string Anchor(RunRecord run) =>
        (run.QuestionId + "-repeat-" + run.Repeat).ToLowerInvariant().Replace('.', '-').Replace(':', '-');
}
