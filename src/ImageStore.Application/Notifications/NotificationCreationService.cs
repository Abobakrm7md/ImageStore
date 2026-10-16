using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ImageStore.Domain.Chat;
using ImageStore.Domain.Posts;
using ImageStore.Domain.Social;
using ImageStore.Domain.Notifications;
namespace ImageStore.Application.Notifications;

public sealed record NotificationEvent(string Key, Guid Recipient, Guid? Actor, NotificationType Type,
    DateTime CreatedAtUtc, Guid? PostId = null, Guid? CommentId = null, Guid? ConversationId = null,
    Guid? MessageId = null, string? Announcement = null, bool Critical = false);

/// <summary>
/// Central creation policy, used by the transactional domain-event bridge and trusted announcements.
/// The persistence adapter stages its result in the SAME unit of work as the originating action.
/// </summary>
public sealed partial class NotificationCreationService
{
    [GeneratedRegex(@"(?<![\w@])@([A-Za-z0-9][A-Za-z0-9_.-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Mentions();

    /// <summary>Maps a newly created business entity to its audience and notification policy.</summary>
    public async Task<IReadOnlyList<Notification>> ForCreatedAsync(object source, INotificationAudience audience, CancellationToken ct)
    {
        var events = new List<NotificationEvent>();
        switch (source)
        {
            case ChatMessage m:
                events.Add(new($"message:{m.Id:N}", await audience.OtherParticipantAsync(m.ConversationId, m.SenderUserId, ct),
                    m.SenderUserId, m.MessageType == "post" ? NotificationType.SharedPost : NotificationType.ChatMessage,
                    m.CreatedAtUtc, ConversationId: m.ConversationId, MessageId: m.Id));
                break;
            case UserFollow f:
                // Lifetime pair key prevents follow/unfollow notification spam.
                events.Add(new($"follow:{f.FollowerUserId:N}", f.FollowedUserId, f.FollowerUserId, NotificationType.NewFollower, f.CreatedAtUtc));
                break;
            case PostReaction r:
                events.Add(new($"reaction:{r.PostId:N}:{r.UserId:N}", await audience.PostAuthorAsync(r.PostId, ct), r.UserId,
                    NotificationType.PostReaction, r.CreatedAtUtc, PostId: r.PostId));
                break;
            case Comment c:
                var author = await audience.PostAuthorAsync(c.PostId, ct);
                var targets = new Dictionary<Guid, NotificationType> { [author] = NotificationType.PostComment };
                if (c.ParentCommentId is Guid parent)
                {
                    var parentAuthor = await audience.CommentAuthorAsync(c.PostId, parent, ct);
                    if (parentAuthor.HasValue) targets[parentAuthor.Value] = NotificationType.CommentReply;
                }
                var names = Mentions().Matches(c.Content).Select(m => m.Groups[1].Value.TrimEnd('.', '-'))
                    .Where(UserProfile.IsValidUserName).Select(UserProfile.NormalizeUserName).Distinct().Take(20).ToArray();
                if (names.Length > 0)
                    foreach (var user in await audience.MentionRecipientsAsync(c.PostId, names, ct))
                        targets[user] = NotificationType.CommentMention;
                foreach (var (user, type) in targets)
                    if (user == author || await audience.CanAccessPostAsync(c.PostId, user, ct))
                        events.Add(new($"comment:{c.Id:N}", user, c.AuthorUserId, type, c.CreatedAtUtc, PostId: c.PostId, CommentId: c.Id));
                break;
        }
        return events.Select(Create).OfType<Notification>().ToArray();
    }

    public Notification? Create(NotificationEvent e)
    {
        if (e.Recipient == e.Actor) return null;
        if (e.Recipient == Guid.Empty || e.Key.Length is < 1 or > 160 || !Enum.IsDefined(e.Type)
            || e.CreatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid notification event.", nameof(e));
        // Stable identity is also the client dedup key. Unique (recipient,event) is enforced in SQL.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{e.Recipient:N}:{e.Key}"));
        return new Notification
        {
            Id = new Guid(hash.AsSpan(0, 16)), RecipientUserId = e.Recipient, ActorUserId = e.Actor,
            Type = e.Type, EventKey = e.Key, PostId = e.PostId, CommentId = e.CommentId,
            ConversationId = e.ConversationId, MessageId = e.MessageId, Announcement = e.Announcement,
            Critical = e.Type == NotificationType.SystemAnnouncement && e.Critical,
            CreatedAtUtc = e.CreatedAtUtc, NextAttemptAtUtc = e.CreatedAtUtc,
        };
    }
}

/// <summary>Audience data access only; business event/priority policy stays in the application service.</summary>
public interface INotificationAudience
{
    Task<Guid> OtherParticipantAsync(Guid conversationId, Guid sender, CancellationToken ct);
    Task<Guid> PostAuthorAsync(Guid postId, CancellationToken ct);
    Task<Guid?> CommentAuthorAsync(Guid postId, Guid commentId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> MentionRecipientsAsync(Guid postId, IReadOnlyList<string> normalizedNames, CancellationToken ct);
    Task<bool> CanAccessPostAsync(Guid postId, Guid viewer, CancellationToken ct);
}
