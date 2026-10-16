using ImageStore.Application.Notifications;
using ImageStore.Domain.Notifications;
using ImageStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace ImageStore.Infrastructure.Notifications;

/// <summary>At-least-once delivery of committed inbox IDs. Never delivers message content.</summary>
public sealed partial class NotificationDispatcher(ImageStoreDbContext db, INotificationDelivery delivery,
    TimeProvider clock, ILogger<NotificationDispatcher> logger)
{
    public async Task DispatchBatchAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await db.Set<Notification>().Where(n => n.DispatchedAtUtc == null && n.NextAttemptAtUtc <= now)
            .OrderBy(n => n.NextAttemptAtUtc).Take(50).ToListAsync(ct);
        foreach (var n in pending)
        {
            try
            {
                if (n.DismissedAtUtc is null) await delivery.ChangedAsync(n.RecipientUserId, n.Id, ct);
                n.DispatchedAtUtc = now;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                LogDeliveryFailed(logger, n.Id, ex);
                n.DeliveryAttempts = Math.Min(n.DeliveryAttempts + 1, 1000000);
                n.NextAttemptAtUtc = now.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(n.DeliveryAttempts, 8))));
            }
            await db.SaveChangesAsync(ct);
        }
    }
    public async Task RetainAsync(CancellationToken ct)
    {
        // Read/dismissed payloads expire after 90 days. Unread items NEVER expire automatically.
        // Keep minimal dedup tombstones indefinitely so removed reactions/refollows cannot spam.
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-90);
        var rows = await db.Set<Notification>().Where(n => n.CreatedAtUtc < cutoff &&
            (n.ReadAtUtc != null || n.DismissedAtUtc != null) &&
            (n.DismissedAtUtc == null || n.ActorUserId != null || n.Announcement != null || n.PostId != null || n.MessageId != null))
            .Take(200).ToListAsync(ct);
        foreach (var n in rows)
        {
            n.DismissedAtUtc ??= clock.GetUtcNow().UtcDateTime;
            n.ActorUserId = null; n.Announcement = null; n.PostId = null; n.CommentId = null;
            n.MessageId = null; n.ConversationId = null;
        }
        await db.SaveChangesAsync(ct);
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification delivery failed for {NotificationId}; retained for retry")]
    private static partial void LogDeliveryFailed(ILogger logger, Guid notificationId, Exception exception);
}

internal sealed partial class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:DispatchEnabled", true)) return;
        // Delay until startup database initialization has finished. Failures are retried, not fatal.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var ticks = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                await dispatcher.DispatchBatchAsync(stoppingToken);
                if (++ticks % 1800 == 0) await dispatcher.RetainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { LogBatchFailed(logger, ex); }
        }
    }
    [LoggerMessage(Level = LogLevel.Error, Message = "Notification outbox batch failed; will retry")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);
}
