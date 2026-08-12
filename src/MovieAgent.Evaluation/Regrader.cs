using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovieAgent.Agent.Recording;

namespace MovieAgent.Evaluation;

public sealed record RegradeResult(
    int Total,
    int Regraded,
    int Skipped,
    int Changed,
    IReadOnlyList<string> Changes,
    int OutcomeReclassified,
    IReadOnlyList<string> OutcomeReclassifications);

/// <summary>
/// Re-scores an existing JSONL against the current grader, without re-running anything.
/// </summary>
/// <remarks>
/// Grading operates purely on the recorded <c>final_answer</c>, so a grader fix can be applied
/// retrospectively to every run ever recorded. This matters more than it sounds: the decline
/// classifier has already been wrong twice, and without this every fix would either invalidate
/// the back catalogue or cost hours of re-running local models.
/// <para>
/// The surface is taken from each run's own recorded <c>tool_names</c> rather than from current
/// configuration, so a file containing several surfaces regrades correctly in one pass.
/// </para>
/// </remarks>
public sealed class Regrader
{
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<RegradeResult> RegradeAsync(
        string inputPath,
        string outputPath,
        EvalSet evalSet,
        CancellationToken cancellationToken = default)
    {
        var questions = evalSet.Questions.ToDictionary(q => q.Id, StringComparer.Ordinal);

        var total = 0;
        var regraded = 0;
        var skipped = 0;
        var changes = new List<string>();
        var outcomeReclassifications = new List<string>();

        var output = new StringBuilder();

        foreach (var line in await File.ReadAllLinesAsync(inputPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            total++;

            RunRecord? run;
            try
            {
                run = JsonSerializer.Deserialize<RunRecord>(line, _readOptions);
            }
            catch (JsonException)
            {
                // The whole point of regrading is to be safe against schema drift across the
                // corpus's history — a line from before a field existed (e.g. Thinking and
                // ReplayThinking, both `required`, postdate the earliest runs in this session)
                // must not take down every file after it in the batch. Preserve the line as-is;
                // there is nothing to grade, but nothing to lose either.
                skipped++;
                output.Append(line).Append('\n');
                continue;
            }

            if (run is null)
            {
                skipped++;
                continue;
            }

            // Ad-hoc runs have no question in the eval set and nothing to grade against.
            if (!questions.TryGetValue(run.QuestionId, out var question))
            {
                skipped++;
                output.Append(line).Append('\n');
                continue;
            }

            var before = run.Grade;

            // Answered-but-blank reclassifies to EmptyAnswer before grading, not after, so the
            // grader's "no final answer to grade (outcome {Outcome})" note reads correctly and
            // so this is a single source of truth rather than two places computing it.
            var effectiveOutcome = RunOutcomeClassifier.Effective(run.Outcome, run.FinalAnswer);
            var reclassified = effectiveOutcome != run.Outcome;
            var runForGrading = reclassified ? run with { Outcome = effectiveOutcome } : run;

            var after = Grader.Grade(question, runForGrading, runForGrading.ToolNames);
            regraded++;

            // Expected to be empty: the new diagnostic fields and the EmptyAnswer split are
            // additive, not scoring changes. A flip here means something regressed.
            if (before?.Correct != after.Correct || before?.Declined != after.Declined)
            {
                changes.Add(
                    $"{run.Model} {run.ToolSurface} {run.QuestionId} rep{run.Repeat}: " +
                    $"{Describe(before)} -> {Describe(after)}");
            }

            // Expected and intentional, unlike the above: every Answered-with-nothing-said run
            // reclassifies once, on the first regrade after this existed.
            if (reclassified)
            {
                outcomeReclassifications.Add(
                    $"{run.Model} {run.ToolSurface} {run.QuestionId} rep{run.Repeat}: {run.Outcome} -> {effectiveOutcome}");
            }

            output.Append(JsonSerializer.Serialize(runForGrading with { Grade = after }, _writeOptions)).Append('\n');
        }

        await File.WriteAllTextAsync(outputPath, output.ToString(), _utf8NoBom, cancellationToken);

        return new RegradeResult(total, regraded, skipped, changes.Count, changes, outcomeReclassifications.Count, outcomeReclassifications);
    }

    private static string Describe(GradeRecord? grade) => grade is null
        ? "ungraded"
        : $"{(grade.Correct ? "PASS" : "FAIL")}{(grade.Declined ? "/declined" : string.Empty)}";
}
