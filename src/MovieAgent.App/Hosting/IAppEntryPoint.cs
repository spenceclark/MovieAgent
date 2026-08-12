namespace MovieAgent.App.Hosting;

/// <summary>
/// What the console actually does once the host is up. Swap the registration in
/// <see cref="AppHostBuilder"/> to change modes (smoke test, REPL, one-shot command).
/// </summary>
public interface IAppEntryPoint
{
    /// <returns>The process exit code.</returns>
    Task<int> RunAsync(string[] args, CancellationToken cancellationToken);
}
