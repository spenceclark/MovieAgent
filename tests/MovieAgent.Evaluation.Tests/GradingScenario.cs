using MovieAgent.Agent.Recording;
using MovieAgent.Evaluation;

namespace MovieAgent.Evaluation.Tests;

/// <summary>
/// Builders for the two records <see cref="Grader.Grade"/> needs. Both have a long tail of
/// required properties that no grading test cares about, and repeating them inline would bury the
/// one field each test is actually about.
/// </summary>
internal static class GradingScenario
{
    internal const string DefaultSurface = "standard+desc";

    /// <summary>Every tool the questions below reference, so a surface is "complete" by default.</summary>
    internal static readonly string[] FullSurface =
    [
        "search_film", "search_actor", "search_customer", "search_category",
        "get_film", "get_actor", "get_customer", "get_category", "get_language",
        "get_address", "get_city", "get_country", "get_store", "get_staff",
        "get_film_actor_ids", "get_actor_film_ids", "get_film_category_ids",
        "get_category_film_ids", "get_film_inventory_ids", "get_inventory_item",
        "count_actor_films", "count_film_actors",
    ];

    internal static EvalQuestion Question(
        AnswerKind kind = AnswerKind.Exact,
        string? expectedAnswer = "Boksburg",
        string expectedBehaviour = "answer",
        IReadOnlyList<IReadOnlyList<string>>? requiresTools = null,
        bool scored = true,
        int expectedHops = 2) => new()
        {
            Id = "test-question",
            Question = "A question.",
            ExpectedHops = expectedHops,
            ExpectedBehaviour = expectedBehaviour,
            ExpectedAnswer = expectedAnswer,
            AnswerKind = kind,
            ReferenceSql = "select 1",
            RequiresTools = requiresTools ?? [["search_film"], ["get_film"]],
            Scored = scored,
        };

    /// <summary>
    /// A run that answered. <paramref name="toolsCalled"/> becomes one successful call each, which
    /// is what navigation is computed from; pass <paramref name="failedTools"/> for calls that
    /// errored, since those must not count as having reached anything.
    /// </summary>
    internal static RunRecord Run(
        string? finalAnswer = "The city is Boksburg.",
        RunOutcome outcome = RunOutcome.Answered,
        IEnumerable<string>? toolsCalled = null,
        IEnumerable<string>? failedTools = null,
        string finishReason = "stop",
        IEnumerable<string>? toolResults = null)
    {
        var calls = new List<ToolCallRecord>();
        var results = toolResults?.ToArray() ?? [];
        var i = 0;

        foreach (var tool in toolsCalled ?? ["search_film", "get_film"])
        {
            calls.Add(Call(tool, results.Length > i ? results[i] : "film_id | title\n1 | X\n1 rows", isError: false));
            i++;
        }

        foreach (var tool in failedTools ?? [])
        {
            calls.Add(Call(tool, "ERROR: bad argument.", isError: true));
        }

        return new RunRecord
        {
            RunId = "test-run",
            StartedAt = DateTimeOffset.UnixEpoch,
            QuestionId = "test-question",
            Question = "A question.",
            ToolSurface = DefaultSurface,
            ToolNames = FullSurface,
            Provider = "Test",
            Model = "test-model",
            Thinking = false,
            ReplayThinking = false,
            Repeat = 1,
            SystemPrompt = "system",
            SystemPromptSha256 = new string('0', 64),
            OutputFormatVersion = "1.3",
            MaxIterations = 20,
            Outcome = outcome,
            CapHit = outcome == RunOutcome.IterationCapReached,
            FinalAnswer = finalAnswer,
            IterationCount = 1,
            ToolCallCount = calls.Count,
            ElapsedMilliseconds = 1,
            Iterations =
            [
                new IterationRecord
                {
                    Iteration = 1,
                    ElapsedMilliseconds = 1,
                    FinishReason = finishReason,
                    ToolCalls = calls,
                },
            ],
        };
    }

    private static ToolCallRecord Call(string tool, string result, bool isError) => new()
    {
        Iteration = 1,
        ToolName = tool,
        ArgumentsRaw = "{}",
        ResultText = result,
        RowsReturned = isError ? 0 : 1,
        IsError = isError,
        ElapsedMilliseconds = 1,
    };

    internal static GradeRecord Grade(
        EvalQuestion question,
        RunRecord run,
        IReadOnlyList<string>? surface = null,
        bool genericSql = false) =>
        Grader.Grade(question, run, surface ?? FullSurface, genericSql);
}
