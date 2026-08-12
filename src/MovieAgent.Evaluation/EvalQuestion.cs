using System.Text.Json.Serialization;

namespace MovieAgent.Evaluation;

[JsonConverter(typeof(JsonStringEnumConverter<AnswerKind>))]
public enum AnswerKind
{
    /// <summary>A single value that must appear in the answer, normalised for case and punctuation.</summary>
    Exact,

    /// <summary>A number that must appear in the answer.</summary>
    Numeric,

    /// <summary>Several values, all of which must appear. Separated by ';' in the eval set.</summary>
    Set,

    /// <summary>There is no answer; the model should say so.</summary>
    Decline,

    /// <summary>
    /// There are many equally valid answers, so the question is ill-posed. The model should say
    /// so or ask which one is meant. <c>reference_sql</c> returns the number of valid answers,
    /// and the verifier checks it is greater than one.
    /// </summary>
    Ambiguous,
}

public sealed record EvalQuestion
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("question")] public required string Question { get; init; }

    [JsonPropertyName("expected_hops")] public required int ExpectedHops { get; init; }

    /// <summary>"answer" or "decline". Overridden to decline when the surface lacks a required tool.</summary>
    [JsonPropertyName("expected_behaviour")] public required string ExpectedBehaviour { get; init; }

    [JsonPropertyName("expected_answer")] public string? ExpectedAnswer { get; init; }

    [JsonPropertyName("answer_kind")] public required AnswerKind AnswerKind { get; init; }

    /// <summary>The SQL that produces the answer. Not exposed to the model; used to re-verify the eval set.</summary>
    [JsonPropertyName("reference_sql")] public required string ReferenceSql { get; init; }

    /// <summary>
    /// The steps the shortest correct chain needs, as alternative groups: one group per step,
    /// and any tool within a group satisfies that step. Drives surface-relative grading.
    /// </summary>
    /// <remarks>
    /// Groups rather than a flat list because a step often has more than one legitimate route.
    /// Counting the actors in a film is <c>get_film_actor_ids</c> on standard and
    /// <c>count_film_actors</c> on enriched; with a flat list the enriched run reached the right
    /// answer by the better route and was recorded as having missed a required tool.
    /// </remarks>
    [JsonPropertyName("requires_tools")] public required IReadOnlyList<IReadOnlyList<string>> RequiresTools { get; init; }

    /// <summary>
    /// False for questions kept as qualitative exhibits rather than scored. They still run and
    /// are still recorded in full; they are excluded from every accuracy denominator.
    /// </summary>
    /// <remarks>
    /// For a genuinely ill-posed question there is no defensible single expected behaviour.
    /// Scoring "decline" as the only correct answer penalises a model that resolves the
    /// ambiguity sensibly, and scoring any particular film penalises one that spots the
    /// ambiguity. Both behaviours are interesting; neither is right, so neither is scored.
    /// </remarks>
    [JsonPropertyName("scored")] public bool Scored { get; init; } = true;

    [JsonPropertyName("note")] public string? Note { get; init; }

    /// <summary>Groups with no member available on this surface, i.e. steps with no route.</summary>
    public IReadOnlyList<IReadOnlyList<string>> UnreachableStepsOn(IReadOnlyList<string> surfaceToolNames) =>
        [.. RequiresTools.Where(group => !group.Any(t => surfaceToolNames.Contains(t, StringComparer.Ordinal)))];

    /// <summary>
    /// True when this question cannot be answered on the given surface, either because the
    /// entity does not exist or because some step has no available tool.
    /// </summary>
    public bool ShouldDeclineOn(IReadOnlyList<string> surfaceToolNames) =>
        string.Equals(ExpectedBehaviour, "decline", StringComparison.OrdinalIgnoreCase)
        || UnreachableStepsOn(surfaceToolNames).Count > 0;
}

public sealed record EvalSet
{
    [JsonPropertyName("eval_set_id")] public required string EvalSetId { get; init; }

    [JsonPropertyName("notes")] public IReadOnlyList<string> Notes { get; init; } = [];

    [JsonPropertyName("questions")] public required IReadOnlyList<EvalQuestion> Questions { get; init; }
}
