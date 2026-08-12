using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieAgent.Core.Configuration;
using OllamaSharp;

namespace MovieAgent.Llm.Providers;

/// <summary>
/// Local Ollama, via OllamaSharp. <see cref="OllamaApiClient"/> implements
/// <see cref="IChatClient"/> directly, so no adapter is needed.
/// </summary>
public sealed class OllamaChatClientProvider : IChatClientProvider
{
    /// <summary>Named client so its long timeout does not leak into unrelated HTTP usage.</summary>
    public const string HttpClientName = "ollama";

    private readonly LlmOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaChatClientProvider> _logger;

    public OllamaChatClientProvider(
        IOptions<LlmOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaChatClientProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public LlmProvider Provider => LlmProvider.Ollama;

    public IChatClient Create()
    {
        var ollama = _options.Ollama;

        _logger.LogInformation("Using Ollama chat model '{Model}' at {Endpoint}.", ollama.Model, ollama.Endpoint);

        return new OllamaApiClient(_httpClientFactory.CreateClient(HttpClientName), ollama.Model);
    }
}
