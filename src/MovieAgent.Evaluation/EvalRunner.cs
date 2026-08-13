using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieAgent.Agent;
using MovieAgent.Agent.Recording;
using MovieAgent.Agent.Configuration;
using MovieAgent.Agent.Tools;

namespace MovieAgent.Evaluation;

/// <summary>
/// Runs every question in the eval set, grades each run, and writes it to the JSONL recorder.
/// </summary>
public sealed class EvalRunner
{
    private readonly AgentLoop _agentLoop;
    private readonly IRunRecorder _recorder;
    private readonly AgentOptions _agentOptions;
    private readonly LlmOptions _llmOptions;
    private readonly ILogger<EvalRunner> _logger;

    public EvalRunner(
        AgentLoop agentLoop,
        IRunRecorder recorder,
        IOptions<AgentOptions> agentOptions,
        IOptions<LlmOptions> llmOptions,
        ILogger<EvalRunner> logger)
    {
        _agentLoop = agentLoop;
        _recorder = recorder;
        _agentOptions = agentOptions.Value;
        _llmOptions = llmOptions.Value;
        _logger = logger;
    }

    public async Task<EvalSummary> RunAsync(
        EvalSet evalSet,
        string? questionIdFilter = null,
        CancellationToken cancellationToken = default)
    {
        var surface = ToolSurfaces.Get(_agentOptions.ToolSurface);
        var model = _llmOptions.Provider == LlmProvider.OpenAI
            ? _llmOptions.OpenAI.Model
            : _llmOptions.Ollama.Model;

        var questions = questionIdFilter is null
            ? evalSet.Questions
            : [.. evalSet.Questions.Where(q => q.Id.Contains(questionIdFilter, StringComparison.OrdinalIgnoreCase))];

        if (questions.Count == 0)
        {
            throw new InvalidOperationException($"No question id matches '{questionIdFilter}'.");
        }

        _logger.LogInformation(
            "Eval set {EvalSet}: {Questions} question(s) x {Repeats} repeat(s) on surface '{Surface}' with {Model} (thinking {Thinking}).",
            evalSet.EvalSetId,
            questions.Count,
            _agentOptions.Repeats,
            surface.Name,
            model,
            _agentOptions.Thinking ? "on" : "off");

        var graded = new List<(EvalQuestion Question, RunRecord Run)>();

        foreach (var question in questions)
        {
            for (var repeat = 1; repeat <= _agentOptions.Repeats; repeat++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var run = await _agentLoop.RunAsync(
                    new AgentRunRequest
                    {
                        QuestionId = question.Id,
                        Question = question.Question,
                        ExpectedHops = question.ExpectedHops,
                        Repeat = repeat,
                    },
                    _llmOptions.Provider.ToString(),
                    model,
                    cancellationToken);

                var grade = Grader.Grade(question, run, surface.ToolNames);
                var recorded = run with { Grade = grade };

                await _recorder.RecordAsync(recorded, cancellationToken);
                graded.Add((question, recorded));

                // Naming the unreached tools inline is the difference between "went somewhere
                // else entirely" and "got to the right rows and did not resolve the last one".
                var navigation = grade.Correct || grade.RequiredToolsMissing.Count == 0
                    ? string.Empty
                    : $", never reached {string.Join(", ", grade.RequiredToolsMissing)}";

                _logger.LogInformation(
                    "{Result} {QuestionId} (repeat {Repeat}, hops {Hops}) - {Calls} call(s), {Iterations} iteration(s){Cap}{Navigation}",
                    grade.Correct ? "PASS" : "FAIL",
                    question.Id,
                    repeat,
                    question.ExpectedHops,
                    run.ToolCallCount,
                    run.IterationCount,
                    run.CapHit ? ", CAP HIT" : string.Empty,
                    navigation);
            }
        }

        return EvalSummary.From(evalSet.EvalSetId, surface.Name, model, _agentOptions.Thinking, graded);
    }
}

