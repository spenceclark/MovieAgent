using MovieAgent.Agent.Recording;
using MovieAgent.Evaluation;
using Xunit;

namespace MovieAgent.Evaluation.Tests;

/// <summary>
/// Argument provenance and schema-error classification. Every case here is decided against the
/// live <see cref="MovieAgent.Agent.Tools.ToolCatalogue"/>, because that is how the classifier
/// works — current declarations applied retroactively to recorded runs.
/// </summary>
public class ToolCallDiagnosticsTests
{
    /// <summary>Builds a run whose calls are given as (iteration, tool, argumentsJson, resultText).</summary>
    private static RunRecord Run(
        string question,
        params (int Iteration, string Tool, string Args, string Result)[] calls) =>
        RunWithErrors(question, [.. calls.Select(c => (c.Iteration, c.Tool, c.Args, c.Result, false))]);

    private static RunRecord RunWithErrors(
        string question,
        IReadOnlyList<(int Iteration, string Tool, string Args, string Result, bool IsError)> calls)
    {
        var iterations = calls
            .GroupBy(c => c.Iteration)
            .OrderBy(g => g.Key)
            .Select(g => new IterationRecord
            {
                Iteration = g.Key,
                ElapsedMilliseconds = 1,
                FinishReason = "stop",
                ToolCalls =
                [
                    .. g.Select(c => new ToolCallRecord
                    {
                        Iteration = c.Iteration,
                        ToolName = c.Tool,
                        ArgumentsRaw = c.Args,
                        ResultText = c.Result,
                        RowsReturned = c.IsError ? 0 : 1,
                        IsError = c.IsError,
                        ElapsedMilliseconds = 1,
                    }),
                ],
            })
            .ToArray();

        return GradingScenario.Run() with
        {
            Question = question,
            Iterations = iterations,
            ToolCallCount = calls.Count,
        };
    }

    // ---- grounding ------------------------------------------------------------------------

    [Fact]
    public void An_id_taken_from_an_earlier_result_is_grounded()
    {
        var summary = ToolCallDiagnostics.Analyse(Run(
            "What language is ADAPTATION HOLES in?",
            (1, "search_film", """{"title_contains":"ADAPTATION HOLES"}""", "film_id | title\n3 | ADAPTATION HOLES\n1 rows"),
            (2, "get_film", """{"film_id":3}""", "film_id | language_id\n3 | 1\n1 rows")));

        Assert.Equal(0, summary.FabricatedArgumentCount);
        Assert.Equal(0, summary.FabricatedIdCount);
    }

    [Fact]
    public void An_id_that_appears_nowhere_in_the_run_is_fabricated()
    {
        var summary = ToolCallDiagnostics.Analyse(Run(
            "What language is ADAPTATION HOLES in?",
            (1, "search_film", """{"title_contains":"ADAPTATION HOLES"}""", "film_id | title\n3 | ADAPTATION HOLES\n1 rows"),
            (2, "get_film", """{"film_id":742}""", "film_id | language_id\n742 | 1\n1 rows")));

        Assert.Equal(1, summary.FabricatedArgumentCount);
        Assert.Equal(1, summary.FabricatedIdCount);
        Assert.Contains("get_film.film_id=742", summary.FabricatedArguments.Single());
    }

    [Fact]
    public void A_value_from_the_question_itself_is_grounded()
    {
        var summary = ToolCallDiagnostics.Analyse(Run(
            "Inventory item 1 is held at a store. Which city is that store in?",
            (1, "get_inventory_item", """{"inventory_id":1}""", "inventory_id | store_id\n1 | 1\n1 rows")));

        Assert.Equal(0, summary.FabricatedArgumentCount);
    }

