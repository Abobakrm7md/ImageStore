using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using ImageStore.IntegrationTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class CommentTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Followers_Can_Add_Edit_And_Delete_Their_Comments()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync();

        var create = await bob.CommentAsync(post.Id, "  Beautiful work!  ");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var comment = (await create.Content.ReadFromJsonAsync<CommentDto>(ApiClient.Json))!;
        Assert.Equal("Beautiful work!", comment.Content); // trimmed
        Assert.Equal(post.Id, comment.PostId);
        Assert.Equal(bob.User!.Id, comment.Author.Id);
        Assert.True(comment.IsMine);
        Assert.True(comment.CanDelete);
        Assert.Null(comment.UpdatedAtUtc);

        var edit = await bob.Http.PutAsJsonAsync(
            $"/api/posts/{post.Id}/comments/{comment.Id}",
            new UpdateCommentRequest("Beautiful work! (edited)"),
            ApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = (await edit.Content.ReadFromJsonAsync<CommentDto>(ApiClient.Json))!;
        Assert.Equal("Beautiful work! (edited)", edited.Content);
        Assert.NotNull(edited.UpdatedAtUtc);

        var delete = await bob.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}/comments/{comment.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await ListAsync(alice, post.Id);
        Assert.Equal(0, list.TotalCount);
    }

    [Fact]
    public async Task Comments_Are_Listed_Oldest_First_With_Pagination()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync();

        for (var index = 0; index < 5; index++)
        {
            await bob.CommentAsync(post.Id, $"Comment {index}");
        }

        var page = await ListAsync(alice, post.Id, "?page=1&pageSize=2");
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal("Comment 0", page.Items[0].Content);
        Assert.Equal("Comment 1", page.Items[1].Content);

        var page2 = await ListAsync(alice, post.Id, "?page=2&pageSize=2");
        Assert.Equal("Comment 2", page2.Items[0].Content);

        // The post author sees CanDelete on foreign comments (moderation), the writer sees IsMine.
        Assert.False(page.Items[0].IsMine);
        Assert.True(page.Items[0].CanDelete);
    }

    [Fact]
    public async Task Empty_Whitespace_And_Oversized_Comments_Are_Rejected()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await alice.PostImageAsync();

        foreach (var content in new[] { "", "   ", "\t\n" })
        {
            var response = await alice.CommentAsync(post.Id, content);
            var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
            Assert.Contains("content", problem.ErrorKeys());
        }

        var tooLong = await alice.CommentAsync(post.Id, new string('x', 2001));
        var longProblem = await tooLong.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("content", longProblem.ErrorKeys());

        var edit = await alice.Http.PutAsJsonAsync(
            $"/api/posts/{post.Id}/comments/{Guid.NewGuid()}",
            new UpdateCommentRequest("   "),
            ApiClient.Json);
        var editProblem = await edit.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("content", editProblem.ErrorKeys());
    }

    [Fact]
    public async Task Users_Without_Access_Cannot_Read_Or_Write_Comments()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var stranger = await ApiClient.CreateAuthenticatedAsync(factory);
        var post = await alice.PostImageAsync();

        var list = await stranger.Http.GetAsync(new Uri($"/api/posts/{post.Id}/comments", UriKind.Relative));
        await list.ReadProblemAsync(HttpStatusCode.NotFound);

        var create = await stranger.CommentAsync(post.Id, "Let me in");
        await create.ReadProblemAsync(HttpStatusCode.NotFound);

        // After unfollowing, previously granted comment access disappears as well.
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var comment = (await (await bob.CommentAsync(post.Id, "Hi")).Content.ReadFromJsonAsync<CommentDto>(ApiClient.Json))!;

        await bob.UnfollowAsync(alice.User.Id);
        var afterUnfollow = await bob.Http.GetAsync(new Uri($"/api/posts/{post.Id}/comments", UriKind.Relative));
        await afterUnfollow.ReadProblemAsync(HttpStatusCode.NotFound);

        var editAfterUnfollow = await bob.Http.PutAsJsonAsync(
            $"/api/posts/{post.Id}/comments/{comment.Id}",
            new UpdateCommentRequest("Edited after unfollow"),
            ApiClient.Json);
        await editAfterUnfollow.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_The_Comment_Author_May_Edit_And_Foreign_Comments_Look_Missing()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        using var carol = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        await carol.FollowAsync(alice.User.Id);
        var post = await alice.PostImageAsync();
        var comment = (await (await bob.CommentAsync(post.Id, "Bob's comment")).Content.ReadFromJsonAsync<CommentDto>(ApiClient.Json))!;

        // Another follower (not the author, not the post owner) may read but not touch it.
        var edit = await carol.Http.PutAsJsonAsync(
            $"/api/posts/{post.Id}/comments/{comment.Id}",
            new UpdateCommentRequest("Hijacked"),
            ApiClient.Json);
        await edit.ReadProblemAsync(HttpStatusCode.NotFound);

        var delete = await carol.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}/comments/{comment.Id}", UriKind.Relative));
        await delete.ReadProblemAsync(HttpStatusCode.NotFound);

        // The comment is untouched.
        var list = await ListAsync(alice, post.Id);
        Assert.Equal("Bob's comment", Assert.Single(list.Items).Content);
    }

    [Fact]
    public async Task The_Post_Author_May_Moderate_Comments_On_Their_Posts()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync();
        var comment = (await (await bob.CommentAsync(post.Id, "Spam!")).Content.ReadFromJsonAsync<CommentDto>(ApiClient.Json))!;

        // Documented moderation policy: the post author may delete (but not edit) any comment.
        var edit = await alice.Http.PutAsJsonAsync(
            $"/api/posts/{post.Id}/comments/{comment.Id}",
            new UpdateCommentRequest("Edited by the post owner"),
            ApiClient.Json);
        await edit.ReadProblemAsync(HttpStatusCode.NotFound);

        var delete = await alice.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}/comments/{comment.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(0, (await ListAsync(alice, post.Id)).TotalCount);
    }

    private static async Task<PagedResult<CommentDto>> ListAsync(ApiClient client, Guid postId, string query = "")
    {
        var response = await client.Http.GetAsync(new Uri($"/api/posts/{postId}/comments{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<CommentDto>>(ApiClient.Json))!;
    }
}
