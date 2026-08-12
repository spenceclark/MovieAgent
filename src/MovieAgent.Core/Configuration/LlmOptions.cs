namespace MovieAgent.Core.Configuration;

/// <summary>
/// Which concrete SDK backs the <see cref="Microsoft.Extensions.AI.IChatClient"/> abstraction.
/// </summary>
public enum LlmProvider
{
    /// <summary>Local (or self-hosted) Ollama, via OllamaSharp.</summary>
    Ollama = 0,

    /// <summary>Hosted OpenAI, via the official OpenAI SDK.</summary>
    OpenAI = 1,
}

/// <summary>
/// Root LLM configuration. Only the section matching <see cref="Provider"/> is validated.
/// </summary>
public sealed class LlmOptions : IValidatableOptions
{
    public const string SectionName = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.Ollama;

    public OpenAIOptions OpenAI { get; set; } = new();

    public OllamaOptions Ollama { get; set; } = new();

    public IEnumerable<string> GetValidationErrors() => Provider switch
    {
        LlmProvider.OpenAI => OpenAI.GetValidationErrors(),
        LlmProvider.Ollama => Ollama.GetValidationErrors(),
        _ => [$"'{SectionName}:{nameof(Provider)}' value '{Provider}' is not supported."],
    };
}

public sealed class OpenAIOptions : IValidatableOptions
{
    /// <summary>
    /// API key. Prefer user secrets or the OPENAI_API_KEY environment variable over appsettings.json.
    /// </summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Optional override for OpenAI-compatible gateways. Leave null for api.openai.com.</summary>
    public string? Endpoint { get; set; }

    public IEnumerable<string> GetValidationErrors()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return $"'{LlmOptions.SectionName}:OpenAI:ApiKey' (or the OPENAI_API_KEY environment variable) " +
                         $"is required when '{LlmOptions.SectionName}:Provider' is '{nameof(LlmProvider.OpenAI)}'.";
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            yield return $"'{LlmOptions.SectionName}:OpenAI:Model' is required.";
        }

        if (!string.IsNullOrWhiteSpace(Endpoint) && !Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            yield return $"'{LlmOptions.SectionName}:OpenAI:Endpoint' must be an absolute URI. Got '{Endpoint}'.";
        }
    }
}

public sealed class OllamaOptions : IValidatableOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2";

    /// <summary>Ollama can be slow to first token while a cold model loads.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    public IEnumerable<string> GetValidationErrors()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            yield return $"'{LlmOptions.SectionName}:Ollama:Endpoint' must be an absolute URI. Got '{Endpoint}'.";
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            yield return $"'{LlmOptions.SectionName}:Ollama:Model' is required.";
        }

        if (TimeoutSeconds <= 0)
        {
            yield return $"'{LlmOptions.SectionName}:Ollama:TimeoutSeconds' must be greater than zero.";
        }
    }
}
