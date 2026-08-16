using MovieAgent.Agent.Recording;
using MovieAgent.Evaluation;
using Xunit;

namespace MovieAgent.Evaluation.Tests;

/// <summary>
/// Navigation, run outcomes, and the null-versus-zero discipline. The strict score is derived from
/// <see cref="GradeRecord.NavigationComplete"/>, so these decide the headline number.
/// </summary>
public class NavigationAndOutcomeTests
{
    [Fact]
    public void Navigation_is_complete_when_every_required_step_ran()
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(requiresTools: [["search_film"], ["get_film"]]),
            GradingScenario.Run(toolsCalled: ["search_film", "get_film"]));

        Assert.True(grade.NavigationComplete);
        Assert.Empty(grade.RequiredToolsMissing);
    }

    [Fact]
    public void Navigation_names_the_step_that_was_never_reached()
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(requiresTools: [["search_film"], ["get_film"]]),
            GradingScenario.Run(toolsCalled: ["search_film"]));

        Assert.False(grade.NavigationComplete);
        Assert.Equal(["get_film"], grade.RequiredToolsMissing);
    }

    [Fact]
    public void Any_tool_in_a_group_satisfies_that_step()
    {
        // A step often has more than one legitimate route: counting a film's actors is
        // get_film_actor_ids on standard and count_film_actors on enriched. With a flat list the
        // enriched run took the better route and was recorded as having missed a required tool.
        var question = GradingScenario.Question(
            requiresTools: [["search_film"], ["get_film_actor_ids", "count_film_actors"]]);

        Assert.True(GradingScenario.Grade(question,
            GradingScenario.Run(toolsCalled: ["search_film", "get_film_actor_ids"])).NavigationComplete);

        Assert.True(GradingScenario.Grade(question,
            GradingScenario.Run(toolsCalled: ["search_film", "count_film_actors"])).NavigationComplete);
    }

    [Fact]
    public void A_failed_call_does_not_count_as_having_reached_a_tool()
    {
        // Calling get_film and being rejected for a bad argument got the model nowhere.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(requiresTools: [["search_film"], ["get_film"]]),
            GradingScenario.Run(toolsCalled: ["search_film"], failedTools: ["get_film"]));

        Assert.False(grade.NavigationComplete);
        Assert.Equal(["get_film"], grade.RequiredToolsMissing);
    }

    [Fact]
    public void A_correct_answer_can_still_be_unnavigated_which_is_what_the_strict_score_catches()
    {
        // The whole reason strict exists. The answer is right; the model never called the tool
        // that would have told it. Correct is true and NavigationComplete is false, and the two
        // must be reported independently rather than one overriding the other.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Exact, "Boksburg", requiresTools: [["search_film"], ["get_city"]]),
            GradingScenario.Run("The store is in Boksburg.", toolsCalled: ["search_film"]));

        Assert.True(grade.Correct);
        Assert.False(grade.NavigationComplete);
    }

    [Theory]
    [InlineData(RunOutcome.IterationCapReached)]
    [InlineData(RunOutcome.Errored)]
    [InlineData(RunOutcome.EmptyAnswer)]
    public void A_run_that_never_answered_is_not_graded_as_a_refusal(RunOutcome outcome)
    {
        // Hitting the cap or erroring is a different failure from correctly declining, and
        // conflating them would inflate refusal accuracy.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Decline, null, "decline", requiresTools: [["search_film"]]),
            GradingScenario.Run("I cannot find it.", outcome: outcome, toolsCalled: ["search_film"]));

        Assert.False(grade.Correct);
        Assert.False(grade.Declined);
        Assert.Contains("No final answer to grade", grade.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void An_empty_final_answer_is_not_graded_even_when_the_outcome_says_answered(string? answer)
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(),
            GradingScenario.Run(answer, outcome: RunOutcome.Answered));

        Assert.False(grade.Correct);
        Assert.False(grade.Declined);
        Assert.Contains("No final answer to grade", grade.Note);
    }

    [Fact]
    public void Navigation_is_still_recorded_on_a_run_that_never_answered()
    {
        // A capped run that reached every tool is a different animal from one that reached none,
        // and the difference is only visible if navigation survives the early return.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(requiresTools: [["search_film"], ["get_film"]]),
            GradingScenario.Run(
                null,
                outcome: RunOutcome.IterationCapReached,
                toolsCalled: ["search_film", "get_film"]));

        Assert.True(grade.NavigationComplete);
        Assert.Equal(["search_film", "get_film"], grade.RequiredTools);
    }

    [Fact]
    public void The_scored_flag_is_carried_through_from_the_question()
    {
        Assert.True(GradingScenario.Grade(GradingScenario.Question(scored: true), GradingScenario.Run()).Scored);
        Assert.False(GradingScenario.Grade(GradingScenario.Question(scored: false), GradingScenario.Run()).Scored);
    }

    [Fact]
    public void A_question_becomes_a_decline_when_the_surface_lacks_a_required_tool()
    {
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Exact, "Boksburg", requiresTools: [["search_film"], ["get_city"]]),
            GradingScenario.Run("I cannot reach that.", toolsCalled: ["search_film"]),
            surface: ["search_film"]);

        Assert.Equal("decline", grade.ExpectedBehaviour);
    }

    [Fact]
    public void On_the_generic_sql_surface_navigation_is_null_rather_than_false()
    {
        // Null means "not applicable"; false would read as a failure to reach a required tool.
        // Reporting zero here previously produced a strict score of zero for a model that
        // answered everything.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Exact, "Boksburg"),
            GradingScenario.Run("The city is Boksburg.", toolsCalled: ["execute_sql"]),
            surface: ["get_schema", "execute_sql"],
            genericSql: true);

        Assert.True(grade.Correct);
        Assert.Null(grade.NavigationComplete);
        Assert.Empty(grade.RequiredTools);
        Assert.Null(grade.FabricatedArgumentCount);
        Assert.Null(grade.SchemaErrorCount);
    }

    [Fact]
    public void On_the_generic_sql_surface_expected_behaviour_ignores_the_missing_tools()
    {
        // requires_tools names tools that do not exist on that surface, so the surface-relative
        // rule would otherwise mark every question unanswerable.
        var grade = GradingScenario.Grade(
            GradingScenario.Question(AnswerKind.Exact, "Boksburg", requiresTools: [["search_film"], ["get_city"]]),
            GradingScenario.Run("The city is Boksburg.", toolsCalled: ["execute_sql"]),
            surface: ["get_schema", "execute_sql"],
            genericSql: true);

        Assert.Equal("answer", grade.ExpectedBehaviour);
        Assert.True(grade.Correct);
    }

    [Fact]
    public void On_a_normal_surface_provenance_counts_are_zero_rather_than_null()
    {
        // The mirror of the case above: on a surface where the question *is* meaningful, a clean
        // run must report 0, not null, or "no fabricated arguments" becomes indistinguishable
        // from "never measured".
        var grade = GradingScenario.Grade(GradingScenario.Question(), GradingScenario.Run());

        Assert.Equal(0, grade.FabricatedArgumentCount);
        Assert.Equal(0, grade.SchemaErrorCount);
        Assert.NotNull(grade.NavigationComplete);
    }

    [Fact]
    public void The_method_string_is_stamped_on_every_grade()
    {
        // Recorded per run so a regrade can be told apart from the grade it replaced.
        Assert.Equal(Grader.Method, GradingScenario.Grade(GradingScenario.Question(), GradingScenario.Run()).Method);
    }
}
