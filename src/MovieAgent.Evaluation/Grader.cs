using System.Globalization;
using System.Text.RegularExpressions;
using MovieAgent.Agent.Recording;
using MovieAgent.Tools;

namespace MovieAgent.Evaluation;

/// <summary>
/// Deterministic grading of a free-text answer.
/// </summary>
/// <remarks>
/// JUDGEMENT CALL, AND THE WEAKEST PART OF THE HARNESS. Grading natural language against a
/// reference value is genuinely hard, and this is the cheap version: normalise, then look for
/// the expected value inside the answer. It is deliberately lenient about surrounding prose
/// and strict about the value itself.
/// <para>
/// Known ways it will be wrong, in rough order of how often you should expect them:
/// </para>
/// <list type="bullet">
/// <item>A correct answer stated in words ("nineteen") scores as wrong.</item>
/// <item>"The film is not in Italian, it is in French" contains "Italian" and scores as right.
/// Substring matching cannot see negation.</item>
/// <item>Refusal detection is a phrase list. Models decline in ways no phrase list anticipates.</item>
/// </list>
/// Treat the numbers this produces as a first pass and eyeball the JSONL before believing them.
/// The two realistic upgrades are an LLM judge on the recorded final_answer, or manual
/// adjudication of a sample — both operate on the recorded data after the fact, so neither
/// requires re-running anything.
/// </remarks>
public static partial class Grader
{
    public const string Method = "deterministic-substring-v2";

    private const string ResultNoun =
        @"(?:match(?:es|ing)?|result(?:s)?|record(?:s)?|film(?:s)?|actor(?:s)?|customer(?:s)?|row(?:s)?|entr(?:y|ies)|data|information)";

    private const string KnowingVerb =
        @"(?:find|found|locate|determine|identify|answer|retrieve|access|resolve|tell)";

    private const string Inability =
        @"(?:cannot|can not|could not|unable|not able|do not have|does not have|did not|does not|do not)";

