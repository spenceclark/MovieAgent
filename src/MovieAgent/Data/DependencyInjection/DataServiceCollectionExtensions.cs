using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Configuration;
using MovieAgent.Data.Sql;
using Npgsql;

namespace MovieAgent.Data.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Npgsql data source and everything that reads through it.
    /// </summary>
    public static IServiceCollection AddMovieAgentData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateSelf();

        // One pooled data source for the process lifetime.
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

            var builder = new NpgsqlDataSourceBuilder(options.ConnectionString);
            builder.UseLoggerFactory(serviceProvider.GetRequiredService<ILoggerFactory>());
            return builder.Build();
        });

        services.AddScoped<ISqlQueryExecutor, NpgsqlQueryExecutor>();

        return services;
    }
}
