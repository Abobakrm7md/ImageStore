using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using ImageStore.IntegrationTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class PostTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Publishing_An_Image_Creates_A_Post_With_The_Image_Data()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await alice.UploadSingleAsync("sample.jpg", "Sunset study", "Golden hour", ["photo"]);

        var post = await alice.CreatePostAsync(image.Id, "Sunset study", "Taken at the coast.");

        Assert.Equal("Sunset study", post.Title);
        Assert.Equal("Taken at the coast.", post.Description);
        Assert.Equal(image.Id, post.Image.ImageId);
        Assert.Equal("image/jpeg", post.Image.ContentType);
        Assert.Equal(alice.User!.Id, post.Author.Id);
        Assert.True(post.IsMine);
        Assert.Equal(0, post.CommentCount);
        Assert.Equal(0, post.ReactionCount);
        Assert.Null(post.MyReaction);

        // The published post shows up on the author's own post list and in the profile count.
        var ownPosts = await alice.Http.GetFromJsonAsync<PagedResult<ImagePostDto>>(
            $"/api/users/{alice.User!.Id}/posts", ApiClient.Json);
        Assert.Equal(post.Id, Assert.Single(ownPosts!.Items).Id);
        Assert.True(ownPosts.Items[0].IsMine);

        var profile = await alice.GetProfileAsync(alice.User.Id);
        Assert.Equal(1, profile.PostCount);
    }

    [Fact]
    public async Task Post_Titles_Default_To_The_Image_Title_And_Are_Validated()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await alice.UploadSingleAsync("sample.png", "Original title");

        var response = await alice.Http.PostAsJsonAsync("/api/posts", new CreatePostRequest(image.Id, null, null), ApiClient.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = (await response.Content.ReadFromJsonAsync<ImagePostDto>(ApiClient.Json))!;
        Assert.Equal("Original title", post.Title);

        var longTitle = await alice.Http.PostAsJsonAsync(
            "/api/posts",
            new CreatePostRequest(image.Id, new string('x', 201), null),
            ApiClient.Json);
        var problem = await longTitle.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("title", problem.ErrorKeys());

        var missingImage = await alice.Http.PostAsJsonAsync("/api/posts", new CreatePostRequest(null, "No image", null), ApiClient.Json);
        var missingProblem = await missingImage.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("imageId", missingProblem.ErrorKeys());
    }

    [Fact]
    public async Task An_Image_Can_Only_Be_Published_Once()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        var image = await alice.UploadSingleAsync("sample.png");
        await alice.CreatePostAsync(image.Id);

        var again = await alice.Http.PostAsJsonAsync("/api/posts", new CreatePostRequest(image.Id, null, null), ApiClient.Json);
        await again.ReadProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_The_Author_Can_Edit_Or_Delete_A_Post()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync("sample.png", "Alice's design");

        // A follower can see the post but not change it – foreign posts look like missing ones.
        var foreignUpdate = await bob.Http.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdatePostRequest("Hacked", null), ApiClient.Json);
        await foreignUpdate.ReadProblemAsync(HttpStatusCode.NotFound);

        var foreignDelete = await bob.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}", UriKind.Relative));
        await foreignDelete.ReadProblemAsync(HttpStatusCode.NotFound);

        var missingUpdate = await alice.Http.PutAsJsonAsync($"/api/posts/{Guid.NewGuid()}", new UpdatePostRequest("Nothing", null), ApiClient.Json);
        await missingUpdate.ReadProblemAsync(HttpStatusCode.NotFound);

        // The author can edit.
        var update = await alice.Http.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdatePostRequest("Alice's design v2", " Updated. "), ApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<ImagePostDto>(ApiClient.Json))!;
        Assert.Equal("Alice's design v2", updated.Title);
        Assert.Equal("Updated.", updated.Description);
        Assert.NotNull(updated.UpdatedAtUtc);

        // An empty title is rejected.
        var invalid = await alice.Http.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdatePostRequest("  ", null), ApiClient.Json);
        var problem = await invalid.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("title", problem.ErrorKeys());

        // The author can delete.
        var delete = await alice.Http.DeleteAsync(new Uri($"/api/posts/{post.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var gone = await alice.Http.GetAsync(new Uri($"/api/posts/{post.Id}", UriKind.Relative));
        await gone.ReadProblemAsync(HttpStatusCode.NotFound);

        // Deleting the post only unpublishes: the image stays in the private gallery.
        var imageStillThere = await alice.Http.GetAsync(new Uri($"/api/images/{post.Image.ImageId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, imageStillThere.StatusCode);
    }

    [Fact]
    public async Task Publishing_Requires_Ownership_Of_The_Image()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        var alicesImage = await alice.UploadSingleAsync("sample.png");

        // Bob cannot publish Alice's image – it looks like a missing image to him.
        var response = await bob.Http.PostAsJsonAsync("/api/posts", new CreatePostRequest(alicesImage.Id, null, null), ApiClient.Json);
        await response.ReadProblemAsync(HttpStatusCode.NotFound);

        var missing = await bob.Http.PostAsJsonAsync("/api/posts", new CreatePostRequest(Guid.NewGuid(), null, null), ApiClient.Json);
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_Content_Is_Served_To_The_Author_And_Followers_Only()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var follower = await ApiClient.CreateAuthenticatedAsync(factory);
        using var stranger = await ApiClient.CreateAuthenticatedAsync(factory);
        await follower.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync("sample.png");

        foreach (var client in new[] { alice, follower })
        {
            var content = await client.Http.GetAsync(new Uri($"/api/posts/{post.Id}/content", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, content.StatusCode);
            Assert.Equal("image/png", content.Headers.ContentType?.MediaType);
            Assert.Equal("private, no-cache", content.Headers.CacheControl?.ToString());

            var download = await client.Http.GetAsync(new Uri($"/api/posts/{post.Id}/download", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Contains("attachment", download.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        }

        foreach (var suffix in new[] { "/content", "/download" })
        {
            var blocked = await stranger.Http.GetAsync(new Uri($"/api/posts/{post.Id}{suffix}", UriKind.Relative));
            await blocked.ReadProblemAsync(HttpStatusCode.NotFound);
        }

        // The raw image endpoint stays owner-only even for followers (the gallery is private).
        var rawImage = await follower.Http.GetAsync(new Uri($"/api/images/{post.Image.ImageId}/content", UriKind.Relative));
        await rawImage.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_The_Image_Removes_The_Published_Post()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await bob.FollowAsync(alice.User!.Id);
        var post = await alice.PostImageAsync("sample.png");
        await bob.ReactAsync(post.Id, "like");
        await bob.CommentAsync(post.Id, "Lovely");

        var deleteImage = await alice.Http.DeleteAsync(new Uri($"/api/images/{post.Image.ImageId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleteImage.StatusCode);

        var gone = await bob.Http.GetAsync(new Uri($"/api/posts/{post.Id}", UriKind.Relative));
        await gone.ReadProblemAsync(HttpStatusCode.NotFound);
        Assert.Equal(0, (await bob.GetFeedAsync()).TotalCount);

        // Comments and reactions went with the post.
        var comments = await factory.WithDbContextAsync(db =>
            Task.FromResult(db.Comments.Count(comment => comment.PostId == post.Id)));
        var reactions = await factory.WithDbContextAsync(db =>
            Task.FromResult(db.PostReactions.Count(reaction => reaction.PostId == post.Id)));
        Assert.Equal(0, comments);
        Assert.Equal(0, reactions);
    }

    [Fact]
    public async Task User_Post_Lists_Are_Visible_To_The_Author_And_Followers_Only()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var follower = await ApiClient.CreateAuthenticatedAsync(factory);
        using var stranger = await ApiClient.CreateAuthenticatedAsync(factory);
        await follower.FollowAsync(alice.User!.Id);
        await alice.PostImageAsync("sample.png", "Published work");

        foreach (var client in new[] { alice, follower })
        {
            var response = await client.Http.GetAsync(new Uri($"/api/users/{alice.User!.Id}/posts", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.Content.ReadFromJsonAsync<PagedResult<ImagePostDto>>(ApiClient.Json))!;
            Assert.Equal("Published work", Assert.Single(page.Items).Title);
        }

        var blocked = await stranger.Http.GetAsync(new Uri($"/api/users/{alice.User!.Id}/posts", UriKind.Relative));
        await blocked.ReadProblemAsync(HttpStatusCode.NotFound);

        var missingUser = await alice.Http.GetAsync(new Uri($"/api/users/{Guid.NewGuid()}/posts", UriKind.Relative));
        await missingUser.ReadProblemAsync(HttpStatusCode.NotFound);
    }
}
