namespace ImageStore.Domain.Chat;

public sealed class Conversation
{
    public Guid Id { get; set; }
    public string PairKey { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastMessageAtUtc { get; set; }
    // Also an optimistic concurrency token: serializes committed message order.
    public long LastSequence { get; set; }
    public List<ConversationParticipant> Participants { get; set; } = [];
}
public sealed class ConversationParticipant
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public long LastReadSequence { get; set; }
}
public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public long Sequence { get; set; }
    public string Content { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string MessageType { get; set; } = "text";
    public Guid? SharedPostId { get; set; }
}
