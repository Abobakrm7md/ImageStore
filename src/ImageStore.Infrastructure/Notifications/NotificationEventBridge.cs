using ImageStore.Application.Notifications;
using ImageStore.Domain.Chat;
using ImageStore.Domain.Notifications;
using ImageStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ImageStore.Infrastructure.Notifications;

/// <summary>
/// Transaction adapter: application policy plans notifications from added domain entities,
/// then this adapter stages them in the original EF unit of work. No after-save delivery gap.
/// </summary>
internal static class NotificationEventBridge
{
    internal static async Task StageAsync(ImageStoreDbContext db, CancellationToken ct)
    {
        var added = db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToArray();
        var creator = new NotificationCreationService();
        var audience = new Audience(db);
        foreach (var row in added)
        {
            foreach (var notification in await creator.ForCreatedAsync(row, audience, ct))
            {
                if (db.Set<Notification>().Local.Any(n => n.Id == notification.Id)
                    || await db.Set<Notification>().AnyAsync(n => n.Id == notification.Id, ct)) continue;
                db.Add(notification);
            }
        }
        foreach (var entry in db.ChangeTracker.Entries<ConversationParticipant>()
                     .Where(e => e.State == EntityState.Modified && e.Property(p => p.LastReadSequence).IsModified).ToArray())
        {
            var p = entry.Entity;
            var read = await db.Set<Notification>().Where(n => n.RecipientUserId == p.UserId
                && n.ConversationId == p.ConversationId && n.ReadAtUtc == null
                && db.Set<ChatMessage>().Any(m => m.Id == n.MessageId && m.Sequence <= p.LastReadSequence)).ToListAsync(ct);
            foreach (var n in read) n.ReadAtUtc = DateTime.UtcNow;
        }
    }

    private sealed class Audience(ImageStoreDbContext db) : INotificationAudience
    {
        public async Task<Guid> OtherParticipantAsync(Guid conversationId, Guid sender, CancellationToken ct) =>
            db.Set<ConversationParticipant>().Local.FirstOrDefault(p => p.ConversationId == conversationId && p.UserId != sender)?.UserId
                ?? await db.Set<ConversationParticipant>().Where(p => p.ConversationId == conversationId && p.UserId != sender).Select(p => p.UserId).SingleAsync(ct);

        public async Task<Guid> PostAuthorAsync(Guid postId, CancellationToken ct) =>
            db.ImagePosts.Local.FirstOrDefault(p => p.Id == postId)?.AuthorUserId
                ?? await db.ImagePosts.Where(p => p.Id == postId).Select(p => p.AuthorUserId).SingleAsync(ct);

        public Task<Guid?> CommentAuthorAsync(Guid postId, Guid commentId, CancellationToken ct) =>
            db.Comments.Where(c => c.Id == commentId && c.PostId == postId).Select(c => (Guid?)c.AuthorUserId).SingleOrDefaultAsync(ct);

        public Task<bool> CanAccessPostAsync(Guid postId, Guid viewer, CancellationToken ct) =>
            new PostRepository(db).BuildAccessibleQuery(viewer).AnyAsync(p => p.Id == postId, ct);

        public async Task<IReadOnlyList<Guid>> MentionRecipientsAsync(Guid postId, IReadOnlyList<string> normalizedNames, CancellationToken ct)
        {
            var author = await PostAuthorAsync(postId, ct);
            return await db.UserProfiles.Where(u => normalizedNames.Contains(u.NormalizedUserName)
                && (u.UserId == author || db.UserFollows.Any(f => f.FollowerUserId == u.UserId && f.FollowedUserId == author)))
                .Select(u => u.UserId).ToListAsync(ct);
        }
    }
}
