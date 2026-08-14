using System.Globalization;
using Microsoft.Extensions.Options;
using MovieAgent.Agent.Recording;
using MovieAgent.Hosting;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Evaluation;

namespace MovieAgent.EntryPoints;

public sealed class EvalEntryPoint : IAppEntryPoint
{
    private readonly EvalRunner _runner;
    private readonly IRunRecorder _recorder;
    private readonly EvalSetOptions _evalSetOptions;
    private readonly IWireCapture _wireCapture;

    public EvalEntryPoint(
        EvalRunner runner,
        IRunRecorder recorder,
        IOptions<EvalSetOptions> evalSetOptions,
        IWireCapture wireCapture)
    {
        _runner = runner;
        _recorder = recorder;
        _evalSetOptions = evalSetOptions.Value;
        _wireCapture = wireCapture;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var evalSet = EvalSetLoader.LoadMany(_evalSetOptions.FileNames);
        var filter = args.Length > 0 ? args[0] : null;

        var summary = await _runner.RunAsync(evalSet, filter, cancellationToken);

        // Same treatment as `ask`: only wired for Ollama, and only written when something was
        // captured. Named after the recorder's file so a sweep's bodies and its runs can be
        // paired up afterwards — the per-response load_duration is the only place the harness
        // can see whether a model was resident or cold-loaded for a given call.
        //
        // IMPORTANT: AgentLoop resets the capture at the start of every run, so what lands here
        // is the LAST run of the sweep, not all of them. Enough to check the request parameters
        // and whether the model was resident; not a full transcript of the sweep.
        string? wireDirectory = null;
        if (_wireCapture.Enabled && _wireCapture.Bodies.Count > 0)
        {
            wireDirectory = Path.Combine(
                "runs", "wire", Path.GetFileNameWithoutExtension(_recorder.FilePath));
            await WireCaptureReporting.DumpBodiesAsync(_wireCapture, wireDirectory, cancellationToken);
        }

        Console.WriteLine();
        Console.WriteLine($"=== {summary.EvalSetId} on '{summary.Surface}' with {summary.Model} (thinking {(summary.Thinking ? "on" : "off")}) ===");

        if (summary.SqlShortcut is { } shortcut)
        {
            WriteSqlShortcutSummary(summary, shortcut);
            Console.WriteLine();
            Console.WriteLine($"recorded to {_recorder.FilePath}");
            if (wireDirectory is not null)
            {
                Console.WriteLine(
                    $"wire traffic for the LAST run only ({_wireCapture.Bodies.Count} exchange(s)) written to {wireDirectory}");
            }

            return 0;
        }

        Console.WriteLine(
            $"runs {summary.Runs}   correct {summary.Correct}   cap hits {summary.CapHits}   " +
            $"errors {summary.Errors}   empty answers {summary.EmptyAnswers}");
        Console.WriteLine(
            $"NAVIGATED CORRECT (strict): {summary.NavigatedCorrect}/{summary.Runs}   " +
            $"({summary.Correct - summary.NavigatedCorrect} pass(es) never reached a required tool)");
        Console.WriteLine($"mean tool calls per run: {summary.MeanToolCalls.ToString("0.00", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"mean tool calls per iteration: {summary.MeanCallsPerIteration.ToString("0.00", CultureInfo.InvariantCulture)} (batching)");
        Console.WriteLine($"repeated identical calls: {summary.RepeatedCalls}");
        Console.WriteLine(
            $"mean tokens per run: in {FormatMean(summary.MeanInputTokens)}, out {FormatMean(summary.MeanOutputTokens)}");
        Console.WriteLine(
            $"fabricated arguments: {summary.FabricatedArgumentCount} " +
            $"(invented id {summary.FabricatedIdCount}, invented search term {summary.FabricatedTermCount})   " +
            $"(call id as argument: {summary.CallIdAsArgumentCount})   " +
            $"type mismatches: {summary.ArgumentTypeMismatchCount}   " +
            $"schema errors: {summary.SchemaErrorCount}");
        Console.WriteLine(
            $"schema-enumerated ids: {summary.SchemaEnumeratedCount} " +
            "(swept a range the tool advertises — counted apart from fabrication, not as it)");
        Console.WriteLine();

        Console.WriteLine("by hop depth (answerable questions only)");
        Console.WriteLine("  hops   answered      navigated");
        foreach (var hop in summary.ByHopDepth)
        {
            var answered = hop.Runs == 0 ? 0 : 100.0 * hop.Correct / hop.Runs;
            var navigated = hop.Runs == 0 ? 0 : 100.0 * hop.Navigated / hop.Runs;
            Console.WriteLine(
                $"  {hop.Hops,-6} {hop.Correct}/{hop.Runs} ({answered.ToString("0", CultureInfo.InvariantCulture),3}%)" +
                $"     {hop.Navigated}/{hop.Runs} ({navigated.ToString("0", CultureInfo.InvariantCulture),3}%)");
        }

        Console.WriteLine("  navigated = reached every tool the shortest correct chain needs.");
        Console.WriteLine("  NECESSARY, NOT SUFFICIENT: navigation_complete checks that each required tool was");
        Console.WriteLine("  called, not that the chain was correct. A run can navigate completely via the wrong");
        Console.WriteLine("  path and still be marked complete. It can falsify a claim of navigation; it cannot");
        Console.WriteLine("  confirm one.");

        Console.WriteLine();
        Console.WriteLine("refusal");
        Console.WriteLine($"  should decline: {summary.Refusals.CorrectlyDeclined}/{summary.Refusals.Cases} correctly declined");
        Console.WriteLine($"  over-refusals:  {summary.Refusals.OverRefusals} answerable question(s) declined");
        Console.WriteLine();
        Console.WriteLine(
            "KNOWN FALSE-POSITIVE MODE: answer grading is substring matching and can pass a partly wrong");
        Console.WriteLine(
            "answer that happens to contain the right substring alongside the wrong one. Not attempted to fix.");
        Console.WriteLine();
        Console.WriteLine($"recorded to {_recorder.FilePath}");

        if (wireDirectory is not null)
        {
            Console.WriteLine(
                $"wire traffic for the LAST run only ({_wireCapture.Bodies.Count} exchange(s)) written to {wireDirectory}");
        }

        return 0;
    }

    /// <summary>"?" rather than "0" when nothing reported usage — the two are not the same fact.</summary>
    private static string FormatMean(double? mean) =>
        mean?.ToString("0", CultureInfo.InvariantCulture) ?? "?";

    /// <summary>
    /// The control surface's own summary. Deliberately does not print hop depth, navigation or
    /// argument provenance: with one generic tool those are undefined, and printing zeros would
    /// read as failures.
    /// </summary>
    private static void WriteSqlShortcutSummary(EvalSummary summary, SqlShortcutStats s)
    {
        static string N(double d) => d.ToString("0.00", CultureInfo.InvariantCulture);

        Console.WriteLine("CONTROL SURFACE — generic SQL. This is not a capability score.");
        Console.WriteLine();
        Console.WriteLine(
            $"correct {summary.Correct}/{s.Runs} run(s)   " +
            $"{s.QuestionsFullyCorrect}/{s.Questions} question(s) correct on every repeat");
        Console.WriteLine(
            $"cap hits {summary.CapHits}   errors {summary.Errors}   empty answers {summary.EmptyAnswers}");
        Console.WriteLine($"mean tool calls per run : {N(s.MeanToolCalls)}");
        Console.WriteLine($"mean iterations per run : {N(s.MeanIterations)}");
        Console.WriteLine(
            $"execute_sql calls       : {s.SqlCalls}   errors {s.SqlErrors} " +
            $"({N(s.MeanSqlErrorsPerRun)} per run)");
        Console.WriteLine($"get_schema calls        : {s.SchemaCalls}");
        Console.WriteLine(
            $"read the schema first   : {s.RunsReadingSchemaFirst}/{s.RunsWithSql} run(s) that wrote SQL");
        Console.WriteLine("  A model that writes SQL without reading the schema is guessing at table and column");
        Console.WriteLine("  names — the main sweep's fabrication instinct in a new form.");
        if (s.RunsWithNoToolCall > 0)
        {
            Console.WriteLine($"made no tool call at all: {s.RunsWithNoToolCall}/{s.Runs} run(s)");
        }

        Console.WriteLine();
        Console.WriteLine("  Not reported on this surface, because one generic tool makes them undefined rather");
        Console.WriteLine("  than zero: hop depth, navigation_complete, required_tools, argument provenance.");
        Console.WriteLine();
        Console.WriteLine("READ THE DELTA, NOT THE SCORE. Text-to-SQL is a far better represented capability than");
        Console.WriteLine("agentic tool composition, with vastly more training data behind it. A model scoring");
        Console.WriteLine("higher here shows that THE TASK CHANGED, not that the model is a better agent. The");
        Console.WriteLine("comparison worth making is against the same model's chain score on standard+desc.");
    }
}
