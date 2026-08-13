using Microsoft.Extensions.DependencyInjection;
using MovieAgent.Hosting;

namespace MovieAgent.EntryPoints;

/// <summary>
/// Dispatches on the first command-line argument. A switch statement rather than a command
/// line library — there are five commands and they take at most one argument each.
/// </summary>
public sealed class EntryPointRouter : IAppEntryPoint
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EntryPointRouter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        var rest = args.Length > 1 ? args[1..] : [];

        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        return command switch
        {
            "check" => await services.GetRequiredService<ConnectivityCheckEntryPoint>().RunAsync(rest, cancellationToken),
            "ask" => await services.GetRequiredService<AskEntryPoint>().RunAsync(rest, cancellationToken),
            "eval" => await services.GetRequiredService<EvalEntryPoint>().RunAsync(rest, cancellationToken),
            "verify" => await services.GetRequiredService<VerifyEvalSetEntryPoint>().RunAsync(rest, cancellationToken),
            "tools" => await services.GetRequiredService<ListToolsEntryPoint>().RunAsync(rest, cancellationToken),
            "regrade" => await services.GetRequiredService<RegradeEntryPoint>().RunAsync(rest, cancellationToken),
            "determinism" => await services.GetRequiredService<DeterminismEntryPoint>().RunAsync(rest, cancellationToken),
            _ => PrintUsage(),
        };
    }

    private static int PrintUsage()
    {
        Console.WriteLine(
            """
            MovieAgent - multi-hop tool-use harness over Pagila.

              check              Confirm the database and the model endpoint are reachable.
              verify             Re-run every eval reference SQL and compare with the recorded answers.
                                 Run this before any measurement run.
              tools [surface]    Print the tool surface as the model will see it.
              ask "<question>"   Run one ad-hoc question through the agent loop. Recorded, ungraded.
              eval [id-filter]   Run the eval set, grade it, and append to the JSONL recorder.
              regrade <file>     Re-score an existing JSONL with the current grader. No re-running.
              determinism [id] [n]
                                 Run one question n times and compare request/response bytes per
                                 turn. Needs Agent__CaptureWireTraffic=true.

            Configuration lives in appsettings.json; override with environment variables, e.g.
              Agent__ToolSurface=minimal  Agent__Repeats=5  Llm__Ollama__Model=qwen3:4b-instruct
            """);

        return 1;
    }
}
