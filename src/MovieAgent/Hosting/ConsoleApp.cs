using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MovieAgent.Hosting;

/// <summary>
/// Starts the host (so hosted services and startup options validation both run),
/// hands control to the registered <see cref="IAppEntryPoint"/>, then shuts down.
/// </summary>
public static class ConsoleApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var host = AppHostBuilder.Build(args);

        try
        {
            await host.StartAsync();
        }
        catch (OptionsValidationException ex)
        {
            Console.Error.WriteLine("Configuration error:");
            foreach (var failure in ex.Failures)
            {
                Console.Error.WriteLine($"  - {failure}");
            }

            return 1;
        }

        try
        {
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var entryPoint = host.Services.GetRequiredService<IAppEntryPoint>();

            return await entryPoint.RunAsync(args, lifetime.ApplicationStopping);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
