using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieAgent.Core.Configuration;
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

    public OpenAIChatClientProvider(IOptions<LlmOptions> options, ILogger<OpenAIChatClientProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public LlmProvider Provider => LlmProvider.OpenAI;

    public IChatClient Create()
    {
        var openAI = _options.OpenAI;
        var credential = new ApiKeyCredential(openAI.ApiKey!);

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(openAI.Endpoint))
        {
            clientOptions.Endpoint = new Uri(openAI.Endpoint);
        }

        _logger.LogInformation(
            "Using OpenAI chat model '{Model}' at {Endpoint}.",
            openAI.Model,
            clientOptions.Endpoint?.ToString() ?? "https://api.openai.com/v1");

        return new OpenAIClient(credential, clientOptions)
            .GetChatClient(openAI.Model)
            .AsIChatClient();
    }
}
