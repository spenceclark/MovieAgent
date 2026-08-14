namespace MovieAgent.Agent.Tools;

/// <summary>
/// How a tool is executed. Everything in <see cref="ToolCatalogue"/> is
/// <see cref="Descriptor"/>; the other two exist only for the <c>sql-shortcut</c> control
/// surface and are deliberately kept out of the main catalogue.
/// </summary>
public enum ToolKind
{
    /// <summary>Fixed parameterised SQL over exactly one table. The only kind the main catalogue allows.</summary>
    Descriptor,

    /// <summary>Returns a static schema listing. No parameters, no query.</summary>
    Schema,

    /// <summary>Runs SQL supplied by the model, after a read-only guard. A shortcut by definition.</summary>
    FreeSql,
}

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
/// Those invariants are asserted over <see cref="ToolCatalogue.All"/>, which the validator also
/// requires to be entirely <see cref="ToolKind.Descriptor"/> — so the shortcut tools cannot be
/// added to it by accident.
/// </remarks>
public sealed record ToolDescriptor
{
    /// <summary>Name as advertised to the model. Lower snake case.</summary>
    public required string Name { get; init; }

    /// <summary>Description as advertised to the model. This is prompt text — treat it as a run variable.</summary>
    public required string Description { get; init; }

    public ToolKind Kind { get; init; } = ToolKind.Descriptor;

    /// <summary>
    /// Parameterised SQL. Placeholders are <c>@name</c> and must match a <see cref="Parameters"/> entry
    /// exactly. No string concatenation of model input happens anywhere.
    /// </summary>
    /// <remarks>
    /// Empty for the non-<see cref="ToolKind.Descriptor"/> kinds, which have no fixed query. The
    /// validator requires it to be present for every <see cref="ToolKind.Descriptor"/> tool, so
    /// dropping <c>required</c> here does not weaken the main catalogue's guarantee.
    /// </remarks>
    public string Sql { get; init; } = string.Empty;

    /// <summary>The single table this tool reads. Recorded so the no-joins rule can be checked.</summary>
    public string Table { get; init; } = string.Empty;

    public IReadOnlyList<ToolParameter> Parameters { get; init; } = [];

    /// <summary>Rows shown to the model. The true total is always stated in the output.</summary>
    public int MaxRows { get; init; } = 20;

    /// <summary>
    /// Appended to the NO ROWS marker to make an empty result actionable, e.g.
    /// "Valid film_id values run 1-1000." Optional.
    /// </summary>
    public string? EmptyResultHint { get; init; }
}
