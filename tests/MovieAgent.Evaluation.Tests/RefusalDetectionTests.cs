using MovieAgent.Agent.Recording;
using MovieAgent.Evaluation;
using Xunit;

namespace MovieAgent.Evaluation.Tests;

/// <summary>
/// Refusal detection. The dangerous direction is a correct answer read as a refusal, because that
/// converts a right answer into an over-refusal and moves two published numbers at once — so the
/// negative cases here matter more than the positive ones.
/// </summary>
public class RefusalDetectionTests
{
    /// <summary>A question that genuinely has no answer, with its evidence path already walked.</summary>
    private static GradeRecord GradeDecline(string answer, IEnumerable<string>? toolsCalled = null) =>
        GradingScenario.Grade(
            GradingScenario.Question(
                AnswerKind.Decline,
                expectedAnswer: null,
                expectedBehaviour: "decline",
                requiresTools: [["search_film"]]),
            GradingScenario.Run(answer, toolsCalled: toolsCalled ?? ["search_film"]));

    /// <summary>An answerable question, to detect over-refusal.</summary>
    private static GradeRecord GradeAnswerable(string answer) =>
        GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Exact, "Boksburg"),
            GradingScenario.Run(answer));

    [Theory]
    [InlineData("The search returned no matches.")]
    [InlineData("There are no films with that title.")]
    [InlineData("No such category exists in the database.")]
    [InlineData("I cannot find a film with that title.")]
    [InlineData("I could not locate any matching record.")]
    [InlineData("I'm unable to determine the answer with these tools.")]
    [InlineData("That film does not exist in the database.")]
    [InlineData("The director is not available in this schema.")]
    [InlineData("Nothing matched that search.")]
    [InlineData("The information is not reachable with the tools I have.")]
    public void Recognises_a_refusal(string answer) =>
        Assert.True(GradeDecline(answer).Declined, $"should read as a refusal: {answer}");

    [Theory]
    [InlineData("There is no category named Steampunk in the database.")]
    [InlineData("There is no \"Steampunk\" category in this database.")]
    [InlineData("There are no categories with the name \"Steampunk\", so I cannot provide a count.")]
    [InlineData("I cannot directly count the total number of films with the tools available.")]
    [InlineData("I cannot reach that with the tools available.")]
    [InlineData("There is no “Steampunk” category in the database, so it contains 0 films.")]
    public void FIXED_IN_V5_a_refusal_phrased_around_a_category_is_now_detected(string answer)
    {
        // Real recorded answers, all unambiguous refusals, all scored as failures under v4:
        //   - ResultNoun had no "category", so "there is no Steampunk category" matched nothing.
        //   - KnowingVerb had no "provide", "reach" or "count", so "cannot provide a count" and
        //     "cannot reach" matched nothing — while the system prompt tells the model to say
        //     exactly that ("If the tools available to you cannot reach the answer, say so").
        //
        // Found by this test suite on its first run rather than by a sweep, which is the whole
        // argument for the suite existing.
        Assert.True(GradeDecline(answer).Declined);
    }

    [Fact]
    public void REGRESSION_recognises_a_refusal_written_with_a_typographic_apostrophe()
    {
        // gpt-5.4 declined with a U+2019 apostrophe and was recorded as having answered: the
        // contraction entries were ASCII-only, so a frontier model looked worse at refusal than
        // a 4B because of a quote character.
        Assert.True(GradeDecline("I couldn’t find any film titled that; the search returned no matches.").Declined);
        Assert.True(GradeDecline("I couldn't find any film titled that.").Declined);
        Assert.True(GradeDecline("I can’t determine that from the available tools.").Declined);
    }

    [Theory]
    [InlineData("The store is in Boksburg.")]
    [InlineData("The city is Boksburg, in South Africa.")]
    [InlineData("Boksburg. The store's address resolves to city 85.")]
    public void Does_not_read_a_plain_correct_answer_as_a_refusal(string answer)
    {
        var grade = GradeAnswerable(answer);
        Assert.True(grade.Correct);
        Assert.False(grade.Declined);
    }

    [Fact]
    public void A_correct_value_beats_refusal_shaped_phrasing_in_the_same_answer()
    {
        // Near-miss recovery: the model says it could not find the exact title AND supplies the
        // right answer for the near variant. That is the behaviour the near-miss family exists to
        // reward, so it must grade as correct rather than as a refusal.
        var grade = GradeAnswerable(
            "I couldn't find CASABLANCA NIGHTS, but the closest match is in Boksburg.");

        Assert.True(grade.Correct);
        Assert.False(grade.Declined);
    }

    [Fact]
    public void A_wrong_answer_phrased_as_a_refusal_counts_as_an_over_refusal_not_a_wrong_answer()
    {
        // Over-refusal and hallucination are different failure modes and are reported separately.
        var grade = GradeAnswerable("I cannot find that film in the database.");

        Assert.False(grade.Correct);
        Assert.True(grade.Declined);
        Assert.Equal("Declined an answerable question.", grade.Note);
    }

    [Fact]
    public void An_unrelated_negative_clause_does_not_trigger_a_refusal()
    {
        // "did not" and a knowing verb must be close together and in the same sentence, or an
        // ordinary narrative sentence reads as a refusal.
        var grade = GradeAnswerable(
            "The rental did not incur a late fee. The store is in Boksburg, which I found via the address.");

        Assert.True(grade.Correct);
        Assert.False(grade.Declined);
    }

    [Fact]
    public void REGRESSION_an_answer_truncated_by_the_token_cap_is_not_credited_as_a_refusal()
    {
        // "I cannot" and "I cannot find it without checking X" share a prefix. A model that runs
        // out of budget mid-explanation would otherwise bank refusal credit for stopping.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(
                AnswerKind.Decline, null, "decline", requiresTools: [["search_film"]]),
            GradingScenario.Run(
                "Based on my search, there is",
                toolsCalled: ["search_film"],
                finishReason: "length"));

        Assert.False(grade.Declined);
        Assert.False(grade.Correct);
    }

    [Fact]
    public void Only_the_final_turn_being_truncated_suppresses_refusal_credit()
    {
        // An earlier truncated turn followed by a complete one leaves a complete final answer.
        // Excluding on "any iteration truncated" erased four genuine over-refusals in the corpus.
        var run = GradingScenario.Run("I cannot find that film.", toolsCalled: ["search_film"]);
        var withEarlierTruncation = run with
        {
            Iterations =
            [
                new IterationRecord
                {
                    Iteration = 1,
                    ElapsedMilliseconds = 1,
                    FinishReason = "length",
                    ToolCalls = [],
                },
                run.Iterations[0] with { Iteration = 2, FinishReason = "stop" },
            ],
        };

        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Decline, null, "decline", requiresTools: [["search_film"]]),
            withEarlierTruncation);

        Assert.True(grade.Declined);
    }

    [Fact]
    public void A_refusal_scores_on_its_wording_with_the_route_recorded_separately()
    {
        // The reverted v4 rule would have failed this for not reading the film record first. It
        // was dropped because the tool list already establishes that no director field exists, so
        // demanding the lookup asks for ceremony rather than evidence — and because navigation is
        // recorded anyway, which keeps "declined by the right route" answerable from the data
        // without the grade itself taking a position.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(
                AnswerKind.Decline,
                expectedAnswer: null,
                expectedBehaviour: "decline",
                requiresTools: [["search_film"], ["get_film"]]),
            GradingScenario.Run("No such information is available.", toolsCalled: ["search_film"]));

        Assert.True(grade.Declined);
        Assert.True(grade.Correct);
        Assert.False(grade.NavigationComplete);
        Assert.Equal(["get_film"], grade.RequiredToolsMissing);
    }

    [Fact]
    public void A_surface_relative_decline_is_exempt_from_the_evidence_requirement()
    {
        // The tool the path needs does not exist on this surface, so completing the path is
        // impossible by definition and must not be required.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(
                AnswerKind.Exact,
                "Boksburg",
                expectedBehaviour: "answer",
                requiresTools: [["search_film"], ["get_city"]]),
            GradingScenario.Run("I could not find that with the tools available.", toolsCalled: ["search_film"]),
            surface: ["search_film"]);

        Assert.Equal("decline", grade.ExpectedBehaviour);
        Assert.True(grade.Correct);
        Assert.True(grade.Declined);
    }
}
