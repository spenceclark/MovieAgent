using System.Text.RegularExpressions;

namespace MovieAgent.Agent.Tools;

/// <summary>
/// Startup guard against the shortcuts this harness exists to avoid. Runs over the whole
/// catalogue, not just the selected surface, so a bad descriptor cannot hide in an unused
/// tool and get switched on later without anyone noticing.
/// </summary>
public static partial class ToolCatalogueValidator
{
    /// <summary>
    /// Pagila's pre-joined views. Exposing any of these — or querying them anywhere in the
    /// codebase — collapses multi-hop questions to a single call.
    /// </summary>
    public static IReadOnlyList<string> BannedObjects { get; } =
    [
        "film_list",
        "actor_info",
        "nicer_but_slower_film_list",
        "sales_by_store",
        "sales_by_film_category",
        "staff_list",
        "customer_list",
        // Schema introspection is a shortcut of the same kind: it hands the model the map.
        "information_schema",
        "pg_catalog",
    ];

    public static void ValidateOrThrow(IEnumerable<ToolDescriptor> descriptors)
    {
        var errors = new List<string>();

        foreach (var tool in descriptors)
        {
            // The shortcut control's tools have no fixed SQL and would sail through every check
            // below vacuously. They are kept in SqlShortcutCatalogue precisely so they are never
            // validated as if they were safe; finding one here means someone merged the two.
            if (tool.Kind != ToolKind.Descriptor)
            {
                errors.Add(
                    $"Tool '{tool.Name}' has kind '{tool.Kind}' and must not be in the main catalogue. " +
                    "Shortcut tools belong in SqlShortcutCatalogue.");
                continue;
            }

            var sql = tool.Sql;

            // Dropping `required` from Sql/Table to accommodate the other kinds would otherwise
            // let a descriptor with no query through, so require them explicitly instead.
            if (string.IsNullOrWhiteSpace(sql))
            {
                errors.Add($"Tool '{tool.Name}' is a descriptor tool with no SQL.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(tool.Table))
            {
                errors.Add($"Tool '{tool.Name}' is a descriptor tool with no declared table.");
                continue;
            }

            foreach (var banned in BannedObjects)
            {
                if (Regex.IsMatch(sql, $@"\b{Regex.Escape(banned)}\b", RegexOptions.IgnoreCase))
                {
                    errors.Add($"Tool '{tool.Name}' references banned object '{banned}'.");
                }
            }

            if (JoinPattern().IsMatch(sql))
            {
                errors.Add($"Tool '{tool.Name}' contains a join. Tools read exactly one table.");
            }

            // A second "from" means a subquery or a comma join, both of which are joins by
            // another name.
            if (FromPattern().Matches(sql).Count > 1)
            {
                errors.Add($"Tool '{tool.Name}' has more than one FROM clause.");
            }

            if (!Regex.IsMatch(sql, $@"\bfrom\s+{Regex.Escape(tool.Table)}\b", RegexOptions.IgnoreCase))
            {
                errors.Add($"Tool '{tool.Name}' declares table '{tool.Table}' but its SQL does not select from it.");
            }

            // Every placeholder must correspond to a declared parameter and vice versa,
            // otherwise argument validation can be silently bypassed.
            var placeholders = PlaceholderPattern().Matches(sql).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var declared = tool.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var extra in placeholders.Except(declared))
            {
                errors.Add($"Tool '{tool.Name}' uses placeholder '@{extra}' with no matching parameter.");
            }

            foreach (var unused in declared.Except(placeholders))
            {
                errors.Add($"Tool '{tool.Name}' declares parameter '{unused}' that its SQL never uses.");
            }

            if (tool.MaxRows <= 0)
            {
                errors.Add($"Tool '{tool.Name}' has a non-positive MaxRows.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Tool catalogue violates the no-shortcuts rules:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
        }
    }

    [GeneratedRegex(@"\bjoin\b", RegexOptions.IgnoreCase)]
    private static partial Regex JoinPattern();

    [GeneratedRegex(@"\bfrom\b", RegexOptions.IgnoreCase)]
    private static partial Regex FromPattern();

    [GeneratedRegex(@"@([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderPattern();
}
