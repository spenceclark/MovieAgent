using Microsoft.Extensions.AI;
using MovieAgent.Agent.Configuration;

namespace MovieAgent.Agent;

public sealed class AgentOptions : IValidatableOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// Hard cap on model turns. Hitting it terminates the run cleanly with
    /// <see cref="Recording.RunOutcome.IterationCapReached"/> — that is data about the model,
    /// not a harness failure.
    /// </summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>Which surface to advertise. One of minimal, standard, enriched.</summary>
    public string ToolSurface { get; set; } = "standard";

    /// <summary>Fixed seed makes runs reproducible; null lets the provider choose.</summary>
    public long? Seed { get; set; }

    public bool Thinking { get; set; } = false;

    public float? Temperature { get; set; }

    /// <summary>
    /// Caps generated tokens per turn, via <see cref="ChatOptions.MaxOutputTokens"/> — on
    /// Ollama, this is <c>num_predict</c>. Null lets the provider/model default apply.
    /// </summary>
    public int? MaxOutputTokens { get; set; } = 2500;

    /// <summary>
    /// Send a reasoning-effort parameter at all. Off for models whose API rejects it.
    /// </summary>
    /// <remarks>
    /// gpt-4o and gpt-4o-mini return <c>HTTP 400 Unrecognized request argument supplied:
    /// reasoning_effort</c>, because effort is a reasoning-model parameter and they are not
    /// reasoning models. <see cref="ToReasoningOptions"/> otherwise always returns an object —
    /// <c>Effort = None</c> is still an explicit instruction, not an absence — so the parameter
    /// goes on every request.
    /// <para>
    /// This exists as a recorded option rather than a local edit because the two gpt-4o rows in
    /// the results were originally produced by patching the line out by hand. That made them
    /// unreproducible from the repository and left no trace in their JSONL of a run variable that
    /// changes the request. Every other such variable is on the record; this one now is too.
    /// </para>
    /// <para>
    /// <b>Not a general "turn reasoning off" switch.</b> On Ollama the same options object becomes
    /// <c>think</c>, and an absent <c>think</c> is not the same as <c>think:false</c> — a thinking
    /// model falls back to its own default, which is usually on. Leave this true for Ollama.
    /// </para>
    /// </remarks>
    public bool SendReasoningEffort { get; set; } = true;

    /// <summary>
    /// The <see cref="ReasoningOptions"/> equivalent of <see cref="Thinking"/>, in one place so
    /// every caller that builds a <see cref="ChatOptions"/> — the agent loop, the connectivity
    /// check — constructs it identically. A second, independently-written ternary is how a
    /// probe ends up silently exercising a different reasoning setting than real runs use.
    /// Null when <see cref="SendReasoningEffort"/> is off, so no parameter is sent at all.
    /// </summary>
    public ReasoningOptions? ToReasoningOptions() =>
        SendReasoningEffort
            ? new ReasoningOptions { Effort = Thinking ? ReasoningEffort.Medium : ReasoningEffort.None }
            : null;

    /// <summary>
    /// Fold each turn's reasoning text into the message's ordinary content before it re-enters
    /// history, so it is still there — as plain text, not as Ollama's native <c>thinking</c>
    /// field — the next time this conversation is sent.
    /// </summary>
    /// <remarks>
    /// Exists because <c>Agent:Thinking</c> alone does not give the model continuity of
    /// thought: verified against the raw wire traffic that OllamaSharp's outbound mapper never
    /// writes <c>Message.Thinking</c> from history, so reasoning is generated and discarded
    /// every iteration regardless of this setting (see <c>RunRecord.ReasoningText</c> remarks).
    /// <para>
    /// This is the cheap, approximate fix, not the faithful one. Qwen's own documented
    /// convention for multi-step tool calls is to replay reasoning via <c>preserve_thinking</c>
    /// — the model's native reasoning field, round-tripped in the shape its chat template
    /// expects. Doing that means reaching past <see cref="Microsoft.Extensions.AI.IChatClient"/>
    /// into Ollama-specific types, which every other line of this harness deliberately avoids.
    /// This setting instead re-injects the same text as ordinary <see cref="Microsoft.Extensions.AI.TextContent"/>,
    /// which stays inside the abstraction but may not reproduce what the model was fine-tuned
    /// to expect. Test this first because it is free; reach for the native mechanism only if
    /// this does not move the two loops it was built to fix.
    /// </para>
    /// </remarks>
    public bool ReplayThinking { get; set; } = false;

    /// <summary>Runs per question, for measuring run-to-run variance.</summary>
    public int Repeats { get; set; } = 1;

    /// <summary>
    /// When true, a tool call identical to one already made in this run is not executed; the
    /// model is told it has already made that call and what it returned.
    /// </summary>
    /// <remarks>
    /// An experimental variable, not a fix. qwen3.5:4b burned entire runs repeating one
    /// byte-identical call, so whether telling it so rescues the run is worth measuring both
    /// ways. Recorded per call as <c>was_repeat</c>, so repetition rate stays visible in the
    /// data whichever way this is set.
    /// </remarks>
    public bool BlockRepeatedToolCalls { get; set; } = true;

    /// <summary>
    /// Replace the provider's tool-call identifiers with a deterministic sequence before they
    /// enter the conversation history.
    /// </summary>
    /// <remarks>
    /// Measured, not guessed. Ollama returns a random id per tool call — call_0emx57jo,
    /// call_n4n6tq1o, call_si4fb040 across three otherwise byte-identical runs. Echoing it back
    /// in the tool result puts random tokens into the context, so every request from iteration 2
    /// onward differs and the model samples from a different sequence. The generation itself was
    /// identical: same tool, same arguments, same eval_count.
    /// <para>
    /// Left as a switch because the run-to-run variance already measured was taken with it off,
    /// and turning it on invalidates comparison against those runs.
    /// </para>
    /// </remarks>
    public bool NormaliseToolCallIds { get; set; } = true;

    /// <summary>
    /// Unwrap Ollama tool messages so the model reads the tool output verbatim, instead of a JSON
    /// serialisation of the adapter's content object.
    /// </summary>
    /// <remarks>
    /// Measured on the wire: OllamaSharp serialises the whole content object into the tool
    /// message, so the model reads <c>{"CallId":"call_1","Result":"film_id | title ..."}</c> on a
    /// single line with every newline escaped, where the harness emitted a three-line
    /// pipe-delimited table. The same result through the OpenAI SDK arrives as raw text with real
    /// newlines and the id in its own <c>tool_call_id</c> field. The frozen output contract
    /// therefore reached hosted models intact and local models mangled.
    /// <para>
    /// Off by default: switching it on changes what the model is sent, so it invalidates
    /// comparison against every run already recorded. Turn it on for new sweeps, or to A/B the
    /// effect on an existing one.
    /// </para>
    /// </remarks>
    public bool RepairOllamaToolMessages { get; set; }

    public IEnumerable<string> GetValidationErrors()
    {
        if (MaxIterations is < 1 or > 100)
        {
            yield return $"'{SectionName}:{nameof(MaxIterations)}' must be between 1 and 100.";
        }

        if (Repeats < 1)
        {
            yield return $"'{SectionName}:{nameof(Repeats)}' must be at least 1.";
        }

        if (!Tools.ToolSurfaces.ByName.ContainsKey(ToolSurface))
        {
            yield return
                $"'{SectionName}:{nameof(ToolSurface)}' is '{ToolSurface}'. " +
                $"Known surfaces: {string.Join(", ", Tools.ToolSurfaces.ByName.Keys)}.";
        }

        if (Temperature is < 0 or > 2)
        {
            yield return $"'{SectionName}:{nameof(Temperature)}' must be between 0 and 2.";
        }

        if (MaxOutputTokens is <= 0)
        {
            yield return $"'{SectionName}:{nameof(MaxOutputTokens)}' must be greater than zero, or unset for no cap.";
        }
    }
}
