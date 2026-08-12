namespace MovieAgent.Evaluation;

/// <summary>
/// Which eval set file(s) to load. Defaults to v1 alone, so nothing that already depends on
/// v1's numbers changes behaviour unless this is set explicitly.
/// </summary>
public sealed class EvalSetOptions
{
    public const string SectionName = "EvalSet";

    /// <summary>Comma-separated file names under the EvalSet directory.</summary>
    public string Files { get; set; } = EvalSetLoader.DefaultFileName;

    public IReadOnlyList<string> FileNames =>
        [.. Files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
