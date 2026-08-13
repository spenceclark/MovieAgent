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
}
