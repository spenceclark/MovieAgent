using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Configuration;
using MovieAgent.Agent.Models;
using Npgsql;

namespace MovieAgent.Data.Sql;

/// <summary>
/// Runs SQL through a pooled <see cref="NpgsqlDataSource"/> and materialises a
/// <see cref="QueryResult"/>. Values are read loosely on purpose: Pagila uses custom
/// types (the <c>mpaa_rating</c> enum, <c>tsvector</c>) that we do not want to have to
/// map ahead of time just to read a row.
/// </summary>
public sealed class NpgsqlQueryExecutor : ISqlQueryExecutor
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly DatabaseOptions _options;
    private readonly ILogger<NpgsqlQueryExecutor> _logger;

    public NpgsqlQueryExecutor(
        NpgsqlDataSource dataSource,
        IOptions<DatabaseOptions> options,
        ILogger<NpgsqlQueryExecutor> logger)
    {
        _dataSource = dataSource;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QueryResult> QueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        _logger.LogDebug("Executing SQL: {Sql}", sql);

        await using var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _options.CommandTimeoutSeconds;

        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns[i] = reader.GetName(i);
        }

        var rows = new List<IReadOnlyList<object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = ReadValue(reader, i);
            }

            rows.Add(row);
        }

        _logger.LogDebug("SQL returned {RowCount} row(s).", rows.Count);
        return new QueryResult(columns, rows);
    }

    /// <summary>
    /// Reads a single field, falling back to the raw PostgreSQL text representation for
    /// types Npgsql has no CLR mapping for (unmapped enums, domains, composites).
    /// </summary>
    private static object? ReadValue(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        try
        {
            return reader.GetValue(ordinal);
        }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException or ArgumentException)
        {
            return reader.GetFieldValue<string>(ordinal);
        }
    }
}
