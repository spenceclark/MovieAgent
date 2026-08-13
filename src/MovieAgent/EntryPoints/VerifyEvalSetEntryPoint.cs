using Microsoft.Extensions.Options;
using MovieAgent.Hosting;
using MovieAgent.Evaluation;

namespace MovieAgent.EntryPoints;

public sealed class VerifyEvalSetEntryPoint : IAppEntryPoint
{
    private readonly EvalSetVerifier _verifier;
    private readonly EvalSetOptions _evalSetOptions;

    public VerifyEvalSetEntryPoint(EvalSetVerifier verifier, IOptions<EvalSetOptions> evalSetOptions)
    {
        _verifier = verifier;
        _evalSetOptions = evalSetOptions.Value;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var evalSet = EvalSetLoader.LoadMany(_evalSetOptions.FileNames);
        var results = await _verifier.VerifyAsync(evalSet, cancellationToken);

        Console.WriteLine($"=== verifying {evalSet.EvalSetId} against the live database ===");
        Console.WriteLine();

        foreach (var result in results)
        {
            if (result.Error is not null)
            {
                Console.WriteLine($"ERROR {result.QuestionId}: {result.Error}");
            }
            else if (result.Matches)
            {
                Console.WriteLine($"ok    {result.QuestionId}: {result.Actual}");
            }
            else
            {
                Console.WriteLine($"STALE {result.QuestionId}: expected '{result.Expected}', database says '{result.Actual}'");
            }
        }

        var stale = results.Count(r => !r.Matches);

        Console.WriteLine();
        Console.WriteLine(stale == 0
            ? $"All {results.Count} expected answers match the database."
            : $"{stale} of {results.Count} expected answers are stale. Fix the eval set before measuring.");

        return stale == 0 ? 0 : 1;
    }
}
