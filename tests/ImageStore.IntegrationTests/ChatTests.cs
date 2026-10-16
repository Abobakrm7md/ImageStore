using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using ImageStore.Application.Chat;
using ImageStore.Application.Common;
using ImageStore.Domain.Chat;
using ImageStore.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
namespace ImageStore.IntegrationTests;

public sealed class ChatTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    internal static async Task<ConversationDto> Open(ApiClient sender, Guid recipient)
    {
        var response = await sender.Http.PostAsJsonAsync("/api/conversations", new { otherUserId = recipient });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConversationDto>(ApiClient.Json))!;
    }
    [Fact]
    public async Task Pair_Is_Reused_And_Lists_Are_Private()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        using var eve = await ApiClient.CreateAuthenticatedAsync(factory);
        var c = await Open(alice, bob.User!.Id);
        Assert.Equal(c.Id, (await Open(bob, alice.User!.Id)).Id);
        Assert.Equal(2, c.Participants.Count);
        var list = await eve.Http.GetFromJsonAsync<PagedResult<ConversationDto>>("/api/conversations");
        Assert.Empty(list!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await eve.Http.GetAsync($"/api/conversations/{c.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await eve.Http.GetAsync($"/api/conversations/{c.Id}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await eve.Http.PostAsJsonAsync($"/api/conversations/{c.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "intrusion"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await eve.Http.PutAsJsonAsync($"/api/conversations/{c.Id}/read", new { sequence = 1 })).StatusCode);
    }
    [Fact]
    public async Task Messages_Are_Durable_Idempotent_Paged_And_Read_Cursor_Is_Monotonic()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var c = await Open(alice, bob.User!.Id);
        var path = $"/api/conversations/{c.Id}";
        var request = new SendMessageRequest(Guid.NewGuid(), "first");
        var first = await alice.Http.PostAsJsonAsync(path + "/messages", request);
        first.EnsureSuccessStatusCode();
        var retry = await alice.Http.PostAsJsonAsync(path + "/messages", request);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await alice.Http.PostAsJsonAsync(path + "/messages", request with { Content = "changed" })).StatusCode);
        (await alice.Http.PostAsJsonAsync(path + "/messages", new SendMessageRequest(Guid.NewGuid(), "second"))).EnsureSuccessStatusCode();
        var page = await bob.Http.GetFromJsonAsync<MessageDto[]>(path + "/messages?limit=1");
        Assert.Equal(2, Assert.Single(page!).Sequence);
        var older = await bob.Http.GetFromJsonAsync<MessageDto[]>(path + "/messages?before=2&limit=1");
        Assert.Equal(request.Id, Assert.Single(older!).Id);
        var summary = await bob.Http.GetFromJsonAsync<ConversationDto>(path);
        Assert.Equal(2, summary!.UnreadCount);
        Assert.Equal(0, (await alice.Http.GetFromJsonAsync<ConversationDto>(path))!.UnreadCount);
        (await bob.Http.PutAsJsonAsync(path + "/read", new { sequence = 2 })).EnsureSuccessStatusCode();
        (await bob.Http.PutAsJsonAsync(path + "/read", new { sequence = 1 })).EnsureSuccessStatusCode();
        summary = await alice.Http.GetFromJsonAsync<ConversationDto>(path);
        Assert.Equal(2, summary!.Participants.Single(p => p.Id == bob.User.Id).LastReadSequence);
        Assert.Equal(0, (await bob.Http.GetFromJsonAsync<ConversationDto>(path))!.UnreadCount);
        Assert.Equal(2, await factory.WithDbContextAsync(db => db.Set<ChatMessage>().CountAsync(m => m.ConversationId == c.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.Http.PutAsJsonAsync(path + "/read", new { sequence = 999 })).StatusCode);
    }
    [Theory]
    [InlineData(0)] [InlineData(4001)]
    public async Task Invalid_Content_Is_Rejected(int length)
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var c = await Open(alice, bob.User!.Id);
        var content = length == 0 ? " \n " : new string('x', length);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Http.PostAsJsonAsync($"/api/conversations/{c.Id}/messages", new SendMessageRequest(Guid.NewGuid(), content))).StatusCode);
    }
    [Fact]
    public async Task Sharing_Requires_Both_Viewers_And_Revoked_Access_Redacts_Link()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await alice.PostImageAsync();
        var c = await Open(alice, bob.User!.Id);
        var path = $"/api/conversations/{c.Id}/messages";
        var request = new SendMessageRequest(Guid.NewGuid(), "", post.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.Http.PostAsJsonAsync(path, request)).StatusCode);
        (await bob.FollowAsync(alice.User!.Id)).EnsureSuccessStatusCode();
        (await alice.Http.PostAsJsonAsync(path, request)).EnsureSuccessStatusCode();
        Assert.Equal(post.Id, Assert.Single((await bob.Http.GetFromJsonAsync<MessageDto[]>(path))!).SharedPostId);
        (await bob.Http.DeleteAsync($"/api/users/{alice.User.Id}/follow")).EnsureSuccessStatusCode();
        Assert.Null(Assert.Single((await bob.Http.GetFromJsonAsync<MessageDto[]>(path))!).SharedPostId);
    }
    [Fact]
    public async Task SignalR_Rejects_Nonparticipant_Group_Membership()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        using var eve = await ApiClient.CreateAuthenticatedAsync(factory);
        var c = await Open(alice, bob.User!.Id);
        var ws = factory.Server.CreateWebSocketClient();
        ws.ConfigureRequest = request => request.Headers.Cookie = eve.Cookies.GetCookieHeader(new Uri("https://localhost"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await ws.ConnectAsync(new Uri("wss://localhost/api/chat"), timeout.Token);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[8192];
        await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        var invocation = $"{{\"type\":1,\"invocationId\":\"1\",\"target\":\"Join\",\"arguments\":[\"{c.Id}\"]}}\u001e";
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(invocation)), WebSocketMessageType.Text, true, timeout.Token);
        string result;
        do
        {
            var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            result = Encoding.UTF8.GetString(buffer, 0, received.Count);
        } while (!result.Contains("invocationId", StringComparison.Ordinal));
        Assert.Contains("\"error\"", result, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Chat_Negotiation_Rejects_CrossOrigin_And_Missing_Csrf()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        alice.Http.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.Http.PostAsync("/api/chat/negotiate?negotiateVersion=1", null)).StatusCode);
        alice.Http.DefaultRequestHeaders.Remove("Origin");
        alice.SendXsrfHeader = false;
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Http.PostAsync("/api/chat/negotiate?negotiateVersion=1", null)).StatusCode);
    }
}
