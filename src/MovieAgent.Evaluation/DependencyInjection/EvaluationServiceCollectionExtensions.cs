using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MovieAgent.Evaluation.DependencyInjection;

public static class EvaluationServiceCollectionExtensions
{
    public static IServiceCollection AddMovieAgentEvaluation(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EvalSetOptions>(configuration.GetSection(EvalSetOptions.SectionName));
        services.AddScoped<EvalRunner>();
        services.AddScoped<EvalSetVerifier>();
        services.AddScoped<Regrader>();
        return services;
    }
}
