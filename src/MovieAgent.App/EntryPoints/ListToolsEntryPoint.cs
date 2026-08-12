using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieAgent.Agent;
using MovieAgent.App.Hosting;
using MovieAgent.Tools;

namespace MovieAgent.App.EntryPoints;

/// <summary>
/// Prints a surface exactly as the model will be shown it. Useful when a run goes strangely
/// and you want to check what the model was actually offered rather than what you intended.
/// </summary>
public sealed class ListToolsEntryPoint : IAppEntryPoint
{
    private readonly AgentOptions _options;

    public ListToolsEntryPoint(IOptions<AgentOptions> options)
    {
        _options = options.Value;
    }

    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var surface = ToolSurfaces.Get(args.Length > 0 ? args[0] : _options.ToolSurface);

        Console.WriteLine($"surface: {surface.Name} ({surface.ToolNames.Count} tools)");
        Console.WriteLine();

        foreach (var tool in surface.Resolve())
        {
            var schema = JsonSerializer.Serialize(
                ToolDeclarationFactory.Create(tool) is Microsoft.Extensions.AI.AIFunctionDeclaration d ? d.JsonSchema : default,
                new JsonSerializerOptions { WriteIndented = false });

            Console.WriteLine($"{tool.Name}  [table: {tool.Table}, max rows: {tool.MaxRows}]");
            Console.WriteLine($"  {tool.Description}");
            Console.WriteLine($"  schema: {schema}");
            Console.WriteLine($"  sql:    {tool.Sql}");
            Console.WriteLine();
        }

        return Task.FromResult(0);
    }
}
