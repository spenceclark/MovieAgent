using Microsoft.Extensions.Options;
using MovieAgent.Agent;
using MovieAgent.Hosting;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Configuration;
using MovieAgent.Evaluation;

namespace MovieAgent.EntryPoints;

/// <summary>
/// Runs one question N times unchanged and compares the request and response bytes turn by turn.
/// </summary>
/// <remarks>
/// The question this answers: is the 15% run-to-run instability a property of the model or of
/// this harness? At iteration 1 the input is fixed and fully known — system prompt, tool
/// schemas, user question, nothing from the environment. If the request hashes match and the
/// response hashes do not, the model is non-deterministic under this serving setup. If the
/// request hashes differ, the responses differing means nothing and the harness is at fault.
/// <para>
/// Requires <c>Agent:CaptureWireTraffic=true</c>, which buffers whole bodies and so is not on
/// for measurement runs.
/// </para>
/// </remarks>
public sealed class DeterminismEntryPoint : IAppEntryPoint
{
    private readonly AgentLoop _agentLoop;
    private readonly IWireCapture _wireCapture;
    private readonly LlmOptions _llmOptions;
    private readonly AgentOptions _agentOptions;
    private readonly EvalSetOptions _evalSetOptions;

    public DeterminismEntryPoint(
        AgentLoop agentLoop,
        IWireCapture wireCapture,
        IOptions<LlmOptions> llmOptions,
        IOptions<AgentOptions> agentOptions,
        IOptions<EvalSetOptions> evalSetOptions)
    {
        _agentLoop = agentLoop;
        _wireCapture = wireCapture;
        _llmOptions = llmOptions.Value;
        _agentOptions = agentOptions.Value;
        _evalSetOptions = evalSetOptions.Value;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!_wireCapture.Enabled)
        {
            Console.Error.WriteLine("Set Agent__CaptureWireTraffic=true to run this check.");
            return 1;
        }

        // The capture handler hangs off the Ollama named HttpClient. The OpenAI SDK builds its
        // own pipeline and never touches it, so the check would silently measure nothing.
        if (_llmOptions.Provider != LlmProvider.Ollama)
        {
            Console.Error.WriteLine(
                $"Wire capture is only wired for Ollama; provider is currently {_llmOptions.Provider}. " +
                "Set Llm__Provider=Ollama.");
            return 1;
        }

        var questionId = args.Length > 0 ? args[0] : "hop4-customer-country";
        var n = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 10;

        var evalSet = EvalSetLoader.LoadMany(_evalSetOptions.FileNames);
        var question = evalSet.Questions.FirstOrDefault(q => q.Id.Contains(questionId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"No question matches '{questionId}'.");

        var model = _llmOptions.Provider == LlmProvider.OpenAI
            ? _llmOptions.OpenAI.Model
            : _llmOptions.Ollama.Model;

        Console.WriteLine($"determinism: {question.Id} x{n} on '{_agentOptions.ToolSurface}' with {model}");
        Console.WriteLine(
            $"seed={_agentOptions.Seed?.ToString() ?? "unset"} " +
            $"temperature={_agentOptions.Temperature?.ToString() ?? "unset"} " +
            $"thinking={(_agentOptions.Thinking ? "on" : "off")}");
        Console.WriteLine();

        var runs = new List<Agent.Recording.RunRecord>();
        string? firstBody = null;

        for (var i = 1; i <= n; i++)
        {
            var run = await _agentLoop.RunAsync(
                new AgentRunRequest { QuestionId = question.Id, Question = question.Question, Repeat = i },
                _llmOptions.Provider.ToString(),
                model,
                cancellationToken);

            firstBody ??= _wireCapture.FirstRequestBody;
            runs.Add(run);

            // Dump bodies so the hashes can be checked rather than believed. A whole-body hash
            // includes Ollama's per-call timing fields, so it over-reports non-determinism.
            await WireCaptureReporting.DumpBodiesAsync(_wireCapture, Path.Combine("runs", "wire", $"run{i:00}"), cancellationToken);

            var it1 = run.Iterations.FirstOrDefault();
            Console.WriteLine(
                $"  run {i,2}  req {Short(it1?.RequestSha256)}  gen {Short(it1?.ContentSha256)}  " +
                $"iters {run.IterationCount}  calls {run.ToolCallCount}");
        }

        Console.WriteLine();
        Report("ITERATION 1", runs.Select(r => r.Iterations.FirstOrDefault()).ToList());

        var maxIterations = runs.Max(r => r.IterationCount);
        for (var k = 1; k < maxIterations; k++)
        {
            var index = k;
            Report(
                $"ITERATION {index + 1}",
                [.. runs.Select(r => r.Iterations.Count > index ? r.Iterations[index] : null)]);
        }

        Console.WriteLine();
        Console.WriteLine("outbound request body, iteration 1 (check the sampling parameters actually left the harness):");
        Console.WriteLine(WireCaptureReporting.SummariseRequest(firstBody));

        return 0;
    }

    private static void Report(string label, IReadOnlyList<Agent.Recording.IterationRecord?> iterations)
    {
        var present = iterations.Where(i => i is not null).ToArray();
        if (present.Length == 0)
        {
            return;
        }

        // Uncaptured hashes are null, and a set of nulls has one distinct value, which would
        // otherwise be reported as perfect determinism. Refusing to answer is the only honest
        // output when the instrument did not run.
        if (present.Any(i => i!.RequestSha256 is null))
        {
            Console.WriteLine(
                $"{label,-13} NOT CAPTURED - no wire hashes recorded. The capture handler is only " +
                "attached to the Ollama client; this tells you nothing.");
            return;
        }

        var requests = present.Select(i => i!.RequestSha256).Distinct().Count();

        // Content, not the raw response body. The body always differs: Ollama's envelope carries
        // created_at and four duration fields, so hashing it reports non-determinism on a
        // generation that was identical token for token.
        var responses = present.Select(i => i!.ContentSha256).Distinct().Count();
        var missing = iterations.Count - present.Length;

        var verdict = (requests, responses) switch
        {
            (1, 1) => "deterministic",
            (1, _) => "SAME REQUEST, DIFFERENT GENERATION -> model is non-deterministic here",
            (_, 1) => "different requests, same generation",
            _ => "DIFFERENT REQUESTS -> the harness changed the input; generation differences prove nothing",
        };

        Console.WriteLine(
            $"{label,-13} n={present.Length,2}{(missing > 0 ? $" (+{missing} ended earlier)" : string.Empty)}  " +
            $"distinct requests={requests}  distinct responses={responses}  {verdict}");
    }

    private static string Short(string? hash) => hash is null ? "--------" : hash[..8];
}
