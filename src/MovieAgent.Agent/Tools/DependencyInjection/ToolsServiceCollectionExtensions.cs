using Microsoft.Extensions.DependencyInjection;

namespace MovieAgent.Agent.Tools.DependencyInjection;

public static class ToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tool executor and validates the whole catalogue up front.
    /// </summary>
    /// <remarks>
    /// Validation runs here, at registration, rather than lazily. A descriptor that quietly
    /// joins two tables would not throw until the model happened to call it, by which point
    /// it would have contaminated a run.
    /// </remarks>
    public static IServiceCollection AddMovieAgentTools(this IServiceCollection services)
    {
        ToolCatalogueValidator.ValidateOrThrow(ToolCatalogue.All);

        // Fail now if a surface names a tool that does not exist.
        foreach (var surface in ToolSurfaces.ByName.Values)
        {
            _ = surface.Resolve();
        }

        services.AddScoped<ToolExecutor>();
        return services;
    }
}
