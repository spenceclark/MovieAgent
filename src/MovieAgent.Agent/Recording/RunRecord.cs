using System.Text.Json.Serialization;

namespace MovieAgent.Agent.Recording;

/// <summary>How a run ended. Distinguishable in the data, which is the point.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunOutcome>))]
public enum RunOutcome
{
    /// <summary>The model stopped calling tools and produced a final answer.</summary>
    Answered,

    /// <summary>The iteration cap was hit. Not an error — a measurement.</summary>
    IterationCapReached,

    /// <summary>The harness or the model endpoint failed. Excluded from accuracy denominators.</summary>
    Errored,

    /// <summary>
    /// The model stopped calling tools but its final message was blank. Distinct from a
    /// substantive wrong answer — before this existed, both pooled under <see cref="Answered"/>
    /// and were indistinguishable without opening the transcript.
    /// </summary>
    EmptyAnswer,
}

/// <summary>
/// Reclassifies a recorded <see cref="RunOutcome"/> using only fields already on the run, so an
/// old JSONL regrades to the sharper distinction without re-running anything.
/// </summary>
public static class RunOutcomeClassifier
{
    public static RunOutcome Effective(RunOutcome recorded, string? finalAnswer) =>
        recorded == RunOutcome.Answered && string.IsNullOrWhiteSpace(finalAnswer)
            ? RunOutcome.EmptyAnswer
            : recorded;
}

/// <summary>
/// One tool call, recorded exactly as it happened.
/// </summary>
public sealed record ToolCallRecord
{
    [JsonPropertyName("iteration")] public required int Iteration { get; init; }

    [JsonPropertyName("tool_name")] public required string ToolName { get; init; }

    /// <summary>
    /// The provider's identifier for this call. Recorded because a randomly generated call id
    /// enters the conversation history and changes the bytes of every later request.
    /// </summary>
    [JsonPropertyName("call_id")] public string? CallId { get; init; }

    /// <summary>
    /// Arguments as the model sent them, before any coercion. Kept raw so that malformed
    /// calls stay visible in the data instead of being normalised away.
    /// </summary>
    [JsonPropertyName("arguments_raw")] public required string ArgumentsRaw { get; init; }

    /// <summary>The exact text handed back to the model.</summary>
    [JsonPropertyName("result_text")] public required string ResultText { get; init; }

    [JsonPropertyName("rows_returned")] public required int RowsReturned { get; init; }

    [JsonPropertyName("is_error")] public required bool IsError { get; init; }

    /// <summary>
    /// This call was byte-identical to an earlier one in the same run. Recorded whether or not
    /// <see cref="AgentOptions.BlockRepeatedToolCalls"/> intercepted it, so repetition rate is
    /// measurable across both settings.
    /// </summary>
    [JsonPropertyName("was_repeat")] public bool WasRepeat { get; init; }

    /// <summary>True when the call was intercepted rather than executed against the database.</summary>
    [JsonPropertyName("blocked")] public bool Blocked { get; init; }

    [JsonPropertyName("elapsed_ms")] public required long ElapsedMilliseconds { get; init; }
}

/// <summary>
/// One turn of the loop: what the model produced, what it cost, and what it called.
/// </summary>
/// <remarks>
/// Token counts sit at this level rather than on <see cref="ToolCallRecord"/> because the
/// provider reports usage per model turn, not per tool call. A turn may contain several
/// tool calls; attributing tokens to individual calls would be a fabrication.
/// </remarks>
public sealed record IterationRecord
{
    [JsonPropertyName("iteration")] public required int Iteration { get; init; }

    [JsonPropertyName("input_tokens")] public long? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")] public long? OutputTokens { get; init; }

    [JsonPropertyName("elapsed_ms")] public required long ElapsedMilliseconds { get; init; }

    [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }

    /// <summary>Any prose the model emitted this turn. Often its stated reasoning.</summary>
    [JsonPropertyName("assistant_text")] public string? AssistantText { get; init; }

