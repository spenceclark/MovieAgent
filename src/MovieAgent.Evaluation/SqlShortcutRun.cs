using MovieAgent.Agent.Recording;

namespace MovieAgent.Evaluation;

/// <summary>
/// The questions the <c>sql-shortcut</c> control is allowed to run, and the summary shape it
/// reports instead of the main one.
/// </summary>
public static class SqlShortcutRun
{
    /// <summary>
    /// The ten linear FK-resolution questions from pagila-v1, hop2 to hop5.
    /// </summary>
    /// <remarks>
    /// Everything else is excluded because <c>execute_sql</c> makes the labels wrong, not merely
    /// the questions easier:
    /// <list type="bullet">
    /// <item>The decline questions are labelled unreachable <em>relative to a tool surface</em>.
    /// <c>unreachable-total-film-count</c> is a one-line <c>count(*)</c> here, so grading a
    /// refusal as correct would be grading the model wrong for being right.</item>
    /// <item>Near-miss recovery is about how a model reacts to a search tool returning NO ROWS.
    /// There is no search tool here.</item>
    /// <item>Fan-out and truncation are about composing and reading paged tool output. A single
    /// SELECT collapses both.</item>
    /// </list>
    /// Enforced in code rather than by convention — see <see cref="EnsureAllowed"/> — because a
    /// filter typo would otherwise produce a plausible-looking but invalid number.
    /// </remarks>
    public static IReadOnlySet<string> ChainQuestionIds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "hop2-film-cost",
        "hop2-actor-count",
        "hop2-actor-film-count",
        "hop3-film-language",
        "hop3-film-categories",
        "hop3-rental-film-title",
        "hop3-store-manager-email",
        "hop4-customer-country",
        "hop4-inventory-store-city",
        "hop5-title-2025-renter",
    };

    /// <summary>Throws if anything outside the chain family has been selected on this surface.</summary>
    public static void EnsureAllowed(IEnumerable<EvalQuestion> questions)
    {
        var offenders = questions
            .Select(q => q.Id)
            .Where(id => !ChainQuestionIds.Contains(id))
            .ToArray();

        if (offenders.Length > 0)
        {
            throw new InvalidOperationException(
                "The 'sql-shortcut' surface runs the chain family only. " +
                $"These questions are not part of it: {string.Join(", ", offenders)}. " +
                "Near-miss, fan-out, truncation and decline questions all carry labels that " +
                "execute_sql invalidates — filter to the chain questions or use another surface.");
        }
    }

    public static SqlShortcutStats Summarise(IReadOnlyList<RunRecord> runs)
    {
        var iterations = runs.Sum(r => r.Iterations.Count);
        var calls = runs.SelectMany(r => r.Iterations).SelectMany(i => i.ToolCalls).ToArray();
        var sqlCalls = calls.Where(c => string.Equals(c.ToolName, "execute_sql", StringComparison.Ordinal)).ToArray();
        var schemaCalls = calls.Where(c => string.Equals(c.ToolName, "get_schema", StringComparison.Ordinal)).ToArray();

        // The behavioural column: a model that writes SQL without reading the schema first is
        // guessing at table and column names, which is the main sweep's fabrication instinct in
        // a new form. Runs that never ran any SQL cannot answer the question either way.
        var runsWithSql = 0;
        var schemaFirst = 0;
        foreach (var run in runs)
        {
            var ordered = run.Iterations.SelectMany(i => i.ToolCalls).ToArray();
            var firstSql = Array.FindIndex(ordered, c => string.Equals(c.ToolName, "execute_sql", StringComparison.Ordinal));
            if (firstSql < 0)
            {
                continue;
            }

            runsWithSql++;
            if (Array.FindIndex(ordered, c => string.Equals(c.ToolName, "get_schema", StringComparison.Ordinal)) is var s && s >= 0 && s < firstSql)
            {
                schemaFirst++;
            }
        }

        var questionsFullyCorrect = runs
            .GroupBy(r => r.QuestionId, StringComparer.Ordinal)
            .Count(g => g.All(r => r.Grade?.Correct == true));

        return new SqlShortcutStats(
            Runs: runs.Count,
            Questions: runs.Select(r => r.QuestionId).Distinct(StringComparer.Ordinal).Count(),
            QuestionsFullyCorrect: questionsFullyCorrect,
            MeanToolCalls: runs.Count == 0 ? 0 : (double)calls.Length / runs.Count,
            MeanIterations: runs.Count == 0 ? 0 : (double)iterations / runs.Count,
            SqlCalls: sqlCalls.Length,
            SqlErrors: sqlCalls.Count(c => c.IsError),
            MeanSqlErrorsPerRun: runs.Count == 0 ? 0 : (double)sqlCalls.Count(c => c.IsError) / runs.Count,
            SchemaCalls: schemaCalls.Length,
            RunsWithSql: runsWithSql,
            RunsReadingSchemaFirst: schemaFirst,
            RunsWithNoToolCall: runs.Count(r => r.ToolCallCount == 0));
    }
}

/// <summary>
/// What the shortcut control reports instead of hop depth, navigation and argument provenance,
/// all of which are undefined when there is one generic tool.
/// </summary>
public sealed record SqlShortcutStats(
    int Runs,
    int Questions,
    int QuestionsFullyCorrect,
    double MeanToolCalls,
    double MeanIterations,
    int SqlCalls,
    int SqlErrors,
    double MeanSqlErrorsPerRun,
    int SchemaCalls,
    int RunsWithSql,
    int RunsReadingSchemaFirst,
    int RunsWithNoToolCall);
