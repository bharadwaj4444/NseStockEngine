using Microsoft.Extensions.DependencyInjection;
using NseStock.Analysis.Contracts;
using NseStock.Infrastructure.Repositories;

namespace NseStock.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNseStockInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddScoped<ITechnicalIndicatorRepository>(
            _ => new PostgresTechnicalIndicatorRepository(
                connectionString));

        services.AddScoped<IHistoricalPriceRepository>(
            _ => new PostgresHistoricalPriceRepository(connectionString)
        );

        return services;
    }
}