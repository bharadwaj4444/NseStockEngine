using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

public class PostgresHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            var command = new NpgsqlCommand("select 1", connection);

            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("Postgres is healthy");
        }
        catch(Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Postgres is unreachable");
        }
    }
}