    public static GradeRecord Grade(EvalQuestion question, RunRecord run, IReadOnlyList<string> surfaceToolNames)
    {
        var shouldDecline = question.ShouldDeclineOn(surfaceToolNames);
        var expectedBehaviour = shouldDecline ? "decline" : "answer";

        // A tool counts as reached only if it ran without error. A call rejected for a bad
        // argument, or blocked as a repeat, did not get the model anywhere.
        var toolsUsed = run.Iterations
            .SelectMany(i => i.ToolCalls)
            .Where(c => !c.IsError)
            .Select(c => c.ToolName)
            .ToHashSet(StringComparer.Ordinal);

        // A step counts as taken if any tool in its group ran successfully.
        var missing = question.RequiresTools
            .Where(group => !group.Any(toolsUsed.Contains))
            .Select(group => string.Join(" or ", group))
            .ToArray();

        // The first truncation notice in the run, if any. Questions are designed so at most one
        // tool call truncates; taking the first is the simple, sufficient choice rather than a
        // deliberate one about which call matters most when several do.
        var truncation = run.Iterations
            .SelectMany(i => i.ToolCalls)
            .Select(c => ToolOutputFormat.TryParseTruncation(c.ResultText))
            .FirstOrDefault(n => n is not null);

        var answerMatchesTruncation = truncation is not null && !string.IsNullOrWhiteSpace(run.FinalAnswer)
            ? NumberPattern().Matches(run.FinalAnswer)
                .Select(m => decimal.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null)
                .Any(d => d == truncation.TotalRows)
            : (bool?)null;

        // Argument provenance and schema-error classification. Independent of the answerable/
        // decline branching below — a run that hit the cap or errored can still have fabricated
        // arguments upthread, and that is exactly the kind of run worth seeing this on.
        var diagnostics = ToolCallDiagnostics.Analyse(run);

        GradeRecord WithNavigation(GradeRecord grade) => grade with
        {
            RequiredTools = [.. question.RequiresTools.Select(g => string.Join(" or ", g))],
            RequiredToolsMissing = missing,
            NavigationComplete = missing.Length == 0,
            Scored = question.Scored,
            TruncationSeen = truncation is not null,
            TruncationStatedTotal = truncation?.TotalRows,
            AnswerMatchesStatedTotal = answerMatchesTruncation,
            FabricatedArgumentCount = diagnostics.FabricatedArgumentCount,
            FabricatedArguments = diagnostics.FabricatedArguments,
            CallIdAsArgumentCount = diagnostics.CallIdAsArgumentCount,
            ArgumentTypeMismatchCount = diagnostics.ArgumentTypeMismatchCount,
            SchemaErrorCount = diagnostics.SchemaErrorCount,
            SchemaErrors = diagnostics.SchemaErrors,
        };

        // A run that never produced an answer cannot be graded as a refusal: hitting the cap
        // or erroring is a different failure from correctly declining, and conflating them
        // would inflate refusal accuracy.
        if (run.Outcome != RunOutcome.Answered || string.IsNullOrWhiteSpace(run.FinalAnswer))
        {
            return WithNavigation(new GradeRecord
            {
                ExpectedAnswer = question.ExpectedAnswer,
                ExpectedBehaviour = expectedBehaviour,
                Correct = false,
                Declined = false,
                Method = Method,
                Note = $"No final answer to grade (outcome {run.Outcome}).",
            });
        }

        var answer = run.FinalAnswer;

        if (shouldDecline)
        {
            var declined = LooksLikeRefusal(answer);
            return WithNavigation(new GradeRecord
            {
                ExpectedAnswer = question.ExpectedAnswer,
                ExpectedBehaviour = expectedBehaviour,
                Correct = declined,
                Declined = declined,
                Method = Method,
                Note = declined ? null : "Expected a refusal; the model answered.",
            });
        }

        // A correct value takes precedence over refusal-shaped phrasing. A model that says
        // "I couldn't find CASABLANCA NIGHTS, but CASABLANCA SUPER is 4.99" both declines and
        // answers in the same breath — that is the near-miss recovery this batch exists to
        // reward, not a refusal. Checking the value first and falling back to refusal detection
        // only on a genuine miss means "no" only counts as "no" when there is no "but" attached.
        var (correct, note) = question.AnswerKind switch
        {
            AnswerKind.Numeric => MatchNumeric(question.ExpectedAnswer, answer),
            AnswerKind.Set => MatchSet(question.ExpectedAnswer, answer),
            AnswerKind.Exact => MatchExact(question.ExpectedAnswer, answer),
            AnswerKind.Decline => (false, "answer_kind is 'decline' but the question is answerable on this surface."),
            AnswerKind.Ambiguous => (false, "answer_kind is 'ambiguous' but the question is graded as answerable."),
            _ => (false, "Unknown answer kind."),
        };

        if (correct)
        {
            return WithNavigation(new GradeRecord
            {
                ExpectedAnswer = question.ExpectedAnswer,
                ExpectedBehaviour = expectedBehaviour,
                Correct = true,
                Declined = false,
                Method = Method,
                Note = note,
            });
        }

        // Only reached on a wrong or missing value. Declining an answerable question is
        // recorded separately from a wrong answer — over-refusal and hallucination are
        // different failure modes.
        if (LooksLikeRefusal(answer))
        {
            return WithNavigation(new GradeRecord
            {
                ExpectedAnswer = question.ExpectedAnswer,
                ExpectedBehaviour = expectedBehaviour,
                Correct = false,
                Declined = true,
                Method = Method,
                Note = "Declined an answerable question.",
            });
        }

        return WithNavigation(new GradeRecord
        {
            ExpectedAnswer = question.ExpectedAnswer,
            ExpectedBehaviour = expectedBehaviour,
            Correct = false,
            Declined = false,
            Method = Method,
            Note = note,
        });
    }

    private static (bool Correct, string? Note) MatchExact(string? expected, string answer)
    {
        if (expected is null)
        {
            return (false, "Question has no expected_answer but is graded as answerable.");
        }

        return (Normalise(answer).Contains(Normalise(expected), StringComparison.Ordinal), null);
    }

    private static (bool Correct, string? Note) MatchSet(string? expected, string answer)
    {
        if (expected is null)
        {
            return (false, "Question has no expected_answer but is graded as answerable.");
        }

        var parts = expected.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalised = Normalise(answer);
        var missing = parts.Where(p => !normalised.Contains(Normalise(p), StringComparison.Ordinal)).ToArray();

        return missing.Length == 0
            ? (true, null)
            : (false, $"Missing from the answer: {string.Join(", ", missing)}.");
    }

