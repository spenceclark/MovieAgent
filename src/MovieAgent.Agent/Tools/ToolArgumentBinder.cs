using System.Globalization;
using System.Text.Json;

namespace MovieAgent.Agent.Tools;

public sealed record ArgumentBindingResult(
    IReadOnlyDictionary<string, object?>? Values,
    string? Error)
{
    public bool Success => Error is null;
}

/// <summary>
/// Turns whatever the model sent into SQL parameter values, or an actionable refusal.
/// </summary>
/// <remarks>
/// Model arguments are untrusted input. The advertised JSON schema is a hint to the model,
/// not a guarantee about what arrives — small models routinely send <c>"6"</c> for an integer,
/// <c>6.0</c> for an id, or an argument that was never declared. Every value is re-checked
/// here against <see cref="ToolParameter"/> before it reaches Npgsql.
/// </remarks>
public static class ToolArgumentBinder
{
    public static ArgumentBindingResult Bind(ToolDescriptor tool, IReadOnlyDictionary<string, object?>? arguments)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        arguments ??= new Dictionary<string, object?>();

        var unknown = arguments.Keys
            .Where(k => !tool.Parameters.Any(p => string.Equals(p.Name, k, StringComparison.Ordinal)))
            .ToArray();

        if (unknown.Length > 0)
        {
            var expected = tool.Parameters.Count == 0
                ? "no arguments"
                : string.Join(", ", tool.Parameters.Select(p => p.Name));

            return new(null, $"{tool.Name} does not take {string.Join(", ", unknown.Select(u => $"'{u}'"))}. It takes {expected}.");
        }

        foreach (var parameter in tool.Parameters)
        {
            if (!arguments.TryGetValue(parameter.Name, out var raw) || raw is null || IsJsonNull(raw))
            {
                if (parameter.Required)
                {
                    return new(null, $"{tool.Name} requires the argument '{parameter.Name}' ({parameter.Description}).");
                }

                values[parameter.Name] = null;
                continue;
            }

            var (value, error) = parameter.Type switch
            {
                ToolParameterType.Integer => CoerceInteger(tool, parameter, raw),
                ToolParameterType.Text => CoerceText(tool, parameter, raw),
                _ => (null, $"{tool.Name} has an unsupported parameter type for '{parameter.Name}'."),
            };

            if (error is not null)
            {
                return new(null, error);
            }

            values[parameter.Name] = value;
        }

        return new(values, null);
    }

    private static (object? Value, string? Error) CoerceInteger(ToolDescriptor tool, ToolParameter parameter, object raw)
    {
        var text = Stringify(raw);

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            // "6.0" is a whole number the model happened to send as a float. Accept it;
            // "6.5" is a genuine mistake and is not accepted.
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                && Math.Abs(d % 1) < double.Epsilon
                && d is >= long.MinValue and <= long.MaxValue)
            {
                number = (long)d;
            }
            else
            {
                return (null, $"{tool.Name}: '{parameter.Name}' must be a whole number, but got '{text}'.");
            }
        }

        if (parameter.Minimum is { } min && number < min)
        {
            return (null, $"{tool.Name}: '{parameter.Name}' must be at least {min}, but got {number}.");
        }

        if (parameter.Maximum is { } max && number > max)
        {
            return (null,
                $"{tool.Name}: '{parameter.Name}' must be at most {max}, but got {number}. " +
                $"There is no such record.");
        }

        return ((int)number, null);
    }

    private static (object? Value, string? Error) CoerceText(ToolDescriptor tool, ToolParameter parameter, object raw)
    {
        var text = Stringify(raw).Trim();

        if (text.Length < parameter.MinLength)
        {
            return (null,
                $"{tool.Name}: '{parameter.Name}' must be at least {parameter.MinLength} characters. " +
                $"This tool will not list every row — give it something to search for.");
        }

        if (text.Length > parameter.MaxLength)
        {
            return (null, $"{tool.Name}: '{parameter.Name}' must be at most {parameter.MaxLength} characters.");
        }

        return (text, null);
    }

    private static bool IsJsonNull(object raw) =>
        raw is JsonElement { ValueKind: JsonValueKind.Null };

    private static string Stringify(object raw) => raw switch
    {
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? string.Empty,
        JsonElement e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => raw.ToString() ?? string.Empty,
    };
}
