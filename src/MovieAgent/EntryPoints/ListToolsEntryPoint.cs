using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieAgent.Agent;
using MovieAgent.Hosting;
using MovieAgent.Agent.Tools;

namespace MovieAgent.EntryPoints;

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
        if (surface.GenericSql)
        {
            Console.WriteLine(
                "CONTROL SURFACE — generic SQL, everything the other surfaces forbid. Chain questions only.");
        }

        Console.WriteLine();

        foreach (var tool in surface.Resolve())
        {
            var schema = JsonSerializer.Serialize(
                ToolDeclarationFactory.Create(tool) is Microsoft.Extensions.AI.AIFunctionDeclaration d ? d.JsonSchema : default,
                new JsonSerializerOptions { WriteIndented = false });

            var header = tool.Kind == ToolKind.Descriptor
                ? $"{tool.Name}  [table: {tool.Table}, max rows: {tool.MaxRows}]"
                : $"{tool.Name}  [{tool.Kind}, max rows: {tool.MaxRows}]";

            Console.WriteLine(header);
            Console.WriteLine($"  {tool.Description}");
            Console.WriteLine($"  schema: {schema}");

            // Only descriptor tools have SQL to show. get_schema returns a constant and
            // execute_sql runs whatever the model sends, so printing an empty "sql:" line for
            // them would suggest something is missing.
            if (tool.Kind == ToolKind.Descriptor)
            {
                Console.WriteLine($"  sql:    {tool.Sql}");
            }

            Console.WriteLine();
        }

        if (surface.GenericSql)
        {
            Console.WriteLine("get_schema returns this, verbatim:");
            Console.WriteLine();
            foreach (var line in SqlShortcutCatalogue.SchemaListing.Split('\n'))
            {
                Console.WriteLine("  " + line.TrimEnd());
            }

            Console.WriteLine();
        }

        return Task.FromResult(0);
    }
}
