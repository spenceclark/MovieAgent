using MovieAgent.Agent.Models;
using MovieAgent.Agent.Tools;
using Xunit;

namespace MovieAgent.Evaluation.Tests;

/// <summary>
/// <see cref="ToolOutputFormat.TryParseTruncation"/> reads the count line back out of a recorded
/// tool result. It is the only route to the true total for a truncated list, because the model
/// never sees the harness's own row telemetry — so a false positive here invents a total that
/// grading then holds the model to.
/// </summary>
public class TruncationNoticeTests
{
    [Fact]
    public void Reads_the_total_and_the_shown_count_from_a_truncated_result()
    {
        var notice = ToolOutputFormat.TryParseTruncation("film_id\n6\n9\n16\n142 rows, showing first 20");

        Assert.NotNull(notice);
        Assert.Equal(142, notice.TotalRows);
        Assert.Equal(20, notice.ShownRows);
    }

    [Theory]
    [InlineData("film_id | title\n11 | ALAMO VIDEOTAPE\n1 rows")]
    [InlineData("film_id\n6\n9\n2 rows")]
    [InlineData("NO ROWS")]
    [InlineData("NO ROWS. No film title contains that text.")]
    [InlineData("ERROR: 'film_id' must be a whole number, but got 'abc'.")]
    [InlineData("")]
    public void Returns_null_when_nothing_was_truncated(string output) =>
        Assert.Null(ToolOutputFormat.TryParseTruncation(output));

    [Fact]
    public void Round_trips_against_the_formatter_that_produces_the_line()
    {
        // The parser and the producer must agree. Asserting against a hand-written string only
        // proves the parser matches what the test author remembered.
        var rows = Enumerable.Range(1, 40).Select(i => (IReadOnlyList<object?>)new object?[] { i }).ToArray();
        var output = ToolOutputFormat.Rows(new QueryResult(["film_id"], rows), maxRows: 20);

        var notice = ToolOutputFormat.TryParseTruncation(output);

        Assert.NotNull(notice);
        Assert.Equal(40, notice.TotalRows);
        Assert.Equal(20, notice.ShownRows);
    }

    [Fact]
    public void An_untruncated_result_from_the_formatter_parses_as_no_notice()
    {
        var rows = Enumerable.Range(1, 5).Select(i => (IReadOnlyList<object?>)new object?[] { i }).ToArray();
        var output = ToolOutputFormat.Rows(new QueryResult(["film_id"], rows), maxRows: 20);

        Assert.Null(ToolOutputFormat.TryParseTruncation(output));
    }

    [Fact]
    public void Only_the_count_line_at_the_end_is_read_not_similar_text_in_a_data_row()
    {
        // A film whose title contains the phrase must not be mistaken for a truncation notice.
        // The pattern is anchored to the end of the output for exactly this reason.
        var notice = ToolOutputFormat.TryParseTruncation(
            "film_id | title\n7 | 500 rows, showing first 3\n1 rows");

        Assert.Null(notice);
    }

    [Fact]
    public void Trailing_whitespace_does_not_defeat_the_anchor()
    {
        var notice = ToolOutputFormat.TryParseTruncation("film_id\n6\n142 rows, showing first 20\n");

        Assert.NotNull(notice);
        Assert.Equal(142, notice.TotalRows);
    }

    [Fact]
    public void The_shown_count_can_equal_the_cap_without_the_total_being_a_round_number()
    {
        var notice = ToolOutputFormat.TryParseTruncation("rental_id\n1\n21 rows, showing first 20");

        Assert.NotNull(notice);
        Assert.Equal(21, notice.TotalRows);
        Assert.Equal(20, notice.ShownRows);
    }

    [Fact]
    public void The_version_constant_is_stamped_so_a_format_change_is_traceable()
    {
        // The contract version is written into every run record; changing the emitted text
        // without bumping it silently invalidates comparison with earlier sweeps.
        Assert.False(string.IsNullOrWhiteSpace(ToolOutputFormat.Version));
    }
}
