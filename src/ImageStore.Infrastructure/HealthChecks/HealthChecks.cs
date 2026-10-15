using ImageStore.Infrastructure.Persistence;
using ImageStore.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ImageStore.Infrastructure.HealthChecks;

internal sealed class DatabaseHealthCheck(ImageStoreDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database is unreachable.");
}

internal sealed class StorageHealthCheck(LocalImageFileStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var probe = Path.Combine(storage.StagingPath, $"health-{Guid.NewGuid():N}.probe");
        try
        {
            File.WriteAllBytes(probe, [0]);
            File.Delete(probe);
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The description is shown only in logs; the endpoint reports just the status.
            return Task.FromResult(HealthCheckResult.Unhealthy("The storage root is not writable.", exception));
        }
    }
}
