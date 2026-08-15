namespace MovieAgent.Agent;

public sealed record AgentRunRequest
{
    public required string QuestionId { get; init; }

    public required string Question { get; init; }

    public int? ExpectedHops { get; init; }

    public int Repeat { get; init; } = 1;

    /// <summary>Overrides <see cref="AgentOptions.ToolSurface"/> when set.</summary>
    public string? ToolSurfaceName { get; init; }

    /// <summary>
    /// The prompt must be selected for the run's tool surface. Required so a new caller cannot
    /// silently pair the SQL surface with the one-table prompt (or vice versa).
    /// </summary>
    public required string SystemPrompt { get; init; }
}
