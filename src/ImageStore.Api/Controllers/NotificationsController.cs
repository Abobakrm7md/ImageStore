using ImageStore.Api.Chat;
using ImageStore.Api.Security;
using ImageStore.Application.Common;
using ImageStore.Application.Notifications;
using ImageStore.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace ImageStore.Api.Controllers;

[ApiController, Route("api/notifications")]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(24 * 1024)]
public sealed partial class NotificationsController(INotificationInbox inbox, INotificationDelivery delivery,
    ICurrentUser current, ChatLimits limits, ILogger<NotificationsController> logger) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List(CancellationToken ct, int page = 1, int pageSize = 20, bool? unread = null, NotificationType? type = null) =>
        inbox.ListAsync(page, pageSize, unread, type, ct);
    [HttpGet("{id:guid}")]
    public Task<NotificationDto> Get(Guid id, CancellationToken ct) => inbox.GetAsync(id, ct);
    [HttpGet("unread-count")]
    public async Task<object> Count(CancellationToken ct) => new { count = await inbox.UnreadCountAsync(ct) };
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) { Limit(); await inbox.MarkReadAsync(id, ct); await Changed(ct); return NoContent(); }
    [HttpPatch("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct) { Limit(); await inbox.MarkReadAsync(null, ct); await Changed(ct); return NoContent(); }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { Limit(); await inbox.DeleteAsync(id, ct); await Changed(ct); return NoContent(); }
    [HttpDelete("read")]
    public async Task<IActionResult> ClearRead(CancellationToken ct) { Limit(); await inbox.DeleteAsync(null, ct); await Changed(ct); return NoContent(); }
    [HttpGet("preferences")]
    public Task<NotificationPreferencesDto> Preferences(CancellationToken ct) => inbox.PreferencesAsync(ct);
    [HttpPut("preferences")]
    public async Task<NotificationPreferencesDto> SavePreferences(NotificationPreferencesDto request, CancellationToken ct)
    { Limit(); var result = await inbox.SavePreferencesAsync(request, ct); await Changed(ct); return result; }
    [HttpPost("announcements"), Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Announce(AnnouncementRequest request, CancellationToken ct)
    { Limit(); await inbox.AnnounceAsync(request, ct); return Accepted(); }
    private void Limit() => limits.Check(current.GetRequiredUserId().ToString());
    private async Task Changed(CancellationToken ct)
    {
        // A live invalidation failure must not turn a committed read/delete into an API failure.
        try { await delivery.ChangedAsync(current.GetRequiredUserId(), null, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { LogRefreshFailed(logger, ex); }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification inbox invalidation failed; clients will reconcile")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}
