namespace MovieAgent.Agent;

public sealed record AgentRunRequest
{
    public required string QuestionId { get; init; }

    public required string Question { get; init; }

    public int? ExpectedHops { get; init; }

    public int Repeat { get; init; } = 1;

    /// <summary>Overrides <see cref="AgentOptions.ToolSurface"/> when set.</summary>
    public string? ToolSurfaceName { get; init; }

    public string SystemPrompt { get; init; } = Agent.SystemPrompt.Default;
}
