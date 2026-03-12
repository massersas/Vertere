using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Service.Application.Csv;
using Service.Infrastructure.Csv;
using Service.Infrastructure.Sqlite;

namespace Service.Infrastructure;

/// <summary>
/// Dependency injection registration for the Infrastructure layer.
/// </summary>
public static class DependencyInjectionInfrastructure
{
    /// <summary>
    /// Registers Infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var dbPath = configuration["SchedulerPDV:SqlitePath"];
        var resolvedPath = string.IsNullOrWhiteSpace(dbPath)
            ? SqliteBootstrapper.GetDatabasePath()
            : dbPath;

        services.AddSingleton(new SqliteDatabaseOptions(resolvedPath));
        services.AddScoped<ICsvParserService, CsvParserService>();
        return services;
    }
}
