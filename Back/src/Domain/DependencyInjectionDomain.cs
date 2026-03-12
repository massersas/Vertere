using Microsoft.Extensions.DependencyInjection;

namespace Service.Domain;

/// <summary>
/// Dependency injection registration for the Domain layer.
/// </summary>
public static class DependencyInjectionDomain
{
    /// <summary>
    /// Registers Domain services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddDomain(this IServiceCollection services)
    {
        return services;
    }
}
