using System.Security.Cryptography;
using System.Text;

namespace MovieAgent.Agent;

/// <summary>
/// The system prompt is a run variable, not a constant of the harness. It is recorded in full
/// on every run line, and hashed so runs can be grouped by prompt version without string
/// comparison.
/// </summary>
/// <remarks>
/// JUDGEMENT CALL — this wording is a starting point and is one of the things most worth
/// owning yourself. Three choices in it that will affect the numbers:
/// <list type="bullet">
/// <item>It states that identifiers must be resolved by further calls. Removing that sentence
/// tests whether the model works the constraint out unaided, which is arguably the more
/// interesting measurement.</item>
/// <item>It gives explicit permission to decline. Without it, refusal accuracy will collapse
/// towards zero and you will be measuring sycophancy rather than reasoning.</item>
/// <item>It says nothing about the schema. Describing the tables would be a shortcut of the
/// same family as a get_schema tool.</item>
/// </list>
/// </remarks>
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
        - Work step by step. Call one tool, read the result, then decide the next call.
        - When you have the answer, state it plainly in one or two sentences.
        - If the tools available to you cannot reach the answer, say so and explain what is
          missing. Do not guess, and do not answer from prior knowledge of this database.
          Declining when the data is not reachable is a correct answer.
        """;

    public static string Sha256(string prompt) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
}
