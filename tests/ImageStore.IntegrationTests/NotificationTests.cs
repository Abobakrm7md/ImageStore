using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Chat;
using ImageStore.Application.Common;
using ImageStore.Application.Notifications;
using ImageStore.Application.Posts;
using ImageStore.Domain.Chat;
using ImageStore.Domain.Notifications;
using ImageStore.Infrastructure.Identity;
using ImageStore.Infrastructure.Notifications;
using ImageStore.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace ImageStore.IntegrationTests;

public sealed class NotificationTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    private static async Task<PagedResult<NotificationDto>> List(ApiClient user, string query = "") =>
        (await user.Http.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/notifications" + query, ApiClient.Json))!;
    private sealed record CountResult(int Count);
    private static async Task<int> Count(ApiClient user) => (await user.Http.GetFromJsonAsync<CountResult>("/api/notifications/unread-count"))!.Count;
    private static async Task<CommentDto> Comment(ApiClient user, Guid post, string content, Guid? parent = null)
    {
        var response = await user.Http.PostAsJsonAsync($"/api/posts/{post}/comments", new CreateCommentRequest(content, parent), ApiClient.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommentDto>(ApiClient.Json))!;
    }
    [Fact]
    public async Task Follows_Are_Persistent_Deduplicated_And_Private_Even_After_Dismissal()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        (await bob.FollowAsync(alice.User!.Id)).EnsureSuccessStatusCode();
        (await bob.FollowAsync(alice.User.Id)).EnsureSuccessStatusCode();
        var n = Assert.Single((await List(alice)).Items);
        Assert.Equal(NotificationType.NewFollower, n.Type);
        Assert.Equal(bob.User!.Id, n.ActorUserId);
        Assert.Equal(1, await Count(alice));
        Assert.Empty((await List(bob)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/api/notifications/{n.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.PatchAsJsonAsync($"/api/notifications/{n.Id}/read", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.DeleteAsync($"/api/notifications/{n.Id}")).StatusCode);
        (await alice.Http.DeleteAsync($"/api/notifications/{n.Id}")).EnsureSuccessStatusCode();
        (await bob.Http.DeleteAsync($"/api/users/{alice.User.Id}/follow")).EnsureSuccessStatusCode();
        (await bob.FollowAsync(alice.User.Id)).EnsureSuccessStatusCode();
        Assert.Empty((await List(alice)).Items);
        Assert.Equal(1, await factory.WithDbContextAsync(db => db.Set<Notification>().CountAsync(x => x.RecipientUserId == alice.User.Id)));
    }
    [Fact]
    public async Task Comments_And_Reactions_Paginate_Filter_And_Suppress_Self_And_Reaction_Churn()
    {
        using var owner = await ApiClient.CreateAuthenticatedAsync(factory);
        using var follower = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await owner.PostImageAsync();
        await Comment(owner, post.Id, "My own comment");
        (await owner.Http.PutAsJsonAsync($"/api/posts/{post.Id}/my-reaction", new SetReactionRequest("like"))).EnsureSuccessStatusCode();
        Assert.Empty((await List(owner)).Items);
        (await follower.FollowAsync(owner.User!.Id)).EnsureSuccessStatusCode();
        await Comment(follower, post.Id, "First"); await Comment(follower, post.Id, "Second");
        foreach (var reaction in new[] { "like", "like", "love" })
            (await follower.Http.PutAsJsonAsync($"/api/posts/{post.Id}/my-reaction", new SetReactionRequest(reaction))).EnsureSuccessStatusCode();
        (await follower.Http.DeleteAsync($"/api/posts/{post.Id}/my-reaction")).EnsureSuccessStatusCode();
        (await follower.Http.PutAsJsonAsync($"/api/posts/{post.Id}/my-reaction", new SetReactionRequest("wow"))).EnsureSuccessStatusCode();
        Assert.Single((await List(owner, "?type=postReaction")).Items);
        var page1 = await List(owner, "?type=postComment&pageSize=1&page=1");
        var page2 = await List(owner, "?type=postComment&pageSize=1&page=2");
        Assert.Equal(2, page1.TotalCount); Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);
        var n = page1.Items[0];
        (await owner.Http.PatchAsJsonAsync($"/api/notifications/{n.Id}/read", new { })).EnsureSuccessStatusCode();
        Assert.Single((await List(owner, "?type=postComment&unread=true")).Items);
        (await owner.Http.PatchAsJsonAsync("/api/notifications/read-all", new { })).EnsureSuccessStatusCode();
        Assert.Equal(0, await Count(owner));
        (await owner.Http.DeleteAsync("/api/notifications/read")).EnsureSuccessStatusCode();
        Assert.Empty((await List(owner)).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.GetAsync("/api/notifications?pageSize=101")).StatusCode);
    }
    [Fact]
    public async Task Replies_And_Mentions_Require_Post_Access_And_Collapse_One_Event_Per_Recipient()
    {
        using var owner = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        using var carol = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await owner.PostImageAsync();
        (await bob.FollowAsync(owner.User!.Id)).EnsureSuccessStatusCode();
        var parent = await Comment(bob, post.Id, "Parent");
        var names = await factory.WithDbContextAsync(db => db.UserProfiles.Where(p => p.UserId == bob.User!.Id || p.UserId == carol.User!.Id).ToDictionaryAsync(p => p.UserId, p => p.UserName));
        var reply = await Comment(owner, post.Id, $"@{names[bob.User!.Id]} @{names[bob.User.Id]} @{names[carol.User!.Id]} Hello", parent.Id);
        Assert.Equal(parent.Id, reply.ParentCommentId);
        var n = Assert.Single((await List(bob)).Items);
        Assert.Equal(NotificationType.CommentMention, n.Type); // Mention wins over reply, one event.
        Assert.Empty((await List(carol)).Items); // Mention does not confer post access.
        await Comment(owner, post.Id, "A plain reply", parent.Id);
        Assert.Single((await List(bob, "?type=commentReply")).Items);
        (await bob.Http.DeleteAsync($"/api/users/{owner.User.Id}/follow")).EnsureSuccessStatusCode();
        var unavailable = await bob.Http.GetFromJsonAsync<NotificationDto>($"/api/notifications/{n.Id}", ApiClient.Json);
        Assert.False(unavailable!.Available); Assert.Null(unavailable.PostId); Assert.Null(unavailable.ActorUserId);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/api/posts/{post.Id}/comments/{reply.Id}")).StatusCode);
    }
    [Fact]
    public async Task Deleted_Content_Is_Redacted()
    {
        using var owner = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await owner.PostImageAsync();
        (await bob.FollowAsync(owner.User!.Id)).EnsureSuccessStatusCode();
        await Comment(bob, post.Id, "Sensitive comment");
        var n = Assert.Single((await List(owner, "?type=postComment")).Items);
        (await owner.Http.DeleteAsync($"/api/posts/{post.Id}")).EnsureSuccessStatusCode();
        var item = await owner.Http.GetFromJsonAsync<NotificationDto>($"/api/notifications/{n.Id}", ApiClient.Json);
        Assert.False(item!.Available); Assert.Null(item.CommentId); Assert.Null(item.PostId);
        Assert.DoesNotContain("Sensitive", item.Text, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Chat_Retries_Create_One_Notice_And_Chat_Read_Marks_It_Read()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var conversation = await ChatTests.Open(alice, bob.User!.Id);
        var request = new SendMessageRequest(Guid.NewGuid(), "Private secret never copied");
        for (var i = 0; i < 2; i++)
            (await alice.Http.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", request)).EnsureSuccessStatusCode();
        var n = Assert.Single((await List(bob)).Items);
        Assert.Equal(NotificationType.ChatMessage, n.Type); Assert.Equal(request.Id, n.MessageId);
        Assert.DoesNotContain("Private secret", n.Text, StringComparison.Ordinal);
        Assert.Empty((await List(alice)).Items);
        (await bob.Http.PutAsJsonAsync($"/api/conversations/{conversation.Id}/read", new { sequence = 1 })).EnsureSuccessStatusCode();
        Assert.Equal(0, await Count(bob));
        var post = await alice.PostImageAsync();
        (await bob.FollowAsync(alice.User!.Id)).EnsureSuccessStatusCode();
        (await alice.Http.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "", post.Id))).EnsureSuccessStatusCode();
        Assert.Single((await List(bob, "?type=sharedPost")).Items);
    }
    [Fact]
    public async Task Preferences_Control_Presentation_Not_Storage_And_Critical_Notices_Bypass_Them()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var prefs = new NotificationPreferencesDto(InAppEnabled: false, SoundEnabled: true, SoundVolume: 12);
        (await alice.Http.PutAsJsonAsync("/api/notifications/preferences", prefs)).EnsureSuccessStatusCode();
        (await bob.FollowAsync(alice.User!.Id)).EnsureSuccessStatusCode();
        Assert.Equal(0, await Count(alice)); Assert.Empty((await List(alice)).Items);
        Assert.Equal(prefs, await alice.Http.GetFromJsonAsync<NotificationPreferencesDto>("/api/notifications/preferences"));
        Assert.Equal(1, await factory.WithDbContextAsync(db => db.Set<Notification>().CountAsync(n => n.RecipientUserId == alice.User.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Http.PutAsJsonAsync("/api/notifications/preferences", prefs with { SoundVolume = 101 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Http.PutAsJsonAsync("/api/notifications/preferences", prefs with { BrowserPushEnabled = true })).StatusCode);
        var announcement = new AnnouncementRequest(Guid.NewGuid(), [alice.User.Id], "Security update", true);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.Http.PostAsJsonAsync("/api/notifications/announcements", announcement)).StatusCode);
        await MakeAdministrator(bob);
        (await bob.Http.PostAsJsonAsync("/api/notifications/announcements", announcement)).EnsureSuccessStatusCode();
        (await bob.Http.PostAsJsonAsync("/api/notifications/announcements", announcement)).EnsureSuccessStatusCode();
        Assert.True(Assert.Single((await List(alice)).Items).Critical);
        Assert.Equal(1, await Count(alice));
        (await alice.Http.PutAsJsonAsync("/api/notifications/preferences", prefs with { InAppEnabled = true })).EnsureSuccessStatusCode();
        Assert.Equal(2, await Count(alice));
    }
    private async Task MakeAdministrator(ApiClient client)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync("Administrator")) Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Administrator"))).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(client.User!.Id.ToString());
        Assert.True((await users.AddToRoleAsync(user!, "Administrator")).Succeeded);
        (await client.Http.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        (await client.Http.PostAsJsonAsync("/api/auth/login", new { email = client.User.Email, password = ApiClient.DefaultPassword })).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Business_Action_Is_Not_Saved_If_Notification_Write_Fails()
    {
        using var isolated = new ImageStoreApiFactory();
        using var alice = await ApiClient.CreateAuthenticatedAsync(isolated);
        using var bob = await ApiClient.CreateAuthenticatedAsync(isolated);
        var c = await ChatTests.Open(alice, bob.User!.Id);
        isolated.SaveChangesFailure.FailNotificationInserts = true;
        var response = await alice.Http.PostAsJsonAsync($"/api/conversations/{c.Id}/messages", new SendMessageRequest(Guid.NewGuid(), "not committed"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await isolated.WithDbContextAsync(db => db.Set<ChatMessage>().CountAsync()));
        Assert.Equal(0, await isolated.WithDbContextAsync(db => db.Set<Notification>().CountAsync()));
    }
    private sealed class TestDelivery : INotificationDelivery
    {
        public bool Fail { get; set; } = true;
        public List<(Guid Recipient, Guid? Id)> Sent { get; } = [];
        public Task ChangedAsync(Guid recipientUserId, Guid? notificationId, CancellationToken ct)
        {
            if (Fail) throw new IOException("Simulated transport failure");
            Sent.Add((recipientUserId, notificationId)); return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Outbox_Retains_Failed_Delivery_And_Retries_Only_The_Recipient()
    {
        using var isolated = new ImageStoreApiFactory();
        using var alice = await ApiClient.CreateAuthenticatedAsync(isolated);
        using var bob = await ApiClient.CreateAuthenticatedAsync(isolated);
        await bob.FollowAsync(alice.User!.Id);
        var delivery = new TestDelivery();
        await isolated.WithDbContextAsync(async db =>
        {
            var dispatcher = new NotificationDispatcher(db, delivery, TimeProvider.System, NullLogger<NotificationDispatcher>.Instance);
            await dispatcher.DispatchBatchAsync(CancellationToken.None);
            var n = await db.Set<Notification>().SingleAsync();
            Assert.Null(n.DispatchedAtUtc); Assert.Equal(1, n.DeliveryAttempts);
            Assert.Single((await List(alice)).Items); // Durable even when realtime is unavailable.
            delivery.Fail = false; n.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
            await dispatcher.DispatchBatchAsync(CancellationToken.None);
            Assert.NotNull(n.DispatchedAtUtc);
            await dispatcher.DispatchBatchAsync(CancellationToken.None);
            Assert.Equal((alice.User.Id, (Guid?)n.Id), Assert.Single(delivery.Sent));
            return true;
        });
    }
}
