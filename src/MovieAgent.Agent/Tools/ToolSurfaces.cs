namespace MovieAgent.Agent.Tools;

/// <summary>A named subset of <see cref="ToolCatalogue.All"/>. The experimental variable.</summary>
public sealed record ToolSurface(string Name, IReadOnlyList<string> ToolNames)
{
    public IReadOnlyList<ToolDescriptor> Resolve() =>
        [.. ToolNames.Select(n => ToolCatalogue.ByName.TryGetValue(n, out var d)
            ? d
            : throw new InvalidOperationException($"Surface '{Name}' names unknown tool '{n}'."))];
}

/// <summary>
/// The three surfaces under comparison.
/// </summary>
/// <remarks>
/// JUDGEMENT CALL — the brief named the three surfaces but not their contents, so this is my
/// reading and the most likely thing you will want to change:
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

    public static IReadOnlyDictionary<string, ToolSurface> ByName { get; } =
        new Dictionary<string, ToolSurface>(StringComparer.OrdinalIgnoreCase)
        {
            [Minimal.Name] = Minimal,
            [Standard.Name] = Standard,
            [StandardWithDescription.Name] = StandardWithDescription,
            [Enriched.Name] = Enriched,
        };

    public static ToolSurface Get(string name) =>
        ByName.TryGetValue(name, out var surface)
            ? surface
            : throw new InvalidOperationException(
                $"Unknown tool surface '{name}'. Known surfaces: {string.Join(", ", ByName.Keys)}.");
}
