using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MovieAgent.Agent.Recording;
using MovieAgent.Agent.Tools;

namespace MovieAgent.Evaluation;

public enum ArgumentProvenance
{
    /// <summary>The value appeared in the question or an earlier tool result, sent as the right JSON kind.</summary>
    Grounded,

    /// <summary>The value appears nowhere in the run — invented, not derived.</summary>
    Fabricated,

    /// <summary>The value is grounded, but sent as the wrong JSON kind for the tool's declared parameter type.</summary>
    TypeMismatch,
}

public sealed record ArgumentProvenanceEntry(
    int Iteration,
    string ToolName,
    string ParameterName,
    string ValueRaw,
    ArgumentProvenance Provenance,
    bool IsCallIdAsArgument);

public sealed record ToolCallDiagnosticsSummary(
    int FabricatedArgumentCount,
    int FabricatedIdCount,
    int FabricatedTermCount,
    IReadOnlyList<string> FabricatedArguments,
    int CallIdAsArgumentCount,
    int ArgumentTypeMismatchCount,
    int SchemaErrorCount,
    IReadOnlyList<string> SchemaErrors);

/// <summary>
/// Two diagnostics computed purely from a recorded <see cref="RunRecord"/>: where did each tool
/// argument's value actually come from, and did a failed call fail for a schema reason (wrong
/// parameter name or type) rather than a data reason (no such row, out of range, repeated).
/// </summary>
/// <remarks>
/// Both are regrade-safe by construction — nothing here needs anything beyond
/// <c>ArgumentsRaw</c>, <c>ResultText</c> and <c>Question</c>, all already on disk for every run
/// ever recorded. Tool parameter <em>types</em> are looked up against the current
/// <see cref="ToolCatalogue"/>, not something recorded per run, which mirrors how the rest of
/// grading already works: current code applied retroactively to recorded data.
/// <para>
/// <b>What "per call" means here.</b> Every argument on every call is classified, but only the
/// non-Grounded ones (fabricated, or a schema-failed call) are written out as evidence lists —
/// matching how <c>required_tools_missing</c> already only lists the gaps, not the whole
/// requirement set twice. The full per-argument breakdown is not part of the recorded shape.
/// </para>
/// <para>
/// <b>Known blind spot, not attempted here.</b> Grounding is textual, not semantic: if the
/// value <c>1</c> appears anywhere in an earlier result — under any column, for any entity — an
/// argument using <c>1</c> for something unrelated still counts as Grounded. A model that reads
/// a <c>store_id</c> of 1 from one row and later sends <c>customer_id: 1</c> would not be
/// caught. Column-aware grounding would need to parse each tool's result shape per call, which
/// this deterministic classifier does not attempt — same trade-off this session already made for
/// <see cref="Grader"/>'s substring answer matching.
/// </para>
/// </remarks>
public static partial class ToolCallDiagnostics
{
    public static ToolCallDiagnosticsSummary Analyse(RunRecord run)
    {
        var allCalls = run.Iterations
            .SelectMany(it => it.ToolCalls.Select(c => (Iteration: it.Iteration, Call: c)))
            .ToList();

        var fabricatedArguments = new List<string>();
        var fabricatedCount = 0;
        var fabricatedIdCount = 0;
        var fabricatedTermCount = 0;
        var callIdCount = 0;
        var typeMismatchCount = 0;
        var schemaErrors = new List<string>();

        for (var i = 0; i < allCalls.Count; i++)
        {
            var (iteration, call) = allCalls[i];

            if (call.IsError && IsSchemaError(call.ResultText))
            {
                schemaErrors.Add($"iter {iteration}: {call.ToolName}: {Truncate(call.ResultText, 160)}");
            }

            // Only iterations strictly before this one, and only successful calls: everything in
            // the same turn was decided in one shot, before the model saw any of that turn's own
            // results, so a sibling call cannot have grounded this argument. Error text is
            // excluded for a sharper reason — this harness's own error messages echo the
            // model's input back ("...but got '$store_id'"), so a fabricated value that has
            // already failed once would otherwise "ground" itself on retry, via nothing but its
            // own error message repeating it. Confirmed against a real run: without this
            // exclusion, get_store({"store_id":"$store_id"}) reads as grounded (and therefore
            // TypeMismatch, not Fabricated) the second time it is sent, purely because the
            // first attempt's error text contains the same string.
            var corpus = string.Join(
                '\n',
                [
                    run.Question,
                    .. allCalls
                        .Where(x => x.Iteration < iteration && !x.Call.IsError)
                        .Select(x => StripRowCount(x.Call.ResultText)),
                ]);

            ToolLookup.ByName.TryGetValue(call.ToolName, out var tool);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(call.ArgumentsRaw);
            }
            catch (JsonException)
            {
                // arguments_raw is always produced by serialising a dictionary (see
                // AgentLoop.SerialiseArguments), so this should not happen — but a diagnostic
                // pass over old data should degrade, not abort, on something unexpected.
                continue;
            }

            using (document)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    var valueRaw = Stringify(property.Value);
                    var isCallId = CallIdPattern().IsMatch(valueRaw);
                    var declared = tool?.Parameters.FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal));

                    ArgumentProvenance provenance;

                    if (isCallId)
                    {
                        // A call id is never legitimate data under any tool's schema — the model
                        // is reading an identifier this harness injected into the conversation
                        // and passing it back as though it were a row value.
                        provenance = ArgumentProvenance.Fabricated;
                        callIdCount++;
                    }
                    else if (!IsGrounded(valueRaw, corpus))
                    {
                        provenance = ArgumentProvenance.Fabricated;
                    }
                    else
                    {
                        provenance = declared is not null && IsTypeMismatch(declared.Type, property.Value.ValueKind)
                            ? ArgumentProvenance.TypeMismatch
                            : ArgumentProvenance.Grounded;
                    }

                    if (provenance == ArgumentProvenance.Fabricated)
                    {
                        fabricatedCount++;

                        // The split that matters: inventing a row id asserts a specific record
                        // exists, which is the hallucination this metric was added to catch.
                        // Inventing a search term is how searching works — a model hunting for
                        // an entity that turns out not to exist will invent several, and that is
                        // correct behaviour, not a fault. Counting them together made the strong
                        // models look reckless: qwen3.5:9b's 48 were 46 search terms on questions
                        // whose subject does not exist. Undeclared parameters fall in neither
                        // bucket — the call is already counted as a schema error.
                        switch (declared?.Type)
                        {
                            case ToolParameterType.Integer:
                                fabricatedIdCount++;
                                break;
                            case ToolParameterType.Text:
                                fabricatedTermCount++;
                                break;
                        }

                        fabricatedArguments.Add($"iter {iteration}: {call.ToolName}.{property.Name}={valueRaw}");
                    }
                    else if (provenance == ArgumentProvenance.TypeMismatch)
                    {
                        typeMismatchCount++;
                    }
                }
            }
        }

        return new ToolCallDiagnosticsSummary(
            fabricatedCount,
            fabricatedIdCount,
            fabricatedTermCount,
            fabricatedArguments,
            callIdCount,
            typeMismatchCount,
            schemaErrors.Count,
            schemaErrors);
    }

    /// <summary>
    /// Wrong parameter name or wrong JSON type — matched against <see cref="ToolArgumentBinder"/>'s
    /// own message templates, verbatim, rather than re-deriving the validation. Deliberately
    /// excludes out-of-range numeric ids and too-short search terms: those are the right type in
    /// the right shape, just referring to data that is not there — a data reason, not a schema one.
    /// </summary>
    private static bool IsSchemaError(string resultText) =>
        resultText.Contains("does not take '", StringComparison.Ordinal)
        || resultText.Contains("requires the argument '", StringComparison.Ordinal)
        || resultText.Contains("must be a whole number, but got '", StringComparison.Ordinal);

    private static bool IsGrounded(string valueRaw, string corpus)
    {
        if (valueRaw.Length == 0)
        {
            return false;
        }

        // Numeric values are matched as a bounded token, not a raw substring: "4" must not match
        // inside "24" or "4.99" (a price), or nearly every small integer id would spuriously
        // look grounded against any result containing a bigger number or a decimal.
        if (long.TryParse(valueRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            var pattern = new Regex($@"(?<!\d){Regex.Escape(valueRaw)}(?!\d)(?!\.\d)");
            return pattern.IsMatch(corpus);
        }

        return corpus.Contains(valueRaw, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTypeMismatch(ToolParameterType declaredType, JsonValueKind actualKind) => declaredType switch
    {
        ToolParameterType.Integer => actualKind == JsonValueKind.String,
        ToolParameterType.Text => actualKind == JsonValueKind.Number,
        _ => false,
    };

    /// <summary>
    /// Strips the tool-output contract's trailing count line ("N rows" / "N rows, showing first
    /// M") before a result is used as grounding evidence.
    /// </summary>
    /// <remarks>
    /// Without this, almost every small integer argument reads as grounded for the wrong reason:
    /// any prior result with, say, exactly 1 row ends in the line "1 rows", and a naive search
    /// would find "1" there and call it grounded — a row *count*, not a row *value*. Confirmed
    /// against a real run: <c>get_film({"film_id":"1"})</c> in hop3-film-language, where the only
    /// prior result was search_film's single-row hit for ADAPTATION HOLES (the real film_id is
    /// 3) — its result text ends in "1 rows", and without this strip that "1" would have made a
    /// fabricated film_id look grounded.
    /// </remarks>
    private static string StripRowCount(string resultText) => RowCountPattern().Replace(resultText, string.Empty);

    private static string Stringify(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    /// <summary>Matches the two exact shapes <c>ToolOutputFormat.Rows</c> produces.</summary>
    [GeneratedRegex(@"^\d+ rows(?:, showing first \d+)?\s*$", RegexOptions.Multiline)]
    private static partial Regex RowCountPattern();

    /// <summary>Matches this harness's own normalised call ids (see <c>AgentLoop.NormaliseCallIds</c>).</summary>
    [GeneratedRegex(@"^call_\d+$")]
    private static partial Regex CallIdPattern();
}
