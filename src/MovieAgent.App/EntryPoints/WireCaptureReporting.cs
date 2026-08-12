using System.Text.Json;
using MovieAgent.Core.Abstractions;

namespace MovieAgent.App.EntryPoints;

/// <summary>
/// Shared between every entry point that surfaces <see cref="IWireCapture"/> data, so a run's
/// worth of request/response bodies is always written and printed the same way.
/// </summary>
public static class WireCaptureReporting
{
    /// <summary>
    /// Writes every captured exchange to <paramref name="directory"/> as
    /// <c>NN-request.json</c>/<c>NN-response.json</c> pairs, one per HTTP exchange in order.
    /// Overwrites whatever was there — these are a snapshot of the run just made, not a log to
    /// append to.
    /// </summary>
    public static async Task DumpBodiesAsync(IWireCapture capture, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);

        for (var i = 0; i < capture.Bodies.Count; i++)
        {
            var (requestBody, responseBody) = capture.Bodies[i];
            await File.WriteAllTextAsync(Path.Combine(directory, $"{i + 1:00}-request.json"), requestBody, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{i + 1:00}-response.json"), responseBody, cancellationToken);
        }
    }

    /// <summary>Prints a request body with the message array elided — the parameters are the point.</summary>
    public static string SummariseRequest(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "  (not captured)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var lines = document.RootElement.EnumerateObject()
                .Select(p => p.NameEquals("messages")
                    ? $"  messages: [{p.Value.GetArrayLength()} messages, elided]"
                    : p.NameEquals("tools")
                        ? $"  tools: [{p.Value.GetArrayLength()} tools, elided]"
                        : $"  {p.Name}: {p.Value.GetRawText()}");

            return string.Join(Environment.NewLine, lines);
        }
        catch (JsonException)
        {
            return "  " + body[..Math.Min(500, body.Length)];
        }
    }
}
