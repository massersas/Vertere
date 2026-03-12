using Microsoft.Extensions.DependencyInjection;

namespace Service.Application;

/// <summary>
/// Dependency injection registration for the Application layer.
/// </summary>
public static class DependencyInjectionApplication
{
    /// <summary>
    /// Registers Application services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<Service.Application.Scheduling.IScheduleGenerator, Service.Application.Scheduling.ScheduleGenerator>();
        return services;
    }
}
