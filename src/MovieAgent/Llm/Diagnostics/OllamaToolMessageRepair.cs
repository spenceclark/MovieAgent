using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MovieAgent.Llm.Diagnostics;

/// <summary>
/// Rewrites outbound Ollama tool messages so the model reads the tool output the harness actually
/// produced, instead of a JSON serialisation of the adapter's own content object.
/// </summary>
/// <remarks>
/// <b>This corrects an adapter defect, measured on the wire.</b> OllamaSharp maps
/// <see cref="Microsoft.Extensions.AI.FunctionResultContent"/> by serialising the whole object into
/// the tool message's <c>content</c>, so a model sees
/// <c>{"CallId":"call_1","Result":"film_id | title\n11 | ALAMO VIDEOTAPE\n1 rows"}</c> — one line,
/// with the newlines escaped — where the harness emitted a three-line pipe-delimited table. The
/// same run through the OpenAI SDK sends <c>content</c> as the raw text with real newlines and the
/// id in its own <c>tool_call_id</c> field. So the frozen output contract reached hosted models
/// intact and local models mangled, which is a difference between providers that has nothing to do
/// with the models.
/// <para>
/// It sits on the named Ollama <see cref="HttpClient"/> rather than in the agent loop on purpose:
/// the loop goes through <see cref="Microsoft.Extensions.AI.IChatClient"/> and must stay ignorant
/// of which provider is behind it. This is the same place <see cref="WireCaptureHandler"/> lives,
/// for the same reason.
/// </para>
/// <para>
/// Opt-in via <c>Agent:RepairOllamaToolMessages</c> and off by default, because turning it on
/// changes what the model is sent and therefore invalidates comparison against every run already
/// recorded. Whether it changes scores is an empirical question, not an obvious one.
/// </para>
/// </remarks>
public sealed class OllamaToolMessageRepairHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            var repaired = Repair(body);

            if (repaired is not null)
            {
                var mediaType = request.Content.Headers.ContentType?.MediaType ?? "application/json";
                request.Content = new StringContent(repaired, Encoding.UTF8, mediaType);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Returns the rewritten body, or null when there was nothing to change.
    /// </summary>
    internal static string? Repair(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root?["messages"] is not JsonArray messages)
        {
            return null;
        }

        var changed = false;

        foreach (var message in messages)
        {
            if (message is not JsonObject obj
                || obj["role"]?.GetValue<string>() != "tool"
                || obj["content"] is not JsonValue value
                || value.TryGetValue<string>(out var content) is false
                || content is null)
            {
                continue;
            }

            // Only unwrap the exact shape the adapter produces. Anything else is left alone —
            // a tool result that legitimately contains JSON must not be mangled.
            JsonNode? inner;
            try
            {
                inner = JsonNode.Parse(content);
            }
            catch (JsonException)
            {
                continue;
            }

            if (inner is not JsonObject wrapper
                || wrapper["Result"] is not JsonValue resultValue
                || resultValue.TryGetValue<string>(out var text) is false
                || text is null)
            {
                continue;
            }

            obj["content"] = text;

            // Ollama's own schema carries the call id on the tool message; the adapter drops it
            // and buries it in the blob instead. Put it back where the API expects it.
            if (wrapper["CallId"] is JsonValue callId && callId.TryGetValue<string>(out var id) && id is not null)
            {
                obj["tool_call_id"] = id;
            }

            changed = true;
        }

        return changed ? root!.ToJsonString() : null;
    }
}