    /// <summary>
    /// Extended-thinking output for this turn (Ollama's <c>message.thinking</c>, mapped to
    /// <see cref="Microsoft.Extensions.AI.TextReasoningContent"/>), when <c>Agent:Thinking</c>
    /// is on. Distinct from <see cref="AssistantText"/>, which is the model's ordinary reply
    /// text and excludes this.
    /// </summary>
    /// <remarks>
    /// Recorded so "how much reasoning did this run generate" is answerable from the JSONL, in
    /// support of a finding worth stating plainly: <b>this reasoning is not carried into the
    /// next iteration.</b> Verified against the raw wire traffic (OllamaSharp 5.4.30,
    /// Microsoft.Extensions.AI 10.8.3) — <c>TextReasoningContent</c> is produced correctly on
    /// the way in from Ollama's response, survives unmodified in this harness's own in-memory
    /// message list, and is then silently dropped by OllamaSharp's outbound mapper: the
    /// re-sent assistant message in the next request carries no <c>thinking</c> field and no
    /// trace of the reasoning text anywhere in the request body, even though
    /// <c>OllamaSharp.Models.Chat.Message.Thinking</c> exists as a settable property on the
    /// request-side type and the wire protocol has no apparent objection to it being set. This
    /// is a round-trip gap in the M.E.AI/OllamaSharp adapter, not a limitation of Ollama's API
    /// and not something this harness's code controls — it goes through
    /// <see cref="Microsoft.Extensions.AI.IChatClient"/> exactly as the rest of the pipeline
    /// does, by design (see <see cref="AgentLoop"/> remarks). With <c>Agent:Thinking</c> on,
    /// each iteration re-derives its plan from tool-call history alone; the reasoning text is
    /// generated, paid for in tokens, and thrown away every single turn.
    /// </remarks>
    [JsonPropertyName("reasoning_text")] public string? ReasoningText { get; init; }

    /// <summary>
    /// SHA-256 of the exact HTTP request body sent for this turn. Present only when
    /// <c>Agent:CaptureWireTraffic</c> is on.
    /// </summary>
    /// <remarks>
    /// Two runs that answer differently are only interesting if they were sent the same bytes.
    /// This is the only way to establish that: the assembled request cannot be reconstructed
    /// afterwards, because the SDK adds fields and generates identifiers on the way out.
    /// </remarks>
    [JsonPropertyName("request_sha256")] public string? RequestSha256 { get; init; }

    [JsonPropertyName("response_sha256")] public string? ResponseSha256 { get; init; }

    /// <summary>
    /// SHA-256 of what the model actually generated this turn: its text plus each tool call's
    /// name and arguments. Excludes tool-call identifiers and provider timing fields.
    /// </summary>
    /// <remarks>
    /// This, not <see cref="ResponseSha256"/>, is the determinism signal. A raw response-body
    /// hash always differs, because Ollama's envelope carries created_at and four duration
    /// fields — it reports non-determinism on a generation that was identical token for token.
    /// Recorded unconditionally, so determinism is checkable in any run without wire capture.
    /// </remarks>
    [JsonPropertyName("content_sha256")] public string? ContentSha256 { get; init; }

    [JsonPropertyName("tool_calls")] public required IReadOnlyList<ToolCallRecord> ToolCalls { get; init; }
}

/// <summary>
/// One line of the JSONL dataset. Self-contained on purpose: every line carries the full run
/// configuration so that analysis never has to join against a separate config file, and so a
/// mixed file of runs from different configurations is still analysable.
/// </summary>
public sealed record RunRecord
{
    [JsonPropertyName("run_id")] public required string RunId { get; init; }

