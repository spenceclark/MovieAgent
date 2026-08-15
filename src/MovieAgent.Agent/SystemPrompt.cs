using System.Security.Cryptography;
using System.Text;
using MovieAgent.Agent.Tools;

namespace MovieAgent.Agent;

/// <summary>
/// The system prompt is a run variable, not a constant of the harness. It is recorded in full
/// on every run line, and hashed so runs can be grouped by prompt version without string
/// comparison.
/// </summary>
/// <remarks>
public static class SystemPrompt
{
  public const string Default =
      """
        You are answering questions about a DVD rental database by calling tools.

        How the tools work:
        - Each tool reads exactly one table. None of them join tables for you.
        - Tools return identifiers, not names. A tool that returns language_id = 1 is telling
          you the identifier; you must call another tool to find out what language that is.
        - Search tools return identifiers only. Reading the full record is a separate call.
        - Answering most questions therefore takes several calls in sequence, where the
          argument to a later call comes from the result of an earlier one.

        Reading tool output:
        - Results are pipe-delimited with a header row, followed by a row count.
        - NO ROWS means the query matched nothing. It is not an error.
        - "40 rows, showing first 20" means you have been given a truncated list. Do not
          state a total based on a truncated list.
        - A line starting with ERROR explains what went wrong and whether retrying can help.

        Answering:
        - Work step by step. Where several lookups do not depend on each other you may ask for
          them in the same turn; where a call needs a value another call has to return first,
          wait for it rather than guessing the value.
        - When you have the answer, state it plainly in one or two sentences.
        - If the tools available to you cannot reach the answer, say so and explain what is
          missing. Do not guess, and do not answer from prior knowledge of this database.
          Declining when the data is not reachable is a correct answer.
        """;

  /// <summary>
  /// Prompt for the <c>sql-shortcut</c> control. This must not describe the one-table tool
  /// catalogue: this surface deliberately exposes the opposite abstraction.
  /// </summary>
  public const string SqlShortcut =
      """
        You are answering questions about a DVD rental database by querying it with tools.

        How the tools work:
        - get_schema returns the actual PostgreSQL base tables, columns, primary keys and foreign
          keys available to you. Call it before writing SQL; do not assume table or column names
          from prior knowledge of Sakila, Pagila or any other database.
        - execute_sql runs one read-only PostgreSQL SELECT statement. Joins, aggregates,
          subqueries and common table expressions are allowed. Writes and multiple statements
          are not allowed.
        - A question that would take several calls through the one-table tools may be answered by
          one query here. Use the relationships returned by get_schema to construct that query.

        Reading tool output:
        - Results are pipe-delimited with a header row, followed by a row count.
        - NO ROWS means the query matched nothing. It is not an error.
        - "40 rows, showing first 20" means you have been given a truncated list. Do not state a
          total based on the displayed rows when the true total is stated separately.
        - A line starting with ERROR explains why the query failed. Read it, correct the SQL and
          try again when the error is recoverable.

        Answering:
        - Base the answer on data returned by execute_sql, not on prior knowledge of this database.
        - When you have the answer, state it plainly in one or two sentences.
        - If the available schema and read-only queries cannot reach the answer, say so and
          explain what is missing. Do not guess.
        """;

  /// <summary>
  /// The sentence removed by <see cref="WithoutDeclineCredit"/>. Held separately so the control
  /// arm cannot drift from the prompt it is a control for.
  /// </summary>
  /// <remarks>
  /// Matched on the sentence, not on the line including its indentation: <see cref="Default"/> is
  /// a raw string literal, so the compiler strips the common leading whitespace and the emitted
  /// text carries two spaces where the source shows ten.
  /// </remarks>
  private const string DeclineCredit = "Declining when the data is not reachable is a correct answer.";

  /// <summary>
  /// <see cref="Default"/> with the sentence telling the model that a decline earns credit
  /// removed, and nothing else changed.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The control for a fair objection to the refusal numbers: the prompt states that declining is
  /// a correct answer, on the axis the results lead with, so those numbers measure appropriate
  /// instruction-<em>following</em> rather than any intrinsic disposition to decline. Every model
  /// gets the same instruction, so the comparison between models is sound either way — what is
  /// unknown without this arm is how much of the refusal behaviour the sentence is carrying.
  /// </para>
  /// <para>
  /// Removing one sentence changes the prompt hash, so runs under it are a separate population and
  /// the recorder keeps them distinguishable without anything having to be labelled by hand.
  /// </para>
  /// </remarks>
  public static string WithoutDeclineCredit
  {
      get
      {
          var lines = Default.Split('\n');
          var kept = lines.Where(l => !l.Contains(DeclineCredit, StringComparison.Ordinal)).ToArray();

          return kept.Length == lines.Length - 1
              ? string.Join('\n', kept)
              : throw new InvalidOperationException(
                  $"Expected exactly one prompt line containing the decline-credit sentence, found "
                  + $"{lines.Length - kept.Length}. The control arm would silently become a no-op or "
                  + "remove more than it should. Update SystemPrompt.DeclineCredit to match.");
      }
  }

  /// <summary>Select the prompt that describes the surface the model will actually receive.</summary>
  public static string ForSurface(ToolSurface surface, bool omitDeclineCredit = false) =>
      surface.GenericSql
          ? SqlShortcut
          : omitDeclineCredit
              ? WithoutDeclineCredit
              : Default;

  public static string Sha256(string prompt) =>
      Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
}
