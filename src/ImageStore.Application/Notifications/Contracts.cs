using ImageStore.Application.Common;
using ImageStore.Domain.Notifications;
namespace ImageStore.Application.Notifications;

public sealed record NotificationDto(Guid Id, NotificationType Type, string Text, string? ActorDisplayName,
    DateTime CreatedAtUtc, DateTime? ReadAtUtc, bool Critical, bool Available,
    Guid? ActorUserId, Guid? PostId, Guid? CommentId, Guid? ConversationId, Guid? MessageId);
public sealed record NotificationPreferencesDto(bool InAppEnabled = true, bool ChatEnabled = true,
    bool FollowEnabled = true, bool CommentsEnabled = true, bool ReactionsEnabled = true,
    bool MentionsEnabled = true, bool AnnouncementsEnabled = true, bool SoundEnabled = false,
    int SoundVolume = 35, bool SuppressActiveConversation = true, bool BrowserPushEnabled = false);
public sealed record AnnouncementRequest(Guid EventId, IReadOnlyList<Guid> RecipientUserIds, string Text, bool Critical = false);

public interface INotificationInbox
{
    Task<PagedResult<NotificationDto>> ListAsync(int page, int pageSize, bool? unread, NotificationType? type, CancellationToken ct);
    Task<NotificationDto> GetAsync(Guid id, CancellationToken ct);
    Task<int> UnreadCountAsync(CancellationToken ct);
    Task MarkReadAsync(Guid? id, CancellationToken ct);
    Task DeleteAsync(Guid? id, CancellationToken ct);
    Task<NotificationPreferencesDto> PreferencesAsync(CancellationToken ct);
    Task<NotificationPreferencesDto> SavePreferencesAsync(NotificationPreferencesDto request, CancellationToken ct);
    Task AnnounceAsync(AnnouncementRequest request, CancellationToken ct);
}
/// <summary>Transport abstraction; implementation targets only the recipient's authenticated connections.</summary>
public interface INotificationDelivery
{
    Task ChangedAsync(Guid recipientUserId, Guid? notificationId, CancellationToken ct);
}
