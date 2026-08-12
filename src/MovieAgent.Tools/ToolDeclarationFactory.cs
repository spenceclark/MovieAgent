using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MovieAgent.Tools;

/// <summary>
/// Turns descriptors into the declaration-only tool objects handed to the model.
/// </summary>
/// <remarks>
/// These are <see cref="AIFunctionDeclaration"/>, not <see cref="AIFunction"/>, so they
/// cannot be invoked. That is deliberate: the harness drives the tool loop by hand in order
/// to record every call, so nothing must be able to quietly execute a tool behind its back.
/// The corollary is that <c>UseFunctionInvocation()</c> must stay off the chat pipeline.
/// </remarks>
public static class ToolDeclarationFactory
{
    public static IList<AITool> CreateFor(ToolSurface surface) =>
        [.. surface.Resolve().Select(Create)];

    public static AITool Create(ToolDescriptor descriptor) =>
        new DescriptorDeclaration(descriptor);

    internal static JsonElement BuildSchema(ToolDescriptor descriptor)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        var required = new List<string>();

        foreach (var parameter in descriptor.Parameters)
        {
            var property = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = parameter.Type == ToolParameterType.Integer ? "integer" : "string",
                ["description"] = parameter.Description,
            };

            if (parameter.Type == ToolParameterType.Integer)
            {
                if (parameter.Minimum is { } min)
                {
                    property["minimum"] = min;
                }

                if (parameter.Maximum is { } max)
                {
                    property["maximum"] = max;
                }
            }
            else
            {
                property["minLength"] = parameter.MinLength;
                property["maxLength"] = parameter.MaxLength;
            }

            properties[parameter.Name] = property;

            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        var schema = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private sealed class DescriptorDeclaration : AIFunctionDeclaration
    {
        private readonly JsonElement _schema;

        public DescriptorDeclaration(ToolDescriptor descriptor)
        {
            Descriptor = descriptor;
            _schema = BuildSchema(descriptor);
        }

        public ToolDescriptor Descriptor { get; }

        public override string Name => Descriptor.Name;

        public override string Description => Descriptor.Description;

        public override JsonElement JsonSchema => _schema;
    }
}
