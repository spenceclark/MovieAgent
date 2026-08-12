namespace MovieAgent.Core.Models;

/// <summary>
/// A provider-agnostic tabular result set. Pure data — rendering lives in
/// MovieAgent.Tools.ToolOutputFormat, which is the single frozen output contract.
/// </summary>
public sealed class QueryResult
{
    public QueryResult(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }

    public int RowCount => Rows.Count;

    public static QueryResult Empty { get; } = new([], []);
}