    private static (bool Correct, string? Note) MatchNumeric(string? expected, string answer)
    {
        if (expected is null || !decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var target))
        {
            return (false, $"expected_answer '{expected}' is not numeric.");
        }

        var found = NumberPattern().Matches(answer)
            .Select(m => decimal.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .Distinct()
            .ToArray();

        return found.Contains(target)
            ? (true, null)
            : (false, found.Length == 0
                ? "No number in the answer."
                : $"Expected {target}; answer contained {string.Join(", ", found)}.");
    }

    /// <summary>
    /// Does this answer decline rather than assert?
    /// </summary>
    /// <remarks>
    /// Was a flat substring list, and it was biased against models that write well. GPT-5.4
    /// declined with "I couldn't find any film titled X ... returned no matches" and scored as
    /// having answered, for two independent reasons: "could not" was absent from the list, and
    /// the apostrophe was U+2019 rather than ASCII, so the contraction entries could never have
    /// matched it anyway. A frontier model looked worse at refusal than a 4B because of quote
    /// characters.
    /// <para>
    /// Now: normalise typographic punctuation, expand contractions, then match intent patterns
    /// rather than fixed strings. Validated over the 165 answers recorded to date — six declines
    /// newly detected, none lost, and no correct answer newly misread as a refusal, which is the
    /// dangerous direction because it would turn a right answer into an over-refusal.
    /// </para>
    /// </remarks>
    private static bool LooksLikeRefusal(string answer)
    {
        var text = NormaliseForRefusal(answer);

        return NoResultsPattern().IsMatch(text)
            || CannotFindPattern().IsMatch(text)
            || NonExistencePattern().IsMatch(text)
            || FixedRefusalPattern().IsMatch(text);
    }

    private static string NormaliseForRefusal(string value)
    {
        var text = value
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('ʼ', '\'')
            .ToLowerInvariant()
            .Replace("can't", "cannot", StringComparison.Ordinal)
            .Replace("won't", "will not", StringComparison.Ordinal);

        // couldn't -> could not, doesn't -> does not, and so on.
        text = ContractionPattern().Replace(text, " not");

        return WhitespacePattern().Replace(text, " ");
    }

    /// <summary>Lower case, collapse whitespace, drop punctuation that varies between phrasings.</summary>
    private static string Normalise(string value) =>
        WhitespacePattern().Replace(PunctuationPattern().Replace(value.ToLowerInvariant(), " "), " ").Trim();

    /// <summary>
    /// Numbers, with digit grouping recognised before the plain form so that a grouped number is
    /// consumed whole.
    /// </summary>
    /// <remarks>
    /// The previous pattern had no grouping alternative, so "1,000" matched as "1" and "000" —
    /// two numbers, neither of them 1000, and the run was graded wrong for a correct answer. The
    /// alternation order matters: put the plain form first and it wins on "1," and the bug
    /// returns. Parsing uses <see cref="NumberStyles.Any"/>, which accepts the separator.
    /// </remarks>
    [GeneratedRegex(@"-?\d{1,3}(?:,\d{3})+(?:\.\d+)?|-?\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"[^\w@.\-]+")]
    private static partial Regex PunctuationPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"n't\b")]
    private static partial Regex ContractionPattern();

    /// <summary>"returned no matches", "there are no films", "no such records".</summary>
    [GeneratedRegex(@"\bno\s+(?:\w+\s+){0,2}" + ResultNoun + @"\b")]
    private static partial Regex NoResultsPattern();

    /// <summary>
    /// An inability verb close to a knowing verb. Bounded to within one sentence and 50
    /// characters so that "did not" in an unrelated clause cannot reach across and trigger it.
    /// </summary>
    [GeneratedRegex(@"\b" + Inability + @"\b[^.]{0,50}\b" + KnowingVerb + @"\b")]
    private static partial Regex CannotFindPattern();

    [GeneratedRegex(@"\b(?:does not|do not)\s+(?:exist|appear)\b|\bnot\s+(?:found|available|reachable|possible|present|in the database)\b|\bnothing\s+(?:found|match\w*)\b")]
    private static partial Regex NonExistencePattern();

    [GeneratedRegex(@"\b(?:no such|no rows|none of the|no tool|insufficient|unavailable)\b")]
    private static partial Regex FixedRefusalPattern();
}
