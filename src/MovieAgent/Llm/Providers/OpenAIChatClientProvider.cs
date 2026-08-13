using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieAgent.Agent.Configuration;
using OpenAI;

namespace MovieAgent.Llm.Providers;

/// <summary>
/// Real OpenAI, via the official OpenAI SDK, surfaced as <see cref="IChatClient"/> by
/// Microsoft.Extensions.AI.OpenAI.
/// </summary>
public sealed class OpenAIChatClientProvider : IChatClientProvider
{
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAIChatClientProvider> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public OpenAIChatClientProvider(
        IOptions<LlmOptions> options,
        ILogger<OpenAIChatClientProvider> logger,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public LlmProvider Provider => LlmProvider.OpenAI;

    public IChatClient Create()
    {
        var openAI = _options.OpenAI;
        var credential = new ApiKeyCredential(openAI.ApiKey!);

        var clientOptions = new OpenAIClientOptions
        {
            // The SDK already retries 429/408/500/502/503/504 with exponential backoff and
            // honours Retry-After — this just raises the attempt count above the SDK default
            // of 3 (too few for a real per-minute rate-limit window) and turns on its logging,
            // which is off (routed to EventSource only) unless a logger factory is supplied.
            RetryPolicy = new ClientRetryPolicy(
                maxRetries: openAI.RetryMaxAttempts,
                enableLogging: true,
                loggerFactory: _loggerFactory),
        };
        if (!string.IsNullOrWhiteSpace(openAI.Endpoint))
        {
            clientOptions.Endpoint = new Uri(openAI.Endpoint);
        }

        _logger.LogInformation(
            "Using OpenAI chat model '{Model}' at {Endpoint} (retry up to {RetryMaxAttempts}x on 429/5xx).",
            openAI.Model,
            clientOptions.Endpoint?.ToString() ?? "https://api.openai.com/v1",
            openAI.RetryMaxAttempts);

        return new OpenAIClient(credential, clientOptions)
            .GetChatClient(openAI.Model)
            .AsIChatClient();
    }
}