    [JsonPropertyName("started_at")] public required DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("question_id")] public required string QuestionId { get; init; }

    [JsonPropertyName("question")] public required string Question { get; init; }

    /// <summary>Hop depth the question was designed to require. Null for ad-hoc questions.</summary>
    [JsonPropertyName("expected_hops")] public int? ExpectedHops { get; init; }

    [JsonPropertyName("tool_surface")] public required string ToolSurface { get; init; }

    [JsonPropertyName("tool_names")] public required IReadOnlyList<string> ToolNames { get; init; }

    [JsonPropertyName("provider")] public required string Provider { get; init; }

    [JsonPropertyName("model")] public required string Model { get; init; }

    [JsonPropertyName("seed")] public long? Seed { get; init; }

    [JsonPropertyName("temperature")] public float? Temperature { get; init; }

    /// <summary>Cap on generated tokens per turn, from Agent:MaxOutputTokens. Null means uncapped.</summary>
    [JsonPropertyName("max_output_tokens")] public int? MaxOutputTokens { get; init; }

    /// <summary>Whether extended reasoning was requested for this run, from Agent:Thinking.</summary>
    [JsonPropertyName("thinking")] public required bool Thinking { get; init; }

    /// <summary>Whether reasoning text was replayed into history, from Agent:ReplayThinking.</summary>
    [JsonPropertyName("replay_thinking")] public required bool ReplayThinking { get; init; }

    /// <summary>
    /// Whether provider tool-call identifiers were rewritten to a deterministic sequence for this
    /// run (<see cref="AgentOptions.NormaliseToolCallIds"/>).
    /// </summary>
    /// <remarks>
    /// Recorded because it is a run variable that changes what the model sees, not a cosmetic
    /// one: an un-normalised id is echoed back into the conversation and puts random tokens in
    /// the context. Every other such variable — seed, temperature, thinking, the caps — is on the
    /// record; this one was not, which made it impossible to tell from the corpus alone which
    /// runs had it on.
    /// </remarks>
    [JsonPropertyName("normalise_tool_call_ids")] public bool NormaliseToolCallIds { get; init; }

    /// <summary>Repeat index when the same question is run several times to measure variance.</summary>
    [JsonPropertyName("repeat")] public required int Repeat { get; init; }

    [JsonPropertyName("system_prompt")] public required string SystemPrompt { get; init; }

    [JsonPropertyName("system_prompt_sha256")] public required string SystemPromptSha256 { get; init; }

    /// <summary>Version of the frozen tool output contract these results were produced under.</summary>
    [JsonPropertyName("output_format_version")] public required string OutputFormatVersion { get; init; }

    [JsonPropertyName("max_iterations")] public required int MaxIterations { get; init; }

    [JsonPropertyName("outcome")] public required RunOutcome Outcome { get; init; }

    [JsonPropertyName("cap_hit")] public required bool CapHit { get; init; }

    [JsonPropertyName("final_answer")] public string? FinalAnswer { get; init; }

    [JsonPropertyName("iteration_count")] public required int IterationCount { get; init; }

    [JsonPropertyName("tool_call_count")] public required int ToolCallCount { get; init; }

    [JsonPropertyName("total_input_tokens")] public long? TotalInputTokens { get; init; }

    [JsonPropertyName("total_output_tokens")] public long? TotalOutputTokens { get; init; }

    [JsonPropertyName("elapsed_ms")] public required long ElapsedMilliseconds { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }

    [JsonPropertyName("iterations")] public required IReadOnlyList<IterationRecord> Iterations { get; init; }

    /// <summary>Grading, filled in by MovieAgent.Evaluation. Null for ungraded ad-hoc runs.</summary>
    [JsonPropertyName("grade")] public GradeRecord? Grade { get; init; }
}

public sealed record GradeRecord
{
    [JsonPropertyName("expected_answer")] public string? ExpectedAnswer { get; init; }

    [JsonPropertyName("expected_behaviour")] public required string ExpectedBehaviour { get; init; }

    [JsonPropertyName("correct")] public required bool Correct { get; init; }

    [JsonPropertyName("declined")] public required bool Declined { get; init; }

    [JsonPropertyName("method")] public required string Method { get; init; }

    [JsonPropertyName("note")] public string? Note { get; init; }

    /// <summary>
    /// Navigation, scored separately from the answer.
    /// </summary>
    /// <remarks>
    /// "Reached the right rows but did not resolve the last identifier to a name" and "went
    /// somewhere else entirely" both score zero on <see cref="Correct"/>, and only one of them
    /// is interesting. This measures the first thing objectively, with no extra judgement: did
    /// the run successfully call each tool the question's shortest correct chain needs.
    /// <para>
    /// It is a necessary condition, not a sufficient one — calling get_customer proves nothing
    /// about the argument passed to it. Read it as a floor on navigation, not a score for it.
    /// </para>
    /// </remarks>
    [JsonPropertyName("required_tools")] public IReadOnlyList<string> RequiredTools { get; init; } = [];

    [JsonPropertyName("required_tools_missing")] public IReadOnlyList<string> RequiredToolsMissing { get; init; } = [];

    /// <summary>
    /// Null when the surface has no notion of navigation — the <c>sql-shortcut</c> control, where
    /// one generic tool answers everything. Null means "not applicable"; false would read as a
    /// failure to reach a required tool, which is a different claim.
    /// </summary>
    [JsonPropertyName("navigation_complete")] public bool? NavigationComplete { get; init; }

    /// <summary>False for qualitative exhibits, which are excluded from accuracy denominators.</summary>
    [JsonPropertyName("scored")] public bool Scored { get; init; } = true;

