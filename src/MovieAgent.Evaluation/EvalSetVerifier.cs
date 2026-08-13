using System.Globalization;
using MovieAgent.Agent.Abstractions;

namespace MovieAgent.Evaluation;

public sealed record VerificationResult(string QuestionId, bool Matches, string Expected, string Actual, string? Error);

/// <summary>
/// Re-runs every reference_sql and compares the result with the recorded expected_answer.
/// </summary>
/// <remarks>
/// The eval set is only as trustworthy as the database it was derived from. Reload Pagila,
/// or point at a different instance, and half the answers silently become wrong — which would
/// show up as a model accuracy collapse and waste a day. Run this before any measurement run.
/// <para>
/// This is the one place in the harness allowed to execute joined SQL, because it is not part
/// of the model's tool surface and never reaches the model.
/// </para>
/// </remarks>
public sealed class EvalSetVerifier
{
    private readonly ISqlQueryExecutor _sql;

    public EvalSetVerifier(ISqlQueryExecutor sql)
    {
        _sql = sql;
    }

    public async Task<IReadOnlyList<VerificationResult>> VerifyAsync(
        EvalSet evalSet,
        CancellationToken cancellationToken = default)
    {
        var results = new List<VerificationResult>(evalSet.Questions.Count);

        foreach (var question in evalSet.Questions)
        {
            try
            {
                var result = await _sql.QueryAsync(question.ReferenceSql, null, cancellationToken);

                // Flattened across every cell in the result, not just the first column of each
                // row. A set answer can come back shaped either way: several rows of one column
                // each (v1's category list) or one row of several columns (v2's "name, count"
                // pair) — both are just a bag of expected values to a Set question, and only the
                // first shape is even meaningful for a single scalar Exact/Numeric answer.
                var actualValues = result.Rows.SelectMany(r => r).Select(Format).ToArray();
                var actual = string.Join("; ", actualValues);
                var expected = question.ExpectedAnswer ?? string.Empty;

                var matches = question.AnswerKind switch
                {
                    // A refusal question's reference SQL proves absence, so zero rows is the pass.
                    AnswerKind.Decline => result.RowCount == 0 || actual is "0",

                    // An ambiguity question's reference SQL counts the valid answers. If that ever
                    // falls to one the question has quietly become answerable and must be requalified.
                    AnswerKind.Ambiguous => long.TryParse(actual, out var n) && n > 1,
                    AnswerKind.Set => SetMatches(expected, actualValues),
                    _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                };

                results.Add(new VerificationResult(question.Id, matches, expected, actual, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new VerificationResult(question.Id, false, question.ExpectedAnswer ?? string.Empty, string.Empty, ex.Message));
            }
        }

        return results;
    }

    private static bool SetMatches(string expected, IEnumerable<string> actual)
    {
        var expectedSet = expected
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return expectedSet.SetEquals(actual.Select(a => a.Trim()));
    }

    private static string Format(object? value) => value switch
    {
        null or DBNull => "NULL",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
