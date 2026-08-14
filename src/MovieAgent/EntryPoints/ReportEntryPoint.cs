using MovieAgent.Evaluation;
using MovieAgent.Hosting;

namespace MovieAgent.EntryPoints;

/// <summary>
/// Renders a recorded JSONL file as a markdown transcript: question, iterations, tool calls,
/// grading, with the stats at each level.
/// </summary>
public sealed class ReportEntryPoint : IAppEntryPoint
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: report <input.jsonl> [output.md]");
            return 1;
        }

        var input = args[0];
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"No such file: {input}");
            return 1;
        }

        // Alongside the input by default, same convention as regrade. Reports are derived data:
        // regenerating one must never be able to overwrite the recorded run it came from, which
        // the .md extension guarantees.
        var output = args.Length > 1
            ? args[1]
            : Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(input))!,
                Path.GetFileNameWithoutExtension(input) + ".report.md");

        var result = await RunReport.WriteAsync(input, output, cancellationToken);

        Console.WriteLine($"{result.Runs} run(s) reported"
            + (result.Unreadable > 0 ? $", {result.Unreadable} unreadable line(s) skipped" : string.Empty));
        Console.WriteLine($"written to {result.OutputPath}");
        return 0;
    }
}
