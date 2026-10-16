namespace ImageStore.Domain.Notifications;

public enum NotificationType
{
    ChatMessage = 1, NewFollower = 2, PostComment = 3, PostReaction = 4,
    CommentReply = 5, CommentMention = 6, SharedPost = 7, SystemAnnouncement = 8,
}

/// <summary>A durable inbox item and its delivery-outbox state. No private message/comment text.</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public NotificationType Type { get; set; }
    public string EventKey { get; set; } = "";
    public Guid? PostId { get; set; }
    public Guid? CommentId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public string? Announcement { get; set; }
    public bool Critical { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public int DeliveryAttempts { get; set; }
}

/// <summary>Preferences control presentation, not durable creation. Critical notices always appear.</summary>
public sealed class NotificationPreferences
{
    public Guid UserId { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool ChatEnabled { get; set; } = true;
    public bool FollowEnabled { get; set; } = true;
    public bool CommentsEnabled { get; set; } = true;
    public bool ReactionsEnabled { get; set; } = true;
    public bool MentionsEnabled { get; set; } = true;
    public bool AnnouncementsEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; }
    public int SoundVolume { get; set; } = 35;
    public bool SuppressActiveConversation { get; set; } = true;
}
