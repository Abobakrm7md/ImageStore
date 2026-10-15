using System.Net;
using ImageStore.Domain.Posts;
using ImageStore.IntegrationTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class FeedTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task The_Feed_Contains_Only_Posts_Of_Followed_Users()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var followed = await ApiClient.CreateAuthenticatedAsync(factory);
        using var stranger = await ApiClient.CreateAuthenticatedAsync(factory);

        var followedPost = await followed.PostImageAsync("sample.png", "Followed work");
        var strangerPost = await stranger.PostImageAsync("sample.jpg", "Stranger work");

        // Before following: the feed is empty (posts are follower-only).
        var empty = await alice.GetFeedAsync();
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);

        await alice.FollowAsync(followed.User!.Id);

        var feed = await alice.GetFeedAsync();
        Assert.Equal(1, feed.TotalCount);
        var item = Assert.Single(feed.Items);
        Assert.Equal(followedPost.Id, item.Id);
        Assert.Equal("Followed work", item.Title);
        Assert.DoesNotContain(feed.Items, post => post.Id == strangerPost.Id);

        // The author information needed to render the post is included.
        Assert.Equal(followed.User.Id, item.Author.Id);
        Assert.False(string.IsNullOrEmpty(item.Author.UserName));
        Assert.False(item.IsMine);

        // The followed user sees their own posts on their profile, not in their feed
        // (the feed is strictly posts of followed users).
        var ownFeed = await followed.GetFeedAsync();
        Assert.Equal(0, ownFeed.TotalCount);
    }

    [Fact]
    public async Task Unfollowing_Removes_Posts_From_The_Feed_And_Blocks_Direct_Access()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await alice.FollowAsync(bob.User!.Id);
        var post = await bob.PostImageAsync();

        Assert.Equal(1, (await alice.GetFeedAsync()).TotalCount);
        var accessible = await alice.Http.GetAsync(new Uri($"/api/posts/{post.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, accessible.StatusCode);
        var content = await alice.Http.GetAsync(new Uri($"/api/posts/{post.Id}/content", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);

        await alice.UnfollowAsync(bob.User.Id);

        Assert.Equal(0, (await alice.GetFeedAsync()).TotalCount);
        foreach (var suffix in new[] { string.Empty, "/content", "/download", "/comments", "/reactions" })
        {
            var blocked = await alice.Http.GetAsync(new Uri($"/api/posts/{post.Id}{suffix}", UriKind.Relative));
            await blocked.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        // The author keeps full access.
        var forAuthor = await bob.Http.GetAsync(new Uri($"/api/posts/{post.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, forAuthor.StatusCode);
    }

    [Fact]
    public async Task The_Feed_Is_Newest_First_And_Paginates_Without_Duplicates()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        using var carol = await ApiClient.CreateAuthenticatedAsync(factory);
        await alice.FollowAsync(bob.User!.Id);
        await alice.FollowAsync(carol.User!.Id);

        var created = new List<Guid>();
        for (var index = 0; index < 7; index++)
        {
            var author = index % 2 == 0 ? bob : carol;
            created.Add((await author.PostImageAsync(title: $"Post {index}")).Id);
        }

        var page1 = await alice.GetFeedAsync("?page=1&pageSize=3");
        var page2 = await alice.GetFeedAsync("?page=2&pageSize=3");
        var page3 = await alice.GetFeedAsync("?page=3&pageSize=3");

        Assert.Equal(7, page1.TotalCount);
        Assert.Equal(3, page3.TotalPages);
        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(3, page2.Items.Count);
        Assert.Single(page3.Items);

        var all = page1.Items.Concat(page2.Items).Concat(page3.Items).ToList();
        Assert.Equal(created.Count, all.Select(post => post.Id).Distinct().Count()); // no duplicates
        Assert.Equal(created.Count, all.Count);

        // Deterministic newest-first ordering (server-side, timestamps may be equal in tests,
        // in which case the Id tiebreaker keeps the order total).
        var expected = all.OrderByDescending(post => post.CreatedAtUtc).ThenByDescending(post => post.Id).ToList();
        Assert.Equal(expected.Select(post => post.Id), all.Select(post => post.Id));
        Assert.Equal(created[^1], page1.Items[0].Id);
    }

    [Fact]
    public async Task Feed_Items_Carry_Comment_And_Reaction_Counts()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await alice.FollowAsync(bob.User!.Id);
        var post = await bob.PostImageAsync();

        await alice.CommentAsync(post.Id, "Nice!");
        await alice.ReactAsync(post.Id, "love");

        var feed = await alice.GetFeedAsync();
        var item = Assert.Single(feed.Items);
        Assert.Equal(1, item.CommentCount);
        Assert.Equal(1, item.ReactionCount);
        Assert.Equal(ReactionType.Love, item.MyReaction);
        Assert.Equal(1, item.ReactionCounts.Single(count => count.Reaction == ReactionType.Love).Count);
        Assert.Equal(0, item.ReactionCounts.Single(count => count.Reaction == ReactionType.Like).Count);
        Assert.Equal(6, item.ReactionCounts.Count);
    }

    [Fact]
    public async Task Feed_Pagination_Parameters_Are_Validated()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);

        var badPage = await alice.Http.GetAsync(new Uri("/api/feed?page=0", UriKind.Relative));
        var problem = await badPage.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("page", problem.ErrorKeys());

        var badSize = await alice.Http.GetAsync(new Uri("/api/feed?pageSize=1000", UriKind.Relative));
        var sizeProblem = await badSize.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("pageSize", sizeProblem.ErrorKeys());
    }

    [Fact]
    public async Task The_Feed_Requires_Authentication()
    {
        using var anonymous = new ApiClient(factory);
        var response = await anonymous.Http.GetAsync(new Uri("/api/feed", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
