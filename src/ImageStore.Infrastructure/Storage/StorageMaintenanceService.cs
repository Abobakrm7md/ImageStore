using ImageStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageStore.Infrastructure.Storage;

public sealed record StorageMaintenanceReport(int StagingFilesDeleted, int OrphanedFilesFound, int OrphanedFilesDeleted);

/// <summary>
/// Periodically reconciles the file system with the database:
/// <list type="bullet">
/// <item>deletes stale staging files left by interrupted uploads (e.g. a process crash);</item>
/// <item>reports stored files without a database record (and optionally deletes them), which can
/// occur when a file deletion fails after its metadata was removed.</item>
/// </list>
/// </summary>
public sealed partial class StorageMaintenanceService(
    LocalImageFileStorage storage,
    IServiceScopeFactory scopeFactory,
    IOptions<StorageMaintenanceOptions> options,
    TimeProvider timeProvider,
    ILogger<StorageMaintenanceService> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes)), timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DbUpdateException or InvalidOperationException)
            {
                LogRunFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<StorageMaintenanceReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var stagingDeleted = 0;
        var stagingMaxAge = TimeSpan.FromMinutes(settings.StagingFileMaxAgeMinutes);
        foreach (var (fullPath, lastWrite) in storage.EnumerateStagingFiles().ToList())
        {
            if (now - lastWrite >= stagingMaxAge && LocalImageFileStorage.TryDeleteFile(fullPath))
            {
                stagingDeleted++;
            }
        }

        if (stagingDeleted > 0)
        {
            LogStagingFilesDeleted(logger, stagingDeleted);
        }

        if (!settings.ScanForOrphanedFiles)
        {
            return new StorageMaintenanceReport(stagingDeleted, 0, 0);
        }

        var orphansFound = 0;
        var orphansDeleted = 0;
        var minAge = TimeSpan.FromMinutes(settings.OrphanedFileMinAgeMinutes);
        var candidates = storage.EnumerateStoredFiles().Where(file => now - file.LastWriteTimeUtc >= minAge).Select(file => file.RelativePath);

        foreach (var batch in candidates.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ImageStoreDbContext>();
            var known = await dbContext.Images
                .AsNoTracking()
                .Where(image => batch.Contains(image.RelativeStoragePath))
                .Select(image => image.RelativeStoragePath)
                .ToListAsync(cancellationToken);
            var knownPaths = known.ToHashSet(StringComparer.Ordinal);

            foreach (var orphan in batch.Where(path => !knownPaths.Contains(path)))
            {
                orphansFound++;
                if (settings.DeleteOrphanedFiles && await storage.DeleteAsync(orphan, cancellationToken))
                {
                    orphansDeleted++;
                    LogOrphanDeleted(logger, orphan);
                }
                else
                {
                    LogOrphanFound(logger, orphan);
                }
            }
        }

        return new StorageMaintenanceReport(stagingDeleted, orphansFound, orphansDeleted);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} stale staging file(s).")]
    private static partial void LogStagingFilesDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored file '{RelativePath}' has no database record (orphaned).")]
    private static partial void LogOrphanFound(ILogger logger, string relativePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleted orphaned stored file '{RelativePath}'.")]
    private static partial void LogOrphanDeleted(ILogger logger, string relativePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storage maintenance run failed.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
