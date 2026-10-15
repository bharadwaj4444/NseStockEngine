using Microsoft.Extensions.DependencyInjection;

namespace NseStock.Infrastructure;

public static class HealthCheck
{
    public static IServiceCollection AddHealthCheckInfrastructure(
        this IServiceCollection services)
    {
        services.AddHealthChecks()
                .AddCheck<PostgresHealthCheck>("Postgres", tags: new[] { "ready" });
        
        return services;
    }
}