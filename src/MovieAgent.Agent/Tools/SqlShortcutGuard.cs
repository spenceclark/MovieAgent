using System.Text.RegularExpressions;

namespace MovieAgent.Agent.Tools;

/// <summary>
/// Decides whether a model-supplied query is safe to run on the <c>sql-shortcut</c> surface.
/// </summary>
/// <remarks>
/// Text matching is a weak barrier on its own, which is why it is not the only one: the query
/// also runs inside a <c>READ ONLY</c> transaction (see
/// <c>ISqlQueryExecutor.QueryReadOnlyAsync</c>), so Postgres refuses a write even if something
/// gets past this. Both layers exist because the connection string in this harness is a
/// superuser, and the SQL is written by a language model.
/// <para>
/// Rejections come back to the model as ordinary retryable tool output, not exceptions — a
/// blocked query is a fact about the run worth recording, and how the model reacts to being
/// told "read-only" is part of what this surface measures.
/// </para>
/// </remarks>
public static partial class SqlShortcutGuard
{
    /// <summary>
    /// Statement keywords that are never allowed. <c>explain</c> is here because
    /// <c>EXPLAIN ANALYZE</c> executes the statement it is explaining, writes included.
    /// </summary>
    private static readonly string[] _forbiddenKeywords =
    [
        "insert", "update", "delete", "merge", "truncate", "drop", "alter", "create", "grant",
        "revoke", "comment", "copy", "call", "do", "execute", "prepare", "vacuum", "analyze",
        "refresh", "reindex", "cluster", "listen", "notify", "lock", "explain", "set", "reset",
        "begin", "commit", "rollback", "savepoint", "into",
    ];

    public sealed record Verdict(bool Allowed, string? Reason, string Sql);

    public static Verdict Inspect(string? rawQuery)
    {
        var sql = (rawQuery ?? string.Empty).Trim();

        if (sql.Length == 0)
        {
            return new Verdict(false, "The query was empty.", sql);
        }

        // One statement only. A trailing semicolon is fine and common; anything after it is a
        // second statement, which is the classic way to smuggle a write past a prefix check.
        sql = sql.TrimEnd();
        while (sql.EndsWith(';'))
        {
            sql = sql[..^1].TrimEnd();
        }

        if (sql.Contains(';'))
        {
            return new Verdict(false, "Only one statement is allowed, and it must not contain a semicolon.", sql);
        }

        var stripped = StripLiteralsAndComments(sql);

        if (!StartsReadOnlyPattern().IsMatch(stripped))
        {
            return new Verdict(false, "Only a single read-only SELECT (optionally opening with WITH) is allowed.", sql);
        }

        foreach (var keyword in _forbiddenKeywords)
        {
            if (Regex.IsMatch(stripped, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
            {
                return new Verdict(false, $"The query uses '{keyword.ToUpperInvariant()}', which is not allowed on this read-only surface.", sql);
            }
        }

        // The same banned-object list the main catalogue is held to. Pagila's pre-joined views
        // would make this surface a different experiment again — the control is "generic SQL
        // over the same base tables", not "plus the convenience views" — and the schema is
        // already available through get_schema, so introspection buys nothing here.
        foreach (var banned in ToolCatalogueValidator.BannedObjects)
        {
            if (Regex.IsMatch(stripped, $@"\b{Regex.Escape(banned)}\b", RegexOptions.IgnoreCase))
            {
                return new Verdict(
                    false,
                    $"'{banned}' is not available. Query the base tables directly; use get_schema to see them.",
                    sql);
            }
        }

        return new Verdict(true, null, sql);
    }

    /// <summary>
    /// Blanks out string literals and comments before keyword matching, so a film title
    /// containing the word "update" is not mistaken for an UPDATE statement.
    /// </summary>
    private static string StripLiteralsAndComments(string sql)
    {
        var noBlockComments = BlockCommentPattern().Replace(sql, " ");
        var noLineComments = LineCommentPattern().Replace(noBlockComments, " ");
        var noStrings = SingleQuotedPattern().Replace(noLineComments, " '' ");
        return DoubleQuotedPattern().Replace(noStrings, " \"\" ");
    }

    [GeneratedRegex(@"^\s*(select|with)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StartsReadOnlyPattern();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentPattern();

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineCommentPattern();

    [GeneratedRegex(@"'(?:[^']|'')*'")]
    private static partial Regex SingleQuotedPattern();

    [GeneratedRegex("\"(?:[^\"]|\"\")*\"")]
    private static partial Regex DoubleQuotedPattern();
}
