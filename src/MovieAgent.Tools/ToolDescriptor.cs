namespace MovieAgent.Tools;

/// <summary>
/// A tool is data, not code. Everything the harness needs to advertise a tool to the model
/// and then execute it lives in this record, so a tool surface is a list of these rather
/// than a set of classes.
/// </summary>
/// <remarks>
/// Invariants that the design of this harness depends on, enforced by
/// <see cref="ToolCatalogueValidator"/>:
/// <list type="bullet">
/// <item>One table per tool. No joins.</item>
/// <item>Foreign keys are returned raw. A tool never resolves a relationship on the model's behalf.</item>
/// <item>No pre-joined Pagila views.</item>
/// </list>
/// </remarks>
public sealed record ToolDescriptor
{
    /// <summary>Name as advertised to the model. Lower snake case.</summary>
    public required string Name { get; init; }

    /// <summary>Description as advertised to the model. This is prompt text — treat it as a run variable.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Parameterised SQL. Placeholders are <c>@name</c> and must match a <see cref="Parameters"/> entry
    /// exactly. No string concatenation of model input happens anywhere.
    /// </summary>
    public required string Sql { get; init; }

    /// <summary>The single table this tool reads. Recorded so the no-joins rule can be checked.</summary>
    public required string Table { get; init; }

    public IReadOnlyList<ToolParameter> Parameters { get; init; } = [];

    /// <summary>Rows shown to the model. The true total is always stated in the output.</summary>
    public int MaxRows { get; init; } = 20;

    /// <summary>
    /// Appended to the NO ROWS marker to make an empty result actionable, e.g.
    /// "Valid film_id values run 1-1000." Optional.
    /// </summary>
    public string? EmptyResultHint { get; init; }
}
