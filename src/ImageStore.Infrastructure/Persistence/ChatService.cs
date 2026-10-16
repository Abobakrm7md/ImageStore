using ImageStore.Application.Chat;
using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using ImageStore.Domain.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
namespace ImageStore.Infrastructure.Persistence;

internal sealed class ChatService(ImageStoreDbContext db, ICurrentUser current, IPostRepository posts, TimeProvider clock) : IChatService
{
    private Guid Me => current.GetRequiredUserId();
    public async Task<IReadOnlyList<Guid>> MembersAsync(Guid id, CancellationToken ct)
    {
        var ids = await db.Set<ConversationParticipant>().Where(x => x.ConversationId == id).Select(x => x.UserId).ToListAsync(ct);
        if (!ids.Contains(Me)) throw new NotFoundException("Conversation");
        return ids;
    }
    public async Task<ConversationDto> OpenAsync(Guid otherUserId, CancellationToken ct)
    {
        var me = Me;
        if (me == otherUserId) throw new RequestValidationException("otherUserId", "Choose another user.");
        if (!await db.UserProfiles.AnyAsync(x => x.UserId == otherUserId, ct)) throw new NotFoundException("User");
        var key = string.Join(":", new[] { me.ToString("N"), otherUserId.ToString("N") }.Order(StringComparer.Ordinal));
        var existing = await db.Set<Conversation>().SingleOrDefaultAsync(x => x.PairKey == key, ct);
        if (existing is not null) return await GetAsync(existing.Id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var conversation = new Conversation { Id = Guid.NewGuid(), PairKey = key, CreatedAtUtc = now, UpdatedAtUtc = now };
        conversation.Participants = new[] { me, otherUserId }.Select(u => new ConversationParticipant { ConversationId = conversation.Id, UserId = u, JoinedAtUtc = now }).ToList();
        db.Add(conversation);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            conversation = await db.Set<Conversation>().SingleAsync(x => x.PairKey == key, ct);
        }
        return await GetAsync(conversation.Id, ct);
    }
    public async Task<PagedResult<ConversationDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100) throw new RequestValidationException("page", "Invalid pagination.");
        var me = Me;
        var query = db.Set<Conversation>().Where(c => c.Participants.Any(p => p.UserId == me));
        var count = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(c => c.UpdatedAtUtc).ThenBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(c => c.Id).ToListAsync(ct);
        var result = new List<ConversationDto>();
        foreach (var id in ids) result.Add(await GetAsync(id, ct));
        return new(result, page, pageSize, count);
    }
    public async Task<ConversationDto> GetAsync(Guid id, CancellationToken ct)
    {
        await MembersAsync(id, ct);
        var people = await (from p in db.Set<ConversationParticipant>().AsNoTracking()
                            join u in db.UserProfiles on p.UserId equals u.UserId
                            where p.ConversationId == id
                            select new ChatPerson(u.UserId, u.UserName, u.DisplayName, u.ProfilePicturePath != null, p.LastReadSequence)).ToListAsync(ct);
        var cursor = people.Single(p => p.Id == Me).LastReadSequence;
        var latest = await db.Set<ChatMessage>().AsNoTracking().Where(m => m.ConversationId == id).OrderByDescending(m => m.Sequence).FirstOrDefaultAsync(ct);
        var unread = await db.Set<ChatMessage>().CountAsync(m => m.ConversationId == id && m.SenderUserId != Me && m.Sequence > cursor, ct);
        return new(id, people, latest is null ? null : await MapAsync(latest, ct), unread);
    }
    public async Task<IReadOnlyList<MessageDto>> HistoryAsync(Guid id, long? before, int limit, CancellationToken ct)
    {
        await MembersAsync(id, ct);
        if (limit is < 1 or > 100 || before is < 1) throw new RequestValidationException("limit", "Invalid cursor or limit.");
        var messages = await db.Set<ChatMessage>().AsNoTracking().Where(m => m.ConversationId == id && (!before.HasValue || m.Sequence < before.Value)).OrderByDescending(m => m.Sequence).Take(limit).ToListAsync(ct);
        var result = new List<MessageDto>();
        foreach (var message in messages) result.Add(await MapAsync(message, ct));
        return result;
    }
    public async Task<MessageDto> SendAsync(Guid id, SendMessageRequest request, CancellationToken ct)
    {
        var members = await MembersAsync(id, ct);
        var content = request.Content?.Trim() ?? "";
        if (request.Id == Guid.Empty || (request.Content?.Length ?? 0) > 4000 || (content.Length == 0 && request.SharedPostId is null))
            throw new RequestValidationException("content", "Use a nonempty retry ID and 1–4000 characters, or share a post.");
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var previous = await db.Set<ChatMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.Id, ct);
            if (previous is not null)
            {
                if (previous.ConversationId != id || previous.SenderUserId != Me || previous.Content != content || previous.SharedPostId != request.SharedPostId)
                    throw new ConflictException("The retry ID has already been used.");
                return await MapAsync(previous, ct);
            }
            if (request.SharedPostId is Guid post)
                foreach (var member in members)
                    if (await posts.GetAccessiblePostAsync(post, member, ct) is null) throw new NotFoundException("Post accessible to both participants");
            var conversation = await db.Set<Conversation>().SingleAsync(c => c.Id == id, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            conversation.LastSequence++;
            conversation.UpdatedAtUtc = now;
            conversation.LastMessageAtUtc = now;
            var message = new ChatMessage { Id = request.Id, ConversationId = id, SenderUserId = Me, Content = content,
                CreatedAtUtc = now, Sequence = conversation.LastSequence, MessageType = request.SharedPostId.HasValue ? "post" : "text", SharedPostId = request.SharedPostId };
            db.Add(message);
            try { await db.SaveChangesAsync(ct); return await MapAsync(message, ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { db.ChangeTracker.Clear(); }
        }
        throw new ConflictException("Conversation is busy. Retry with the same message ID.");
    }
    public async Task<ReadReceipt> ReadAsync(Guid id, long sequence, CancellationToken ct)
    {
        await MembersAsync(id, ct);
        if (sequence < 1 || !await db.Set<ChatMessage>().AnyAsync(m => m.ConversationId == id && m.Sequence == sequence, ct))
            throw new RequestValidationException("sequence", "Read cursor must reference an existing message.");
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var participant = await db.Set<ConversationParticipant>().SingleAsync(p => p.ConversationId == id && p.UserId == Me, ct);
            participant.LastReadSequence = Math.Max(sequence, participant.LastReadSequence);
            try { await db.SaveChangesAsync(ct); return new(id, Me, participant.LastReadSequence); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        }
        throw new ConflictException("Read cursor is busy. Retry.");
    }
    private async Task<MessageDto> MapAsync(ChatMessage m, CancellationToken ct)
    {
        // Never disclose post metadata or a live link after access is revoked/deletion.
        Guid? post = m.SharedPostId;
        if (post is Guid id && await posts.GetAccessiblePostAsync(id, Me, ct) is null) post = null;
        return new(m.Id, m.ConversationId, m.SenderUserId, m.Sequence, m.Content, m.CreatedAtUtc, m.MessageType, post);
    }
}