    [Fact]
    public void REGRESSION_the_trailing_row_count_line_does_not_ground_an_invented_id()
    {
        // Every single-row result ends in the line "1 rows". Without stripping it, a fabricated
        // film_id of 1 reads as grounded against a row *count* rather than a row *value* — caught
        // on hop3-film-language, where the real film_id was 3.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "What language is ADAPTATION HOLES in?",
            (1, "search_film", """{"title_contains":"ADAPTATION HOLES"}""", "film_id | title\n3 | ADAPTATION HOLES\n1 rows"),
            (2, "get_film", """{"film_id":1}""", "film_id | language_id\n1 | 1\n1 rows")));

        Assert.Equal(1, summary.FabricatedIdCount);
    }

    [Fact]
    public void REGRESSION_the_truncated_row_count_line_is_stripped_too()
    {
        // The seed id comes from the question so that only the second call is under test.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "How many films are in category 11?",
            (1, "get_category_film_ids", """{"category_id":11}""", "film_id\n6\n9\n142 rows, showing first 20"),
            (2, "get_film", """{"film_id":142}""", "film_id | title\n142 | X\n1 rows")));

        // 142 is the stated *total*, not a returned film_id, so using it as an id is invention.
        Assert.Equal(1, summary.FabricatedIdCount);
    }

    [Fact]
    public void REGRESSION_a_number_does_not_ground_itself_inside_a_longer_number_or_a_decimal()
    {
        // Without bounded matching "4" grounds against "24" or against a price like "4.99", and
        // nearly every small id looks grounded against any result containing a bigger number.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "What is the replacement cost of film 24?",
            (1, "get_film", """{"film_id":24}""", "film_id | replacement_cost\n24 | 4.99\n1 rows"),
            (2, "get_actor", """{"actor_id":4}""", "actor_id | name\n4 | Y\n1 rows")));

        Assert.Equal(1, summary.FabricatedIdCount);
    }

    [Fact]
    public void REGRESSION_a_sibling_call_in_the_same_turn_cannot_ground_an_argument()
    {
        // Both calls were decided in one shot, before the model saw either result, so the second
        // cannot have derived its id from the first.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "Which city is store 1 in?",
            (1, "get_store", """{"store_id":1}""", "store_id | address_id\n1 | 129\n1 rows"),
            (1, "get_address", """{"address_id":129}""", "address_id | city_id\n129 | 493\n1 rows")));

        Assert.Equal(1, summary.FabricatedIdCount);
    }

    [Fact]
    public void REGRESSION_an_error_message_echoing_the_argument_back_does_not_ground_it_on_retry()
    {
        // The harness's own error text repeats the offending value ("...but got '$store_id'"), so
        // without excluding error results a fabricated value grounds itself on the second attempt
        // purely via its own failure message.
        var summary = ToolCallDiagnostics.Analyse(RunWithErrors(
            "Which city?",
            [
                (1, "get_store", """{"store_id":"$store_id"}""",
                    "ERROR: 'store_id' must be a whole number, but got '$store_id'.", true),
                (2, "get_store", """{"store_id":"$store_id"}""",
                    "ERROR: 'store_id' must be a whole number, but got '$store_id'.", true),
            ]));

        // Both attempts are invented, and neither may be reclassified as a mere type mismatch.
        Assert.Equal(2, summary.FabricatedArgumentCount);
        Assert.Equal(0, summary.ArgumentTypeMismatchCount);
    }

    // ---- search terms vs row ids ------------------------------------------------------------

    [Fact]
    public void Narrowing_a_search_to_a_word_from_the_question_is_grounded()
    {
        // A model that fails on the full title and retries with one word of it has derived every
        // term from what it was given. This is the normal shape of near-miss recovery and must
        // not be counted as invention, or the metric would punish the behaviour the near-miss
        // family exists to reward.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "What is the rental rate of ZOMBIE ACCOUNTANT PARADOX?",
            (1, "search_film", """{"title_contains":"ZOMBIE ACCOUNTANT PARADOX"}""", "NO ROWS"),
            (2, "search_film", """{"title_contains":"zombie"}""", "NO ROWS"),
            (3, "search_film", """{"title_contains":"paradox"}""", "NO ROWS")));

        Assert.Equal(0, summary.FabricatedTermCount);
        Assert.Equal(0, summary.FabricatedArgumentCount);
    }

    [Fact]
    public void An_invented_search_term_counts_separately_from_an_invented_row_id()
    {
        // Inventing a term the question never contained is a guess, but a legitimate one — this
        // is how searching works. Inventing a row id asserts that a specific record exists, which
        // is the hallucination the metric was added to catch. The two are counted apart.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "What is the rental rate of ZOMBIE ACCOUNTANT PARADOX?",
            (1, "search_film", """{"title_contains":"ZOMBIE ACCOUNTANT PARADOX"}""", "NO ROWS"),
            (2, "search_film", """{"title_contains":"undead"}""", "NO ROWS"),
            (3, "get_film", """{"film_id":742}""", "film_id | title\n742 | X\n1 rows")));

        Assert.Equal(1, summary.FabricatedTermCount);
        Assert.Equal(1, summary.FabricatedIdCount);
        Assert.Equal(2, summary.FabricatedArgumentCount);
    }

    // ---- schema enumeration -----------------------------------------------------------------

    [Fact]
    public void A_contiguous_sweep_of_a_declared_range_is_enumeration_not_invention()
    {
        // get_category declares category_id as 1 to 16. A model walking the range has derived
        // every value from the schema it was shown, so this is systematic, not hallucinated.
        var calls = Enumerable.Range(1, 8)
            .Select(i => (Iteration: i, Tool: "get_category", Args: $$"""{"category_id":{{i}}}""", Result: $"category_id | name\n{i} | C{i}\n1 rows"))
            .ToArray();

        var summary = ToolCallDiagnostics.Analyse(Run("How many films are in the Steampunk category?", calls));

        Assert.Equal(8, summary.SchemaEnumeratedCount);
        Assert.Equal(0, summary.FabricatedIdCount);
        Assert.Equal(0, summary.FabricatedArgumentCount);
    }

    [Fact]
    public void Fewer_than_four_consecutive_values_stays_invention()
    {
        // Two or three consecutive ids happen by chance often enough that treating them as
        // deliberate enumeration would launder ordinary guessing.
        var calls = Enumerable.Range(1, 3)
            .Select(i => (Iteration: i, Tool: "get_category", Args: $$"""{"category_id":{{i}}}""", Result: $"category_id | name\n{i} | C{i}\n1 rows"))
            .ToArray();

        var summary = ToolCallDiagnostics.Analyse(Run("How many films are in the Steampunk category?", calls));

        Assert.Equal(0, summary.SchemaEnumeratedCount);
        Assert.Equal(3, summary.FabricatedIdCount);
    }

    [Fact]
    public void Scattered_in_range_guesses_are_not_a_sweep()
    {
        // Four values inside the declared range, but not consecutive. In-range alone is far too
        // weak a test — most fabricated ids in the corpus are in range.
        var picks = new[] { 2, 5, 9, 14 };
        var calls = picks
            .Select((v, i) => (Iteration: i + 1, Tool: "get_category", Args: $$"""{"category_id":{{v}}}""", Result: $"category_id | name\n{v} | C\n1 rows"))
            .ToArray();

        var summary = ToolCallDiagnostics.Analyse(Run("A question.", calls));

        Assert.Equal(0, summary.SchemaEnumeratedCount);
        Assert.Equal(4, summary.FabricatedIdCount);
    }

    [Fact]
    public void A_sweep_outside_the_declared_range_is_not_enumeration()
    {
        // category_id is declared 1 to 16. Walking 90..96 is contiguous but cannot have been
        // derived from the advertised bounds.
        var calls = Enumerable.Range(90, 7)
            .Select(i => (Iteration: i, Tool: "get_category", Args: $$"""{"category_id":{{i}}}""", Result: "ERROR: out of range"))
            .ToArray();

        var summary = ToolCallDiagnostics.Analyse(Run("A question.", calls));

        Assert.Equal(0, summary.SchemaEnumeratedCount);
        Assert.Equal(7, summary.FabricatedIdCount);
    }

    [Fact]
    public void Sweeps_are_grouped_per_tool_and_parameter()
    {
        // Two ids of two apiece across different tools must not combine into one four-long sweep.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "A question.",
            (1, "get_category", """{"category_id":1}""", "category_id\n1\n1 rows"),
            (2, "get_category", """{"category_id":2}""", "category_id\n2\n1 rows"),
            (3, "get_language", """{"language_id":3}""", "language_id\n3\n1 rows"),
            (4, "get_language", """{"language_id":4}""", "language_id\n4\n1 rows")));

        Assert.Equal(0, summary.SchemaEnumeratedCount);
        Assert.Equal(4, summary.FabricatedIdCount);
    }

    // ---- call ids and type mismatches --------------------------------------------------------

    [Fact]
    public void A_harness_call_id_passed_as_an_argument_is_always_fabricated()
    {
        // The model is echoing an identifier the harness injected into the conversation back as
        // though it were a row value. Never legitimate under any tool's schema.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "A question.",
            (1, "get_film", """{"film_id":"call_1"}""", "ERROR: bad argument")));

        Assert.Equal(1, summary.CallIdAsArgumentCount);
        Assert.Equal(1, summary.FabricatedArgumentCount);
    }

    [Fact]
    public void A_grounded_value_sent_as_the_wrong_json_kind_is_a_type_mismatch_not_invention()
    {
        // film_id 3 is grounded, but sent as a string where the schema declares an integer. That
        // is a formatting failure, not a hallucination, and is counted separately.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "What language is ADAPTATION HOLES in?",
            (1, "search_film", """{"title_contains":"ADAPTATION HOLES"}""", "film_id | title\n3 | ADAPTATION HOLES\n1 rows"),
            (2, "get_film", """{"film_id":"3"}""", "film_id | language_id\n3 | 1\n1 rows")));

        Assert.Equal(1, summary.ArgumentTypeMismatchCount);
        Assert.Equal(0, summary.FabricatedArgumentCount);
    }

    [Fact]
    public void A_number_sent_where_text_is_declared_is_also_a_type_mismatch()
    {
        var summary = ToolCallDiagnostics.Analyse(Run(
            "Find film 1994.",
            (1, "search_film", """{"title_contains":1994}""", "NO ROWS")));

        Assert.Equal(1, summary.ArgumentTypeMismatchCount);
        Assert.Equal(0, summary.FabricatedArgumentCount);
    }

    // ---- schema errors -----------------------------------------------------------------------

    [Theory]
    [InlineData("ERROR: 'get_film' does not take 'titel'. It takes: film_id.")]
    [InlineData("ERROR: 'get_film' requires the argument 'film_id'.")]
    [InlineData("ERROR: 'film_id' must be a whole number, but got 'abc'.")]
    public void A_wrong_name_or_wrong_type_failure_counts_as_a_schema_error(string message)
    {
        var summary = ToolCallDiagnostics.Analyse(RunWithErrors(
            "A question.", [(1, "get_film", """{"film_id":1}""", message, true)]));

        Assert.Equal(1, summary.SchemaErrorCount);
    }

    [Theory]
    [InlineData("ERROR: There is no film with that film_id. Valid values run 1 to 1000.")]
    [InlineData("ERROR: 'title_contains' must be at least 2 characters.")]
    [InlineData("ERROR: you have already called get_film with {\"film_id\":1} and it returned 1 row.")]
    public void A_data_failure_is_not_a_schema_error(string message)
    {
        // Out-of-range ids, too-short terms and blocked repeats are the right type in the right
        // shape, referring to data that is not there. Counting them as schema errors would make
        // the metric measure "did anything go wrong" rather than "did the model misread the API".
        var summary = ToolCallDiagnostics.Analyse(RunWithErrors(
            "A question.", [(1, "get_film", """{"film_id":9999}""", message, true)]));

        Assert.Equal(0, summary.SchemaErrorCount);
    }

    // ---- robustness --------------------------------------------------------------------------

    [Fact]
    public void Unparseable_arguments_degrade_rather_than_abort_the_whole_run()
    {
        // A diagnostic pass over years-old recorded data must not throw on one malformed line.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "A question.",
            (1, "get_film", "not json at all", "ERROR"),
            (2, "get_film", """{"film_id":742}""", "film_id\n742\n1 rows")));

        Assert.Equal(1, summary.FabricatedIdCount);
    }

    [Fact]
    public void An_unknown_tool_name_is_classified_without_a_declaration()
    {
        // Fabricated, but neither an id nor a term, because nothing declares its type.
        var summary = ToolCallDiagnostics.Analyse(Run(
            "A question.",
            (1, "get_nonexistent", """{"whatever":123}""", "ERROR: unknown tool")));

        Assert.Equal(1, summary.FabricatedArgumentCount);
        Assert.Equal(0, summary.FabricatedIdCount);
        Assert.Equal(0, summary.FabricatedTermCount);
    }

    [Fact]
    public void A_run_with_no_tool_calls_produces_an_empty_summary()
    {
        var summary = ToolCallDiagnostics.Analyse(GradingScenario.Run(toolsCalled: []));

        Assert.Equal(0, summary.FabricatedArgumentCount);
        Assert.Equal(0, summary.SchemaErrorCount);
        Assert.Empty(summary.FabricatedArguments);
        Assert.Empty(summary.SchemaErrors);
        Assert.Empty(summary.SchemaEnumeratedArguments);
    }
}
