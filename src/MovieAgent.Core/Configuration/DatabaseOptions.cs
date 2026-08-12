namespace MovieAgent.Core.Configuration;

/// <summary>
/// Connection settings for the Pagila PostgreSQL database.
/// </summary>
public sealed class DatabaseOptions : IValidatableOptions
{
    public const string SectionName = "Database";

    /// <summary>Full Npgsql connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Seconds to wait for a command before timing out.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    public IEnumerable<string> GetValidationErrors()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            yield return $"'{SectionName}:{nameof(ConnectionString)}' is required.";
        }

        if (CommandTimeoutSeconds < 0)
        {
            yield return $"'{SectionName}:{nameof(CommandTimeoutSeconds)}' must not be negative.";
        }
    }
}