    /// <summary>Did any tool call in the run truncate, and if so what total did it state.</summary>
    /// <remarks>
    /// A correct numeric answer to a truncated-list question is ambiguous on its own: the model
    /// may have read the count line, or landed on the right number some other way. This is not
    /// provable either way from the outside, but a partial check is available and is worth
    /// recording — the true total is otherwise present nowhere in the data the model can see
    /// except this line, so a stated total that matches it is at least consistent with having
    /// read it, and a mismatch is proof the answer did not come from there.
    /// </remarks>
    [JsonPropertyName("truncation_seen")] public bool TruncationSeen { get; init; }

    [JsonPropertyName("truncation_stated_total")] public int? TruncationStatedTotal { get; init; }

    /// <summary>Null when no truncation was seen, or when the run produced no final answer to check.</summary>
    [JsonPropertyName("answer_matches_stated_total")] public bool? AnswerMatchesStatedTotal { get; init; }

    /// <summary>
    /// Arguments the model invented rather than derived — see
    /// <c>MovieAgent.Evaluation.ToolCallDiagnostics</c> for exactly what counts. Includes call
    /// ids passed off as data (<see cref="CallIdAsArgumentCount"/> is the same calls, counted
    /// again on their own axis since that failure mode is harness-caused, not a model
    /// hallucination in the usual sense).
    /// </summary>
    [JsonPropertyName("fabricated_argument_count")] public int? FabricatedArgumentCount { get; init; }

    /// <summary>
    /// Fabricated values sent for an id parameter — the model asserting that a specific row
    /// exists. This is the hallucination the metric exists to catch, and the number to read.
    /// </summary>
    [JsonPropertyName("fabricated_id_count")] public int? FabricatedIdCount { get; init; }

    /// <summary>
    /// Fabricated values sent for a search-term parameter. Usually not a fault: a model hunting
    /// for an entity that does not exist invents terms because that is what searching is.
    /// </summary>
    [JsonPropertyName("fabricated_term_count")] public int? FabricatedTermCount { get; init; }

    /// <summary>The offending "iter N: tool.param=value" entries. Only the fabricated ones, not a full audit.</summary>
    [JsonPropertyName("fabricated_arguments")] public IReadOnlyList<string> FabricatedArguments { get; init; } = [];

    /// <summary>
    /// Ids that walk a contiguous stretch of the range their tool advertises — systematic
    /// enumeration rather than invention. Excluded from <see cref="FabricatedArgumentCount"/>.
    /// </summary>
    /// <remarks>
    /// The grounding corpus is the question plus prior tool results and excludes the tool
    /// declarations, so a model that reads a parameter's advertised bounds and sweeps them scores
    /// as fabricating every value. This separates that case out. It is narrow by design — see
    /// <c>ArgumentProvenance.SchemaEnumerated</c>.
    /// </remarks>
    [JsonPropertyName("schema_enumerated_count")] public int? SchemaEnumeratedCount { get; init; }

    [JsonPropertyName("schema_enumerated_arguments")] public IReadOnlyList<string> SchemaEnumeratedArguments { get; init; } = [];

    /// <summary>Fabricated arguments whose value matched <c>^call_\d+$</c> — a normalised call id read back as data.</summary>
    /// <remarks>
    /// ONLY MEANINGFUL WHEN <see cref="NormaliseToolCallIds"/> IS ON. The detector matches the
    /// harness's own <c>call_N</c> shape; a raw provider id is an opaque hex string it cannot
    /// recognise, so with normalisation off this reads zero whatever the model did. Measured, not
    /// assumed: the same qwen2.5:1.5b sweep scored 0 with normalisation off and 3 with it on, from
    /// the same behaviour. Read this alongside <c>normalise_tool_call_ids</c> on the same run.
    /// </remarks>
    [JsonPropertyName("call_id_as_argument_count")] public int? CallIdAsArgumentCount { get; init; }

    /// <summary>Grounded but sent as the wrong JSON kind, e.g. <c>{"film_id":"3"}</c> where the tool declares an integer.</summary>
    [JsonPropertyName("argument_type_mismatch_count")] public int? ArgumentTypeMismatchCount { get; init; }

    /// <summary>Calls that failed for a wrong parameter name or type, not a data reason.</summary>
    [JsonPropertyName("schema_error_count")] public int? SchemaErrorCount { get; init; }

    [JsonPropertyName("schema_errors")] public IReadOnlyList<string> SchemaErrors { get; init; } = [];
}
