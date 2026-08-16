using MovieAgent.Agent.Recording;
using MovieAgent.Evaluation;
using Xunit;

namespace MovieAgent.Evaluation.Tests;

/// <summary>
/// Value matching: exact, set and numeric. Cases marked REGRESSION are defects that reached
/// published results before being found by hand; the rest are failure modes the same matching
/// strategy admits but that no recorded run has produced yet.
/// </summary>
public class AnswerMatchingTests
{
    private static bool GradeExact(string expected, string answer) =>
        GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Exact, expected),
            GradingScenario.Run(answer)).Correct;

    private static bool GradeSet(string expected, string answer) =>
        GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Set, expected),
            GradingScenario.Run(answer)).Correct;

    private static bool GradeNumeric(string expected, string answer) =>
        GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Numeric, expected),
            GradingScenario.Run(answer)).Correct;

    // ---- exact ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Boksburg", "The store is in Boksburg.")]
    [InlineData("Boksburg", "boksburg")]                                    // case-insensitive
    [InlineData("Boksburg", "The city is **Boksburg**.")]                   // markdown emphasis
    [InlineData("Boksburg", "  The   city\nis  Boksburg  ")]                // whitespace collapsed
    [InlineData("BETTY MILLER", "Betty Miller rented it.")]                 // multi-word, mixed case
    [InlineData("Anguilla", "Bobby Boudreau lives in Anguilla.")]
    [InlineData("Thailand", "The customer lives in Thailand.")]             // trailing full stop
    [InlineData("English", "The film is in English")]                       // no trailing punctuation
    [InlineData("hartmann1448@ratkehaley.com", "Email: hartmann1448@ratkehaley.com.")]
    [InlineData("WARDROBE PHANTOM", "The film is \"WARDROBE PHANTOM\" (id 958).")]
    public void Exact_accepts(string expected, string answer) =>
        Assert.True(GradeExact(expected, answer), $"expected '{expected}' to match: {answer}");

    [Theory]
    [InlineData("English", "The film is Italianate.")]                      // substring of a longer word
    [InlineData("Boksburg", "The store is in Johannesburg.")]
    [InlineData("Anguilla", "The customer lives in Angola.")]
    [InlineData("Thailand", "The country could not be determined.")]
    public void Exact_rejects(string expected, string answer) =>
        Assert.False(GradeExact(expected, answer), $"expected '{expected}' NOT to match: {answer}");

    [Fact]
    public void Exact_matches_inside_a_longer_word_only_when_bounded()
    {
        // The word-boundary rule is what stops "English" matching "Englishman", but it must not
        // reject a legitimate answer whose match is flanked by retained punctuation. Normalise
        // keeps '.', '@' and '-', so those are boundaries the whitespace rule would have missed.
        Assert.True(GradeExact("English", "in English."));
        Assert.True(GradeExact("English", "(English)"));
        Assert.False(GradeExact("English", "Englishman"));
        Assert.False(GradeExact("English", "unEnglish"));
    }

    [Fact]
    public void Exact_cannot_see_negation_which_is_a_known_and_accepted_limitation()
    {
        // Documented in the Grader remarks. Pinned so that if someone later adds negation
        // handling, this test fails and forces the decision to be deliberate rather than silent.
        Assert.True(GradeExact("Italian", "The film is not in Italian, it is in French."));
    }

    // ---- set ------------------------------------------------------------------------------

    [Theory]
    [InlineData("Boksburg; Hamilton", "The stores are in Boksburg and Hamilton.")]
    [InlineData("Boksburg; Hamilton", "Hamilton, then Boksburg.")]                  // order-independent
    [InlineData("Children; Comedy; New", "It belongs to Children, Comedy, and New.")]
    [InlineData("CATE MCQUEEN; 30", "Cate McQueen has appeared in 30 films.")]
    public void Set_requires_every_member(string expected, string answer) =>
        Assert.True(GradeSet(expected, answer));

    [Theory]
    [InlineData("Boksburg; Hamilton", "The store is in Boksburg.")]                 // one member missing
    [InlineData("Children; Comedy; New", "It belongs to Children and Comedy.")]
    public void Set_rejects_a_missing_member(string expected, string answer) =>
        Assert.False(GradeSet(expected, answer));

    [Fact]
    public void Set_REGRESSION_a_numeric_member_must_not_match_inside_a_longer_number()
    {
        // Found by review, not by a run: "30" matched inside "130" under plain Contains, so an
        // answer stating the wrong count passed. Zero recorded runs hit it, but the defect was
        // real and the fix is what the word-boundary rule exists for.
        Assert.False(GradeSet("CATE MCQUEEN; 30", "Cate McQueen has appeared in 130 films."));
        Assert.False(GradeSet("CATE MCQUEEN; 30", "Cate McQueen has appeared in 305 films."));
    }

    [Fact]
    public void Set_allows_extra_values_beyond_those_required()
    {
        // Deliberate and worth pinning: the set rule is "all of these appear", not "exactly
        // these". An answer naming an extra category still passes. If that is ever tightened it
        // should be a decision, not a surprise.
        Assert.True(GradeSet("Children; Comedy", "Children, Comedy, and Horror."));
    }

    [Fact]
    public void Set_trims_whitespace_around_separators()
    {
        Assert.True(GradeSet("Boksburg;Hamilton", "Boksburg and Hamilton"));
        Assert.True(GradeSet("Boksburg ;  Hamilton ", "Boksburg and Hamilton"));
    }

    // ---- numeric --------------------------------------------------------------------------

    [Theory]
    [InlineData("6", "The rental duration is 6 days.")]
    [InlineData("16.99", "The replacement cost is $16.99.")]
    [InlineData("142", "There are 142 films in the Horror category.")]
    [InlineData("7", "The film has 7 actors credited in it.")]
    [InlineData("0", "There are 0 matching rentals.")]
    [InlineData("-1", "The offset is -1.")]
    public void Numeric_accepts_the_value_anywhere_in_the_answer(string expected, string answer) =>
        Assert.True(GradeNumeric(expected, answer));

    [Fact]
    public void Numeric_REGRESSION_grouped_thousands_parse_as_one_number()
    {
        // "1,000" previously matched as "1" and "000" — two numbers, neither of them 1000 — and
        // graded a correct answer wrong. The pattern must consume the grouped form whole.
        Assert.True(GradeNumeric("1000", "There are 1,000 films."));
        Assert.True(GradeNumeric("1000", "There are 1000 films."));
        Assert.True(GradeNumeric("1234567", "A total of 1,234,567 rows."));
    }

    [Fact]
    public void Numeric_does_not_match_a_digit_sequence_inside_a_longer_one()
    {
        Assert.False(GradeNumeric("30", "There are 130 films."));
        Assert.False(GradeNumeric("16.99", "The cost is 116.99."));
    }

    [Fact]
    public void Numeric_KNOWN_LIMITATION_passes_when_the_value_appears_only_in_working()
    {
        // The sharpest known weakness, and it fired on the second-best model in sweep v3.
        // qwen3.5:9b never called get_film, computed date arithmetic for 2,700 characters,
        // concluded "the answer is likely 3 days" — and passed because "~6 days" appeared
        // mid-working. Tightening this (e.g. taking the last number) flags 24 of 230 legitimate
        // passes, so the discriminator is navigation, not the matcher. Pinned so the behaviour
        // is a documented choice.
        Assert.True(GradeNumeric("6", "June 17 to June 23 = ~6 days. The answer is likely 3 days."));
    }

    [Fact]
    public void Numeric_rejects_a_non_numeric_expected_answer_rather_than_throwing()
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Numeric, "not-a-number"),
            GradingScenario.Run("The answer is 6."));

        Assert.False(grade.Correct);
        Assert.Contains("not numeric", grade.Note);
    }

    [Fact]
    public void Numeric_reports_what_it_found_when_the_value_is_absent()
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Numeric, "6"),
            GradingScenario.Run("The duration is 3 days."));

        Assert.False(grade.Correct);
        Assert.Contains("3", grade.Note);
    }

    [Fact]
    public void Numeric_says_so_when_the_answer_contains_no_number_at_all()
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Numeric, "6"),
            GradingScenario.Run("The duration varies by store."));

        Assert.False(grade.Correct);
        Assert.Equal("No number in the answer.", grade.Note);
    }

    // ---- missing expected answer ----------------------------------------------------------

    [Theory]
    [InlineData(AnswerKind.Exact)]
    [InlineData(AnswerKind.Set)]
    [InlineData(AnswerKind.Numeric)]
    public void A_null_expected_answer_fails_loudly_rather_than_matching_everything(AnswerKind kind)
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(kind, expectedAnswer: null),
            GradingScenario.Run("Anything at all."));

        Assert.False(grade.Correct);
        Assert.NotNull(grade.Note);
    }

    [Theory]
    [InlineData(AnswerKind.Decline)]
    [InlineData(AnswerKind.Ambiguous)]
    public void A_non_answerable_kind_on_an_answerable_question_is_not_silently_correct(AnswerKind kind)
    {
        // Only reachable through an eval-set authoring mistake, which is exactly when a silent
        // pass would be most damaging.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(kind, "Boksburg"),
            GradingScenario.Run("The city is Boksburg."));

        Assert.False(grade.Correct);
        Assert.NotNull(grade.Note);
    }
}
