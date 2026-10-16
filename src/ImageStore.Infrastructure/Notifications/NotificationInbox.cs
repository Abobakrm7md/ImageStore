using ImageStore.Application.Common;
using ImageStore.Application.Notifications;
using ImageStore.Domain.Chat;
using ImageStore.Domain.Notifications;
using ImageStore.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace ImageStore.Infrastructure.Notifications;

internal sealed class NotificationInbox(ImageStoreDbContext db, ICurrentUser current, TimeProvider clock) : INotificationInbox
{
    private Guid Me => current.GetRequiredUserId();
    private IQueryable<Notification> Owned => db.Set<Notification>().Where(n => n.RecipientUserId == Me && n.DismissedAtUtc == null);

    private async Task<IQueryable<Notification>> VisibleAsync(CancellationToken ct)
    {
        var p = await PreferencesAsync(ct);
        var types = new List<NotificationType>();
        if (p.InAppEnabled)
        {
            if (p.ChatEnabled) types.AddRange([NotificationType.ChatMessage, NotificationType.SharedPost]);
            if (p.FollowEnabled) types.Add(NotificationType.NewFollower);
            if (p.CommentsEnabled) types.AddRange([NotificationType.PostComment, NotificationType.CommentReply]);
            if (p.ReactionsEnabled) types.Add(NotificationType.PostReaction);
            if (p.MentionsEnabled) types.Add(NotificationType.CommentMention);
            if (p.AnnouncementsEnabled) types.Add(NotificationType.SystemAnnouncement);
        }
        return Owned.Where(n => n.Critical || types.Contains(n.Type));
    }
    public async Task<PagedResult<NotificationDto>> ListAsync(int page, int pageSize, bool? unread, NotificationType? type, CancellationToken ct)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100 || (type.HasValue && !Enum.IsDefined(type.Value)))
            throw new RequestValidationException("page", "Invalid notification filters or pagination.");
        var query = await VisibleAsync(ct);
        if (unread.HasValue) query = unread.Value ? query.Where(n => n.ReadAtUtc == null) : query.Where(n => n.ReadAtUtc != null);
        if (type.HasValue) query = query.Where(n => n.Type == type.Value);
        var total = await query.CountAsync(ct);
        var rows = await query.AsNoTracking().OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(await ProjectManyAsync(rows, ct), page, pageSize, total);
    }
    public async Task<NotificationDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Owned.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, ct) ?? throw new NotFoundException("Notification");
        return (await ProjectManyAsync([row], ct))[0];
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct) => await (await VisibleAsync(ct)).CountAsync(n => n.ReadAtUtc == null, ct);

    public async Task MarkReadAsync(Guid? id, CancellationToken ct)
    {
        if (id.HasValue && !await Owned.AnyAsync(n => n.Id == id.Value, ct)) throw new NotFoundException("Notification");
        var now = clock.GetUtcNow().UtcDateTime;
        var query = id.HasValue ? Owned.Where(n => n.Id == id.Value) : (await VisibleAsync(ct)).Where(n => n.CreatedAtUtc <= now);
        query = query.Where(n => n.ReadAtUtc == null);
        if (db.Database.IsRelational()) await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), ct);
        else
        {
            foreach (var n in await query.ToListAsync(ct)) n.ReadAtUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }
    public async Task DeleteAsync(Guid? id, CancellationToken ct)
    {
        if (id.HasValue && !await Owned.AnyAsync(n => n.Id == id.Value, ct)) throw new NotFoundException("Notification");
        var query = id.HasValue ? Owned.Where(n => n.Id == id.Value) : Owned.Where(n => n.ReadAtUtc != null);
        var now = clock.GetUtcNow().UtcDateTime;
        // Keep a tombstone for event idempotency, rather than allowing old events to resurrect.
        if (db.Database.IsRelational()) await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.DismissedAtUtc, now), ct);
        else
        {
            foreach (var n in await query.ToListAsync(ct)) n.DismissedAtUtc = now;
            await db.SaveChangesAsync(ct);
        }
    }
    public async Task<NotificationPreferencesDto> PreferencesAsync(CancellationToken ct)
    {
        var p = await db.Set<NotificationPreferences>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == Me, ct);
        return p is null ? new() : new(p.InAppEnabled, p.ChatEnabled, p.FollowEnabled, p.CommentsEnabled, p.ReactionsEnabled,
            p.MentionsEnabled, p.AnnouncementsEnabled, p.SoundEnabled, p.SoundVolume, p.SuppressActiveConversation);
    }
    public async Task<NotificationPreferencesDto> SavePreferencesAsync(NotificationPreferencesDto r, CancellationToken ct)
    {
        if (r.SoundVolume is < 0 or > 100 || r.BrowserPushEnabled)
            throw new RequestValidationException("preferences", "Volume must be 0–100. Web Push is not available in this release.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var p = await db.Set<NotificationPreferences>().SingleOrDefaultAsync(x => x.UserId == Me, ct);
            if (p is null) { p = new() { UserId = Me }; db.Add(p); }
            p.InAppEnabled = r.InAppEnabled; p.ChatEnabled = r.ChatEnabled; p.FollowEnabled = r.FollowEnabled;
            p.CommentsEnabled = r.CommentsEnabled; p.ReactionsEnabled = r.ReactionsEnabled; p.MentionsEnabled = r.MentionsEnabled;
            p.AnnouncementsEnabled = r.AnnouncementsEnabled; p.SoundEnabled = r.SoundEnabled;
            p.SoundVolume = r.SoundVolume; p.SuppressActiveConversation = r.SuppressActiveConversation;
            try { await db.SaveChangesAsync(ct); return r; }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { db.ChangeTracker.Clear(); }
        }
        throw new ConflictException("Preferences changed concurrently. Retry.");
    }
    public async Task AnnounceAsync(AnnouncementRequest request, CancellationToken ct)
    {
        if (request.EventId == Guid.Empty || string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 240
            || request.RecipientUserIds is null || request.RecipientUserIds.Count is < 1 or > 100)
            throw new RequestValidationException("announcement", "Provide an event UUID, 1–100 recipients and 1–240 characters.");
        var ids = request.RecipientUserIds.Distinct().ToArray();
        if (await db.Users.CountAsync(u => ids.Contains(u.Id), ct) != ids.Length) throw new NotFoundException("Recipient");
        var creator = new NotificationCreationService();
        foreach (var recipient in ids)
        {
            var n = creator.Create(new($"announcement:{request.EventId:N}", recipient, null, NotificationType.SystemAnnouncement,
                clock.GetUtcNow().UtcDateTime, Announcement: request.Text.Trim(), Critical: request.Critical))!;
            if (!await db.Set<Notification>().AnyAsync(x => x.Id == n.Id, ct)) db.Add(n);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConflictException("Announcement was concurrently processed. Retry with the same event ID.", ex); }
    }
    private async Task<IReadOnlyList<NotificationDto>> ProjectManyAsync(IReadOnlyList<Notification> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var actors = rows.Where(n => n.ActorUserId.HasValue).Select(n => n.ActorUserId!.Value).Distinct().ToArray();
        var names = await db.UserProfiles.Where(u => actors.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.DisplayName, ct);
        var postIds = rows.Where(n => n.PostId.HasValue).Select(n => n.PostId!.Value).Distinct().ToArray();
        var visiblePosts = (await new PostRepository(db).BuildAccessibleQuery(Me).Where(p => postIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var commentIds = rows.Where(n => n.CommentId.HasValue).Select(n => n.CommentId!.Value).Distinct().ToArray();
        var comments = await db.Comments.Where(c => commentIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.PostId, ct);
        var conversationIds = rows.Where(n => n.ConversationId.HasValue).Select(n => n.ConversationId!.Value).Distinct().ToArray();
        var conversations = (await db.Set<ConversationParticipant>().Where(p => p.UserId == Me && conversationIds.Contains(p.ConversationId)).Select(p => p.ConversationId).ToListAsync(ct)).ToHashSet();
        var messageIds = rows.Where(n => n.MessageId.HasValue).Select(n => n.MessageId!.Value).Distinct().ToArray();
        var messages = await db.Set<ChatMessage>().Where(m => messageIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.ConversationId, ct);
        return rows.Select(n =>
        {
            var available = n.Type == NotificationType.SystemAnnouncement
                || (n.Type == NotificationType.NewFollower && n.ActorUserId.HasValue && names.ContainsKey(n.ActorUserId.Value))
                || (n.PostId.HasValue && visiblePosts.Contains(n.PostId.Value)
                    && (!n.CommentId.HasValue || comments.GetValueOrDefault(n.CommentId.Value) == n.PostId.Value))
                || (n.ConversationId.HasValue && conversations.Contains(n.ConversationId.Value)
                    && n.MessageId.HasValue && messages.GetValueOrDefault(n.MessageId.Value) == n.ConversationId.Value);
            var actor = available && n.ActorUserId.HasValue ? names.GetValueOrDefault(n.ActorUserId.Value) : null;
            var text = !available ? "This activity is no longer available." : n.Type switch
            {
                NotificationType.ChatMessage => "Sent you a private message.",
                NotificationType.SharedPost => "Shared an image post with you.",
                NotificationType.NewFollower => "Started following you.",
                NotificationType.PostComment => "Commented on your post.",
                NotificationType.PostReaction => "Reacted to your post.",
                NotificationType.CommentReply => "Replied to your comment.",
                NotificationType.CommentMention => "Mentioned you in a comment.",
                _ => n.Announcement ?? "Important system announcement.",
            };
            return new NotificationDto(n.Id, n.Type, text, actor, n.CreatedAtUtc, n.ReadAtUtc, n.Critical, available,
                available ? n.ActorUserId : null, available ? n.PostId : null, available ? n.CommentId : null,
                available ? n.ConversationId : null, available ? n.MessageId : null);
        }).ToList();
    }
}
