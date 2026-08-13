using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Configuration;
using MovieAgent.Llm.Diagnostics;
using MovieAgent.Llm.Providers;

namespace MovieAgent.Llm.DependencyInjection;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single <see cref="IChatClient"/> chosen by <c>Llm:Provider</c>.
    /// This is the only place in the solution that knows which SDK is in play.
    /// </summary>
    /// <remarks>
    /// The pipeline is deliberately thin. Anything that rewrites messages, caches responses or
    /// invokes tools would sit between the loop and the model and corrupt the measurements.
    /// </remarks>
    public static IServiceCollection AddMovieAgentLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .PostConfigure(options =>
            {
                // Convention: let the standard environment variable stand in for the API key
                // so it never has to live in appsettings.json.
                options.OpenAI.ApiKey = string.IsNullOrWhiteSpace(options.OpenAI.ApiKey)
                    ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    : options.OpenAI.ApiKey;
            })
            .ValidateSelf();

        // Wire capture is a singleton so a whole run's exchanges accumulate in one place, and is
        // registered even when disabled so nothing downstream has to null-check.
        var captureEnabled = configuration.GetValue<bool>("Agent:CaptureWireTraffic");
        if (captureEnabled)
        {
            services.AddSingleton<IWireCapture>(new WireCapture());
            services.AddTransient<WireCaptureHandler>();
        }
        else
        {
            services.AddSingleton<IWireCapture, NullWireCapture>();
        }

        var ollamaClient = services.AddHttpClient(OllamaChatClientProvider.HttpClientName, (serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<LlmOptions>>().Value.Ollama;
            httpClient.BaseAddress = new Uri(options.Endpoint);
            httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        if (captureEnabled)
        {
            ollamaClient.AddHttpMessageHandler<WireCaptureHandler>();
        }

        services.AddSingleton<IChatClientProvider, OllamaChatClientProvider>();
        services.AddSingleton<IChatClientProvider, OpenAIChatClientProvider>();

        services
            .AddChatClient(serviceProvider =>
            {
                var selected = serviceProvider.GetRequiredService<IOptions<LlmOptions>>().Value.Provider;

                var provider = serviceProvider
                    .GetServices<IChatClientProvider>()
                    .FirstOrDefault(p => p.Provider == selected)
                    ?? throw new InvalidOperationException(
                        $"No {nameof(IChatClientProvider)} registered for '{selected}'.");

                return provider.Create();
            })
            // NO UseFunctionInvocation(). MovieAgent.Agent.AgentLoop drives the tool loop by
            // hand so that every call, its raw arguments and its exact result can be recorded.
            // Adding it back would silently execute tools outside the recorder and make the
            // iteration counts meaningless.
            .UseLogging();

        return services;
    }
}
