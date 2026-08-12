using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using MovieAgent.Core.Abstractions;

namespace MovieAgent.Llm.Diagnostics;

public sealed class WireCapture : IWireCapture
{
    private readonly List<WireExchange> _exchanges = [];
    private readonly List<(string Request, string Response)> _bodies = [];
    private readonly Lock _gate = new();

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<WireExchange> Exchanges
    {
        get
        {
            lock (_gate)
            {
                return [.. _exchanges];
            }
        }
    }

    public string? FirstRequestBody { get; private set; }

    public IReadOnlyList<(string Request, string Response)> Bodies
    {
        get
        {
            lock (_gate)
            {
                return [.. _bodies];
            }
        }
    }

    public void Record(WireExchange exchange, string requestBody, string responseBody)
    {
        lock (_gate)
        {
            FirstRequestBody ??= requestBody;
            _exchanges.Add(exchange);
            _bodies.Add((requestBody, responseBody));
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _exchanges.Clear();
            _bodies.Clear();
            FirstRequestBody = null;
        }
    }
}

/// <summary>
/// Buffers and hashes both directions of every model call.
/// </summary>
/// <remarks>
/// Buffering the response defeats streaming, which is why this is opt-in via
/// <c>Agent:CaptureWireTraffic</c> and off for measurement runs.
/// </remarks>
public sealed class WireCaptureHandler : DelegatingHandler
{
    private readonly IWireCapture _capture;

    public WireCaptureHandler(IWireCapture capture)
    {
        _capture = capture;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_capture.Enabled)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var requestBody = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);

        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // Put the body back so the SDK can still read it.
        var replacement = new ByteArrayContent(responseBytes);
        foreach (var header in response.Content.Headers)
        {
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = replacement;

        _capture.Record(
            new WireExchange(
                Sha256(Encoding.UTF8.GetBytes(requestBody)),
                Sha256(responseBytes),
                Encoding.UTF8.GetByteCount(requestBody),
                responseBytes.Length),
            requestBody,
            Encoding.UTF8.GetString(responseBytes));

        return response;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
