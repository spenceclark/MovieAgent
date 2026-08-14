namespace MovieAgent.Agent.Tools;

/// <summary>A named subset of <see cref="ToolCatalogue.All"/>. The experimental variable.</summary>
public sealed record ToolSurface(string Name, IReadOnlyList<string> ToolNames)
{
    /// <summary>
    /// True for the <c>sql-shortcut</c> control surface, where one generic tool answers every
    /// question.
    /// </summary>
    /// <remarks>
    /// Grading has to change shape when this is set, not just produce worse numbers.
    /// <c>requires_tools</c> names tools that do not exist here, so the surface-relative decline
    /// rule would mark every question unanswerable; and navigation, hop depth and argument
    /// provenance have no meaning when there is one tool. Those are suppressed rather than
    /// reported as zero, because zero reads as a failure and "not applicable" is the truth.
    /// </remarks>
    public bool GenericSql { get; init; }

    public IReadOnlyList<ToolDescriptor> Resolve() =>
        [.. ToolNames.Select(n => ToolLookup.ByName.TryGetValue(n, out var d)
            ? d
            : throw new InvalidOperationException($"Surface '{Name}' names unknown tool '{n}'."))];
}

/// <summary>
/// Every tool the harness knows about, main catalogue and shortcut control together.
/// </summary>
/// <remarks>
/// The two catalogues stay separate so <see cref="ToolCatalogueValidator"/> can keep proving
/// things about <see cref="ToolCatalogue.All"/> alone. This is the one place they are merged,
/// and only for name resolution.
/// </remarks>
public static class ToolLookup
{
    public static IReadOnlyDictionary<string, ToolDescriptor> ByName { get; } =
        ToolCatalogue.All
            .Concat(SqlShortcutCatalogue.All)
            .ToDictionary(t => t.Name, StringComparer.Ordinal);
}

/// <summary>
/// The three surfaces under comparison.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// <b>minimal</b> is search and read on the three entity tables only. No junction tools at all,
/// which means relationship questions are genuinely unreachable here rather than merely hard.
/// That is what makes it a usable control for the "declines when it cannot get there" metric.
/// </item>
/// <item>
/// <b>standard</b> adds the lookup tables and the junction tools. This is the surface the
/// hop-depth numbers are really about.
/// </item>
/// <item>
/// <b>standard+desc</b> is standard plus description search.
/// </item>
/// <item>
/// <b>enriched</b> is standard plus the count tools, so the counting variant is one config
/// change rather than a code change.
/// </item>
/// </list>
/// Both variants differ from standard by exactly one thing, deliberately: standard stays the
/// fixed control, and any accuracy difference has one candidate cause rather than two.
/// Note that total-catalogue questions ("how many films in total") are unreachable on both
/// minimal and standard by construction: every search tool requires a term of at least two
/// characters, so there is no list-everything path. That is deliberate — it gives the counting
/// variant something to actually change.
/// </remarks>
public static class ToolSurfaces
{
    public static ToolSurface Minimal { get; } = new("minimal",
    [
        "search_film",
        "get_film",
        "search_actor",
        "get_actor",
        "search_customer",
        "get_customer",
    ]);

    public static ToolSurface Standard { get; } = new("standard",
    [
        .. Minimal.ToolNames,
        "search_category",
        "get_category",
        "get_language",
        "get_address",
        "get_city",
        "get_country",
        "get_store",
        "get_staff",
        "get_inventory_item",
        "get_rental",
        "get_film_actor_ids",
        "get_actor_film_ids",
        "get_film_category_ids",
        "get_category_film_ids",
        "get_film_inventory_ids",
        "get_customer_rental_ids",
        "get_inventory_rental_ids",
        "get_customer_payments",
    ]);

    /// <summary>Standard plus description search. Differs from standard by exactly one tool.</summary>
    public static ToolSurface StandardWithDescription { get; } = new("standard+desc",
    [
        .. Standard.ToolNames,
        "search_film_description",
    ]);

    public static ToolSurface Enriched { get; } = new("enriched",
    [
        .. Standard.ToolNames,
        "count_films",
        "count_film_actors",
        "count_actor_films",
        "count_category_films",
        "count_customer_rentals",
    ]);

    /// <summary>
    /// THE CONTROL, NOT A CAPABILITY. Two tools: read the schema, run any SELECT. Everything the
    /// other four surfaces exist to forbid.
    /// </summary>
    /// <remarks>
    /// The no-shortcuts constraint is the premise of the whole harness, and until this surface
    /// existed it was asserted rather than measured. Running the chain questions here separates
    /// two failures the main sweep conflates: a model that cannot emit a structured tool call at
    /// all, and a model that can call one tool but cannot compose a chain across turns. The first
    /// should fail here too — <c>execute_sql</c> is still a tool call. The second should improve
    /// sharply, because one call now suffices.
    /// <para>
    /// <b>An improvement here is not evidence of a better agent.</b> Text-to-SQL has far more
    /// training data behind it than agentic tool composition, so a model doing better on this
    /// surface shows the task changed. Read the delta, not the absolute score.
    /// </para>
    /// </remarks>
    public static ToolSurface SqlShortcut { get; } = new("sql-shortcut",
    [
        "get_schema",
        "execute_sql",
    ])
    { GenericSql = true };

    public static IReadOnlyDictionary<string, ToolSurface> ByName { get; } =
        new Dictionary<string, ToolSurface>(StringComparer.OrdinalIgnoreCase)
        {
            [Minimal.Name] = Minimal,
            [Standard.Name] = Standard,
            [StandardWithDescription.Name] = StandardWithDescription,
            [Enriched.Name] = Enriched,
            [SqlShortcut.Name] = SqlShortcut,
        };

    public static ToolSurface Get(string name) =>
        ByName.TryGetValue(name, out var surface)
            ? surface
            : throw new InvalidOperationException(
                $"Unknown tool surface '{name}'. Known surfaces: {string.Join(", ", ByName.Keys)}.");
}
