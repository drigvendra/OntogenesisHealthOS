using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace HealthOS.CoreApi.Health;

public sealed class DatabaseSettings
{
    public const string Section = "Database";

    /// <summary>Runtime connection (login role hos_app). Supplied via user-secrets or environment, never committed.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await using var cmd = dataSource.CreateCommand("SELECT 1");
            await cmd.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // A health check must convert every failure into an Unhealthy result.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Do not include exception details: they can contain host names or connection info.
            return HealthCheckResult.Unhealthy("database unavailable");
        }
    }
}
