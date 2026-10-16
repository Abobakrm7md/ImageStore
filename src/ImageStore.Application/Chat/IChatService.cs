using ImageStore.Application.Common;
namespace ImageStore.Application.Chat;

public sealed record SendMessageRequest(Guid Id, string? Content, Guid? SharedPostId = null);
public sealed record ChatPerson(Guid Id, string UserName, string DisplayName, bool HasProfilePicture, long LastReadSequence);
public sealed record MessageDto(Guid Id, Guid ConversationId, Guid SenderUserId, long Sequence, string Content,
    DateTime CreatedAtUtc, string MessageType, Guid? SharedPostId);
public sealed record ConversationDto(Guid Id, IReadOnlyList<ChatPerson> Participants, MessageDto? LatestMessage, int UnreadCount);
public sealed record ReadReceipt(Guid ConversationId, Guid UserId, long Sequence);

// Implemented by persistence; all entry points derive identity through ICurrentUser.
public interface IChatService
{
    Task<ConversationDto> OpenAsync(Guid otherUserId, CancellationToken ct);
    Task<PagedResult<ConversationDto>> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<ConversationDto> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MessageDto>> HistoryAsync(Guid id, long? before, int limit, CancellationToken ct);
    Task<MessageDto> SendAsync(Guid id, SendMessageRequest request, CancellationToken ct);
    Task<ReadReceipt> ReadAsync(Guid id, long sequence, CancellationToken ct);
    Task<IReadOnlyList<Guid>> MembersAsync(Guid id, CancellationToken ct);
}
