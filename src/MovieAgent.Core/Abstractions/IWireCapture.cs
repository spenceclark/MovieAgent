namespace MovieAgent.Core.Abstractions;

public sealed record WireExchange(string RequestSha256, string ResponseSha256, int RequestBytes, int ResponseBytes);

/// <summary>
/// Records a hash of the exact HTTP request and response bodies exchanged with the model.
/// </summary>
/// <remarks>
/// The point is to settle determinism questions with bytes rather than inference. If two runs
/// produce different answers, the first thing to establish is whether they were even sent the
/// same request — and the assembled request is not something the harness can reconstruct after
/// the fact, because provider SDKs add fields, reorder keys and generate identifiers of their
/// own on the way out.
/// </remarks>
public interface IWireCapture
{
    /// <summary>Off by default: capturing buffers whole bodies and is a diagnostic, not a mode.</summary>
    bool Enabled { get; }

    /// <summary>The exchanges recorded since the last <see cref="Reset"/>, in order.</summary>
    IReadOnlyList<WireExchange> Exchanges { get; }

    /// <summary>The first request body verbatim, kept so the outbound parameters can be eyeballed.</summary>
    string? FirstRequestBody { get; }

    /// <summary>
    /// Every body exchanged, in order. A hash of a whole body is not enough to settle a
    /// determinism question: Ollama's response envelope carries per-call timing fields, so two
    /// byte-identical generations still hash differently.
    /// </summary>
    IReadOnlyList<(string Request, string Response)> Bodies { get; }

    void Record(WireExchange exchange, string requestBody, string responseBody);

    void Reset();
}

/// <summary>Used when capture is switched off, so nothing has to null-check.</summary>
public sealed class NullWireCapture : IWireCapture
{
    public bool Enabled => false;

    public IReadOnlyList<WireExchange> Exchanges => [];

    public string? FirstRequestBody => null;

    public IReadOnlyList<(string Request, string Response)> Bodies => [];

    public void Record(WireExchange exchange, string requestBody, string responseBody)
    {
    }

    public void Reset()
    {
    }
}
