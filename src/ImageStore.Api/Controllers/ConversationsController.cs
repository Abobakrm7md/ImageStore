using ImageStore.Api.Chat;
using ImageStore.Api.Security;
using ImageStore.Application.Chat;
using ImageStore.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace ImageStore.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/conversations")]
[RequestSizeLimit(24 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ConversationsController(IChatService chat, ChatEvents events, ChatLimits limits, ICurrentUser current) : ControllerBase
{
    public sealed record OpenRequest(Guid OtherUserId);
    public sealed record ReadRequest(long Sequence);
    [HttpGet]
    public Task<PagedResult<ConversationDto>> List(CancellationToken ct, int page = 1, int pageSize = 30) => chat.ListAsync(page, pageSize, ct);
    [HttpPost]
    public async Task<ConversationDto> Open(OpenRequest request, CancellationToken ct)
    {
        limits.Check(current.GetRequiredUserId().ToString());
        var result = await chat.OpenAsync(request.OtherUserId, ct);
        await events.Changed(result.Id, ct); return result;
    }
    [HttpGet("{id:guid}")]
    public Task<ConversationDto> Get(Guid id, CancellationToken ct) => chat.GetAsync(id, ct);
    [HttpGet("{id:guid}/messages")]
    public Task<IReadOnlyList<MessageDto>> History(Guid id, CancellationToken ct, long? before = null, int limit = 50) => chat.HistoryAsync(id, before, limit, ct);
    [HttpPost("{id:guid}/messages")]
    public async Task<MessageDto> Send(Guid id, SendMessageRequest request, CancellationToken ct)
    {
        limits.Check(current.GetRequiredUserId().ToString());
        var result = await chat.SendAsync(id, request, ct);
        await events.Changed(id, ct); return result;
    }
    [HttpPut("{id:guid}/read")]
    public async Task<ReadReceipt> Read(Guid id, ReadRequest request, CancellationToken ct)
    {
        limits.Check(current.GetRequiredUserId().ToString());
        var result = await chat.ReadAsync(id, request.Sequence, ct);
        await events.Read(result, ct); return result;
    }
}
