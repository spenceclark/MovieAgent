using Microsoft.Extensions.Options;
using MovieAgent.Hosting;
using MovieAgent.Evaluation;

namespace MovieAgent.EntryPoints;

public sealed class RegradeEntryPoint : IAppEntryPoint
{
    private readonly Regrader _regrader;
    private readonly EvalSetOptions _evalSetOptions;

    public RegradeEntryPoint(Regrader regrader, IOptions<EvalSetOptions> evalSetOptions)
    {
        _regrader = regrader;
        _evalSetOptions = evalSetOptions.Value;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: regrade <input.jsonl> [output.jsonl]");
            return 1;
        }

        var input = args[0];
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"No such file: {input}");
            return 1;
        }

        // Never overwrite in place. The original file is the raw record of what happened, and a
        // regrade round-trips it through deserialise/reserialise, so writing back would also drop
        // any property this build does not know about.
        var output = args.Length > 1
            ? args[1]
            : Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(input))!,
                Path.GetFileNameWithoutExtension(input) + ".regraded.jsonl");

        if (ReportEntryPoint.SamePath(input, output))
        {
            Console.Error.WriteLine($"Refusing to regrade over the raw record: {input}");
            return 1;
        }

        var result = await _regrader.RegradeAsync(input, output, EvalSetLoader.LoadMany(_evalSetOptions.FileNames), cancellationToken);

        Console.WriteLine($"read {result.Total} run(s), regraded {result.Regraded}, skipped {result.Skipped} (not in eval set)");
        Console.WriteLine();

        // Grade flips are unexpected — a diagnostic addition changed a scoring decision, which
        // should not happen. Outcome reclassifications are expected on the first regrade after
        // EmptyAnswer was added, and reported separately so the two are never conflated.
        Console.WriteLine($"{result.Changed} grade(s) changed" + (result.Changed > 0 ? " -- UNEXPECTED, read these:" : ""));
        foreach (var change in result.Changes)
        {
            Console.WriteLine($"  {change}");
        }

        Console.WriteLine();
        Console.WriteLine($"{result.OutcomeReclassified} outcome(s) reclassified (Answered -> EmptyAnswer)");
        foreach (var change in result.OutcomeReclassifications)
        {
            Console.WriteLine($"  {change}");
        }

        Console.WriteLine();
        Console.WriteLine($"written to {output}");
        return result.Changed > 0 ? 1 : 0;
    }
}
