using System.Text.Json;

namespace MovieAgent.Evaluation;

public static class EvalSetLoader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public const string DefaultFileName = "pagila-v1.json";

    public static EvalSet Load(string? path = null)
    {
        var resolved = path ?? Path.Combine(AppContext.BaseDirectory, "EvalSet", DefaultFileName);

        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException($"Eval set not found at '{resolved}'.", resolved);
        }

        var json = File.ReadAllText(resolved);
        var set = JsonSerializer.Deserialize<EvalSet>(json, _options)
                  ?? throw new InvalidOperationException($"Eval set at '{resolved}' deserialised to null.");

        var duplicates = set.Questions
            .GroupBy(q => q.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Eval set has duplicate question ids: {string.Join(", ", duplicates)}.");
        }

        return set;
    }

    /// <summary>
    /// Loads and merges several eval set files, so a v1-only run and a v1+v2 run are both a
    /// one-line config change rather than two code paths. Each question id must be unique across
    /// the whole merge — v1 and v2 are additive by convention, and a collision almost certainly
    /// means a copy-paste id reused across files, not an intentional override.
    /// </summary>
    public static EvalSet LoadMany(IEnumerable<string> fileNames)
    {
        var names = fileNames.ToArray();
        if (names.Length == 0)
        {
            throw new InvalidOperationException("No eval set file names given.");
        }

        var sets = names.Select(name => Load(Path.Combine(AppContext.BaseDirectory, "EvalSet", name))).ToArray();

        var duplicates = sets
            .SelectMany(s => s.Questions)
            .GroupBy(q => q.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Question id(s) appear in more than one eval set file: {string.Join(", ", duplicates)}.");
        }

        return new EvalSet
        {
            EvalSetId = string.Join("+", sets.Select(s => s.EvalSetId)),
            Notes = [.. sets.SelectMany(s => s.Notes)],
            Questions = [.. sets.SelectMany(s => s.Questions)],
        };
    }
}