public sealed record EvalSummary(
    string EvalSetId,
    string Surface,
    string Model,
    bool Thinking,
    int Runs,
    int Correct,
    int NavigatedCorrect,
    int CapHits,
    int Errors,
    int EmptyAnswers,
    double MeanToolCalls,
    double MeanCallsPerIteration,
    int RepeatedCalls,
    int FabricatedArgumentCount,
    int FabricatedIdCount,
    int FabricatedTermCount,
    int CallIdAsArgumentCount,
    int ArgumentTypeMismatchCount,
    int SchemaErrorCount,
    double? MeanInputTokens,
    double? MeanOutputTokens,
    IReadOnlyList<HopAccuracy> ByHopDepth,
    RefusalAccuracy Refusals)
{
    public static EvalSummary From(
        string evalSetId,
        string surface,
        string model,
        bool thinking,
        IReadOnlyList<(EvalQuestion Question, RunRecord Run)> graded)
    {
        // Unscored exhibits still run and are still recorded; they just never reach a denominator.
        graded = [.. graded.Where(g => g.Run.Grade?.Scored != false)];

        var byHop = graded
            .Where(g => g.Run.Grade?.ExpectedBehaviour == "answer")
            .GroupBy(g => g.Question.ExpectedHops)
            .OrderBy(g => g.Key)
            .Select(g => new HopAccuracy(
                g.Key,
                g.Count(),
                g.Count(x => x.Run.Grade?.Correct == true),
                g.Count(x => x.Run.Grade?.NavigationComplete == true)))
            .ToArray();

        var refusalCases = graded.Where(g => g.Run.Grade?.ExpectedBehaviour == "decline").ToArray();
        var answerCases = graded.Where(g => g.Run.Grade?.ExpectedBehaviour == "answer").ToArray();

        // The batching metric: how many calls land in one turn on average, counting only
        // iterations that made at least one call (a final answer's zero-call iteration would
        // otherwise drag this toward zero without saying anything about batching).
        var callBearingIterations = graded.SelectMany(g => g.Run.Iterations).Count(it => it.ToolCalls.Count > 0);
        var totalCalls = graded.Sum(g => g.Run.ToolCallCount);

        // Nullable, not zero, when nothing reported usage — "the provider didn't tell us" and
        // "it cost nothing" are different facts, same rule already applied to per-run totals.
        var runsWithInputTokens = graded.Where(g => g.Run.TotalInputTokens is not null).ToArray();
        var runsWithOutputTokens = graded.Where(g => g.Run.TotalOutputTokens is not null).ToArray();

        return new EvalSummary(
            evalSetId,
            surface,
            model,
            thinking,
            graded.Count,
            graded.Count(g => g.Run.Grade?.Correct == true),
            // The strict score: correct AND, where the question requires traversal, having
            // actually reached every required tool. Drops passes the model landed on by luck —
            // llama3.1 calling get_film(1) without searching, then being right because film 1
            // happens to be English. A decline needs no traversal, so it is exempt.
            graded.Count(g => g.Run.Grade?.Correct == true
                && (g.Run.Grade.ExpectedBehaviour != "answer" || g.Run.Grade.NavigationComplete == true)),
            graded.Count(g => g.Run.CapHit),
            graded.Count(g => g.Run.Outcome == RunOutcome.Errored),
            graded.Count(g => g.Run.Outcome == RunOutcome.EmptyAnswer),
            graded.Count == 0 ? 0 : graded.Average(g => g.Run.ToolCallCount),
            callBearingIterations == 0 ? 0 : (double)totalCalls / callBearingIterations,
            graded.Sum(g => g.Run.Iterations.SelectMany(i => i.ToolCalls).Count(c => c.WasRepeat)),
            graded.Sum(g => g.Run.Grade?.FabricatedArgumentCount ?? 0),
            graded.Sum(g => g.Run.Grade?.FabricatedIdCount ?? 0),
            graded.Sum(g => g.Run.Grade?.FabricatedTermCount ?? 0),
            graded.Sum(g => g.Run.Grade?.CallIdAsArgumentCount ?? 0),
            graded.Sum(g => g.Run.Grade?.ArgumentTypeMismatchCount ?? 0),
            graded.Sum(g => g.Run.Grade?.SchemaErrorCount ?? 0),
            runsWithInputTokens.Length == 0 ? null : runsWithInputTokens.Average(g => (double)g.Run.TotalInputTokens!.Value),
            runsWithOutputTokens.Length == 0 ? null : runsWithOutputTokens.Average(g => (double)g.Run.TotalOutputTokens!.Value),
            byHop,
            new RefusalAccuracy(
                refusalCases.Length,
                refusalCases.Count(g => g.Run.Grade?.Declined == true),
                answerCases.Count(g => g.Run.Grade?.Declined == true)));
    }
}

/// <summary>
/// <paramref name="Navigated"/> counts runs that reached every tool the shortest correct chain
/// needs.
/// </summary>
/// <remarks>
/// <paramref name="Correct"/> and <paramref name="Navigated"/> are graded independently —
/// <c>Correct</c> from substring-matching the final answer text, <c>Navigated</c> from which
/// required tools actually ran without error — and neither implies the other. Usually
/// <c>Navigated &gt;= Correct</c> (a run that reached everything but still stated the answer
/// wrong), but a run can also score <c>Correct</c> while skipping a required tool: it guessed or
/// already "knew" a value that happened to resolve correctly, or the substring match caught a
/// wrong-path answer that contained the right value anyway. Confirmed against a real run —
/// <c>nearmiss-film-rate</c>, where the model called <c>get_film</c> directly on a guessed
/// <c>film_id</c> without ever calling the required <c>search_film</c>, and the guess happened
/// to be CASABLANCA NIGHTS at the expected $4.99.
/// </remarks>
public sealed record HopAccuracy(int Hops, int Runs, int Correct, int Navigated);

/// <summary>
/// <paramref name="OverRefusals"/> counts answerable questions the model declined — the cost
/// side of refusal accuracy, and meaningless without it.
/// </summary>
public sealed record RefusalAccuracy(int Cases, int CorrectlyDeclined, int OverRefusals);
