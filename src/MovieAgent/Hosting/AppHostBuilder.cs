using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MovieAgent.Agent.DependencyInjection;
using MovieAgent.EntryPoints;
using MovieAgent.Data.DependencyInjection;
using MovieAgent.Evaluation.DependencyInjection;
using MovieAgent.Llm.DependencyInjection;
using MovieAgent.Agent.Tools.DependencyInjection;

namespace MovieAgent.Hosting;

/// <summary>
/// Single composition root. Each module contributes its own registrations.
/// </summary>
public static class AppHostBuilder
{
    public static IHost Build(string[] args)
    {
        // Pin the content root to the binaries so appsettings.json and the eval set resolve
        // no matter what directory the process was launched from.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration.AddUserSecrets<Marker>(optional: true);

        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.Services
            .AddMovieAgentData(builder.Configuration)
            .AddMovieAgentLlm(builder.Configuration)
            .AddMovieAgentTools()
            .AddMovieAgentAgent(builder.Configuration)
            .AddMovieAgentEvaluation(builder.Configuration);

        // Entry points are selected by the first command-line argument. See EntryPointRouter.
        builder.Services.AddScoped<ConnectivityCheckEntryPoint>();
        builder.Services.AddScoped<AskEntryPoint>();
        builder.Services.AddScoped<EvalEntryPoint>();
        builder.Services.AddScoped<VerifyEvalSetEntryPoint>();
        builder.Services.AddScoped<ListToolsEntryPoint>();
        builder.Services.AddScoped<RegradeEntryPoint>();
        builder.Services.AddScoped<DeterminismEntryPoint>();
        builder.Services.AddSingleton<IAppEntryPoint, EntryPointRouter>();

        return builder.Build();
    }

    /// <summary>Anchors user-secrets lookup to this assembly.</summary>
    private sealed class Marker;
}
