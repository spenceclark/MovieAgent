using MovieAgent.Agent.Models;

namespace MovieAgent.Agent.Abstractions;

/// <summary>
/// Executes read-only SQL against the movie database and returns a generic result set.
/// </summary>
public interface ISqlQueryExecutor
{
    /// <param name="sql">SQL to execute. Use named placeholders (<c>@name</c>) for values.</param>
    /// <param name="parameters">Values bound to the named placeholders in <paramref name="sql"/>.</param>
    Task<QueryResult> QueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes SQL inside a <c>READ ONLY</c> transaction, so the database itself refuses a
    /// write regardless of what the text says.
    /// </summary>
    /// <remarks>
    /// Exists for the <c>sql-shortcut</c> surface, where the query is written by the model.
    /// <see cref="MovieAgent.Agent.Tools.SqlShortcutGuard"/> screens the text first; this is the
    /// barrier that does not depend on getting a regex right. The main catalogue's tools run
    /// through <see cref="QueryAsync"/> because their SQL is fixed at compile time.
    /// </remarks>
    Task<QueryResult> QueryReadOnlyAsync(
        string sql,
        CancellationToken cancellationToken = default);
}
