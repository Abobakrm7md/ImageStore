using System.Security.Claims;
using System.Threading.RateLimiting;
using ImageStore.Application.Chat;
using ImageStore.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace ImageStore.Api.Chat;

public sealed class ChatLimits : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    public void Check(string user)
    {
        using var lease = limiter.AttemptAcquire(user);
        if (!lease.IsAcquired) throw new BadHttpRequestException("Too many chat operations. Try again shortly.", 429);
    }
    public void Dispose() => limiter.Dispose();
}

// Events contain only invalidations. Each recipient retrieves its own authorized DTO, including
// current post visibility. No post data from the sender's view is broadcast to another viewer.
public sealed class ChatEvents(IHubContext<ChatHub> hub, IChatService chat)
{
    public async Task Changed(Guid id, CancellationToken ct)
    {
        var members = await chat.MembersAsync(id, ct);
        await hub.Clients.Users(members.Select(x => x.ToString()).ToList()).SendAsync("ConversationChanged", id, ct);
    }
    public async Task Read(ReadReceipt receipt, CancellationToken ct)
    {
        var members = await chat.MembersAsync(receipt.ConversationId, ct);
        await hub.Clients.Users(members.Select(x => x.ToString()).ToList()).SendAsync("ReadUpdated", receipt, ct);
        await hub.Clients.User(receipt.UserId.ToString()).SendAsync("NotificationChanged", (Guid?)null, ct);
    }
}

[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
public sealed class ChatHub(IChatService chat, ChatEvents events, ChatLimits limits) : Hub
{
    private void Limit() => limits.Check(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task Join(Guid conversationId)
    {
        Limit(); await chat.MembersAsync(conversationId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId.ToString());
    }
    public Task Leave(Guid conversationId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId.ToString());
    public async Task<MessageDto> SendMessage(Guid conversationId, SendMessageRequest request)
    {
        Limit(); var message = await chat.SendAsync(conversationId, request, Context.ConnectionAborted);
        await events.Changed(conversationId, Context.ConnectionAborted); return message;
    }
    public async Task MarkRead(Guid conversationId, long sequence)
    {
        Limit(); var receipt = await chat.ReadAsync(conversationId, sequence, Context.ConnectionAborted);
        await events.Read(receipt, Context.ConnectionAborted);
    }
    public async Task Typing(Guid conversationId, bool typing)
    {
        Limit(); await chat.MembersAsync(conversationId, Context.ConnectionAborted);
        await Clients.OthersInGroup(conversationId.ToString()).SendAsync("Typing", conversationId, Context.UserIdentifier, typing);
    }
}
