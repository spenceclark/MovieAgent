using Microsoft.Extensions.Options;
using MovieAgent.Agent;
using MovieAgent.Agent.Recording;
using MovieAgent.Hosting;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Configuration;

namespace MovieAgent.EntryPoints;

/// <summary>
/// One ad-hoc question through the loop. Recorded like any other run but not graded, so
/// exploratory questions do not pollute the accuracy figures.
/// </summary>
public sealed class AskEntryPoint : IAppEntryPoint
{
    private readonly AgentLoop _agentLoop;
    private readonly IRunRecorder _recorder;
    private readonly IWireCapture _wireCapture;
    private readonly LlmOptions _llmOptions;
    private readonly AgentOptions _agentOptions;

    public AskEntryPoint(
        AgentLoop agentLoop,
        IRunRecorder recorder,
        IWireCapture wireCapture,
        IOptions<LlmOptions> llmOptions,
        IOptions<AgentOptions> agentOptions)
    {
        _agentLoop = agentLoop;
        _recorder = recorder;
        _wireCapture = wireCapture;
        _llmOptions = llmOptions.Value;
        _agentOptions = agentOptions.Value;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("""Usage: ask "<question>" """);
            return 1;
        }

        var question = string.Join(' ', args);
        var model = _llmOptions.Provider == LlmProvider.OpenAI
            ? _llmOptions.OpenAI.Model
            : _llmOptions.Ollama.Model;

        var run = await _agentLoop.RunAsync(
            new AgentRunRequest { QuestionId = "adhoc", Question = question },
            _llmOptions.Provider.ToString(),
            model,
            cancellationToken);

        await _recorder.RecordAsync(run, cancellationToken);

        // Only wired for Ollama — the capture handler hangs off that named HttpClient, and the
        // OpenAI SDK builds its own pipeline that never touches it. Enabled-but-empty means the
        // wrong provider, not a bug, so say so rather than silently writing nothing.
        string? wireDirectory = null;
        if (_wireCapture.Enabled)
        {
            if (_wireCapture.Bodies.Count > 0)
            {
                wireDirectory = Path.Combine("runs", "wire", "ask");
                await WireCaptureReporting.DumpBodiesAsync(_wireCapture, wireDirectory, cancellationToken);
            }
            else if (_llmOptions.Provider != LlmProvider.Ollama)
            {
                Console.WriteLine(
                    $"(Agent__CaptureWireTraffic is on, but wire capture is only wired for Ollama; " +
                    $"provider is {_llmOptions.Provider}, so nothing was captured.)");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"model:      {run.Provider}/{run.Model} (thinking {(_agentOptions.Thinking ? "on" : "off")})");
        Console.WriteLine($"outcome:    {run.Outcome}{(run.CapHit ? " (iteration cap hit)" : string.Empty)}");
        Console.WriteLine($"iterations: {run.IterationCount}/{run.MaxIterations}   tool calls: {run.ToolCallCount}");
        Console.WriteLine($"tokens:     in {run.TotalInputTokens?.ToString() ?? "?"}, out {run.TotalOutputTokens?.ToString() ?? "?"}");
        Console.WriteLine($"elapsed:    {run.ElapsedMilliseconds} ms");
        Console.WriteLine();
        Console.WriteLine(run.FinalAnswer ?? "(no final answer)");
        Console.WriteLine();
        Console.WriteLine($"recorded to {_recorder.FilePath}");

        if (wireDirectory is not null)
        {
            Console.WriteLine($"wire traffic ({_wireCapture.Bodies.Count} exchange(s)) written to {wireDirectory}");
        }

        return run.Outcome == RunOutcome.Errored ? 1 : 0;
    }
}
