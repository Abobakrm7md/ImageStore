using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Posts;
using ImageStore.Domain.Posts;
using ImageStore.IntegrationTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class ReactionTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Reactions_Can_Be_Added_Changed_And_Removed()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync();

        var added = await bob.ReactAsync(post.Id, "like");
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var summary = (await added.Content.ReadFromJsonAsync<ReactionSummaryDto>(ApiClient.Json))!;
        Assert.Equal(1, summary.TotalCount);
        Assert.Equal(ReactionType.Like, summary.MyReaction);
        Assert.Equal(1, Count(summary, ReactionType.Like));
        Assert.Equal(0, Count(summary, ReactionType.Dislike));
        Assert.Equal(6, summary.Counts.Count); // every known type is reported, zeros included

        // Changing replaces the existing reaction – still exactly one per user.
        var changed = await bob.ReactAsync(post.Id, "love");
        var changedSummary = (await changed.Content.ReadFromJsonAsync<ReactionSummaryDto>(ApiClient.Json))!;
        Assert.Equal(1, changedSummary.TotalCount);
        Assert.Equal(ReactionType.Love, changedSummary.MyReaction);
        Assert.Equal(0, Count(changedSummary, ReactionType.Like));
        Assert.Equal(1, Count(changedSummary, ReactionType.Love));

        var rows = await factory.WithDbContextAsync(db =>
            Task.FromResult(db.PostReactions.Count(reaction => reaction.PostId == post.Id && reaction.UserId == bob.User!.Id)));
        Assert.Equal(1, rows);

        // Removing deletes it; removing again is idempotent.
        var removed = await bob.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}/my-reaction", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var removedAgain = await bob.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}/my-reaction", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, removedAgain.StatusCode);

        var empty = await GetSummaryAsync(bob, post.Id);
        Assert.Equal(0, empty.TotalCount);
        Assert.Null(empty.MyReaction);
    }

    [Fact]
    public async Task Repeated_Same_Reaction_Requests_Never_Create_Duplicates()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync();

        // Repeat the same request several times (double-clicked button). True concurrent inserts
        // are additionally blocked by the unique (PostId, UserId) index – verified against the
        // SQL Server model in SocialPersistenceTests.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await bob.ReactAsync(post.Id, "wow");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var summary = await GetSummaryAsync(alice, post.Id);
        Assert.Equal(1, summary.TotalCount);
        Assert.Equal(1, Count(summary, ReactionType.Wow));

        var rows = await factory.WithDbContextAsync(db =>
            Task.FromResult(db.PostReactions.Count(reaction => reaction.PostId == post.Id)));
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Counts_Are_Grouped_By_Type_Across_Users()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await alice.PostImageAsync();
        var followers = new List<ApiClient>();
        try
        {
            for (var index = 0; index < 4; index++)
            {
                var follower = await ApiClient.CreateAuthenticatedAsync(factory);
                await follower.FollowAsync(alice.User!.Id);
                followers.Add(follower);
            }

            await followers[0].ReactAsync(post.Id, "like");
            await followers[1].ReactAsync(post.Id, "like");
            await followers[2].ReactAsync(post.Id, "laugh");
            await followers[3].ReactAsync(post.Id, "sad");

            var summary = await GetSummaryAsync(alice, post.Id);
            Assert.Equal(4, summary.TotalCount);
            Assert.Equal(2, Count(summary, ReactionType.Like));
            Assert.Equal(1, Count(summary, ReactionType.Laugh));
            Assert.Equal(1, Count(summary, ReactionType.Sad));
            Assert.Equal(0, Count(summary, ReactionType.Love));
            Assert.Null(summary.MyReaction);

            // Each user identifies their own reaction.
            Assert.Equal(ReactionType.Laugh, (await GetSummaryAsync(followers[2], post.Id)).MyReaction);
        }
        finally
        {
            followers.ForEach(follower => follower.Dispose());
        }
    }

    [Fact]
    public async Task Unknown_Reaction_Names_Are_Rejected()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await alice.PostImageAsync();

        foreach (var reaction in new[] { "angry", "", "LIKE2", "3" })
        {
            var response = await alice.ReactAsync(post.Id, reaction);
            var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
            Assert.Contains("reaction", problem.ErrorKeys());
        }

        // Case-insensitive stable names are accepted.
        var ok = await alice.ReactAsync(post.Id, "LIKE");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Only_Users_With_Post_Access_Can_React()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var stranger = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await alice.PostImageAsync();

        var summary = await stranger.Http.GetAsync(new Uri($"/api/posts/{post.Id}/reactions", UriKind.Relative));
        await summary.ReadProblemAsync(HttpStatusCode.NotFound);

        var react = await stranger.ReactAsync(post.Id, "like");
        await react.ReadProblemAsync(HttpStatusCode.NotFound);

        var remove = await stranger.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}/my-reaction", UriKind.Relative));
        await remove.ReadProblemAsync(HttpStatusCode.NotFound);

        // Bob reacts, then unfollows: his reaction is gone from his view and he can no longer change it.
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        await bob.ReactAsync(post.Id, "love");
        Assert.Equal(1, (await GetSummaryAsync(alice, post.Id)).TotalCount);

        await bob.UnfollowAsync(alice.User.Id);
        var afterUnfollow = await bob.ReactAsync(post.Id, "like");
        await afterUnfollow.ReadProblemAsync(HttpStatusCode.NotFound);

        // The reaction row of the unfollowed user remains for the author's counts until removed
        // by its owner or by deleting the post (documented retention policy).
        Assert.Equal(1, (await GetSummaryAsync(alice, post.Id)).TotalCount);
    }

    private static async Task<ReactionSummaryDto> GetSummaryAsync(ApiClient client, Guid postId)
    {
        var response = await client.Http.GetAsync(new Uri($"/api/posts/{postId}/reactions", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReactionSummaryDto>(ApiClient.Json))!;
    }

    private static int Count(ReactionSummaryDto summary, ReactionType reaction) =>
        summary.Counts.Single(count => count.Reaction == reaction).Count;
}
