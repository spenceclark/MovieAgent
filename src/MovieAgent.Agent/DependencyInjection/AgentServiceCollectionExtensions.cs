using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieAgent.Agent.Recording;
using MovieAgent.Agent.Configuration;

namespace MovieAgent.Agent.DependencyInjection;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddMovieAgentAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateSelf();

        services.AddOptions<RecorderOptions>()
            .Bind(configuration.GetSection(RecorderOptions.SectionName));

        // Singleton so every run in a session appends to one file.
        services.AddSingleton<IRunRecorder, JsonlRunRecorder>();
        services.AddScoped<AgentLoop>();

        return services;
    }
}
