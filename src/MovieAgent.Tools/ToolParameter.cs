namespace MovieAgent.Tools;

public enum ToolParameterType
{
    Integer,
    Text,
}

/// <summary>
/// One parameter of a tool. Carries both the schema advertised to the model and the
/// validation applied to whatever the model actually sends, which are not the same thing:
/// model arguments are untrusted input and are re-checked against these bounds at execution
/// time rather than assumed to conform to the advertised schema.
/// </summary>
public sealed record ToolParameter
{
    public required string Name { get; init; }

    public required ToolParameterType Type { get; init; }

    public required string Description { get; init; }

    public bool Required { get; init; } = true;

    /// <summary>Inclusive lower bound for <see cref="ToolParameterType.Integer"/>.</summary>
    public long? Minimum { get; init; }

    /// <summary>Inclusive upper bound for <see cref="ToolParameterType.Integer"/>.</summary>
    public long? Maximum { get; init; }

    /// <summary>
    /// Minimum length for <see cref="ToolParameterType.Text"/>. Defaults to 2 so that a
    /// blank or single-character search term is rejected rather than quietly returning the
    /// whole table — that would be a list-everything tool by the back door.
    /// </summary>
    public int MinLength { get; init; } = 2;

    public int MaxLength { get; init; } = 100;

    public static ToolParameter Id(string name, string description, long max) => new()
    {
        Name = name,
        Type = ToolParameterType.Integer,
        Description = description,
        Minimum = 1,
        Maximum = max,
    };

    public static ToolParameter Term(string name, string description) => new()
    {
        Name = name,
        Type = ToolParameterType.Text,
        Description = description,
    };
}
