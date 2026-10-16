using System.Net;
using System.Net.Http.Json;
using ImageStore.Application.Common;
using ImageStore.Application.Social;
using ImageStore.IntegrationTests.TestSupport;
using ImageStore.UnitTests.TestSupport;

namespace ImageStore.IntegrationTests;

public sealed class SocialTests(ImageStoreApiFactory factory) : IClassFixture<ImageStoreApiFactory>
{
    [Fact]
    public async Task Registration_Creates_A_Profile_With_A_Unique_User_Name()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory, "Alice.Smith@example.com");
        using var second = await ApiClient.CreateAuthenticatedAsync(factory, "alice.smith@example.org");

        var aliceProfile = await alice.GetProfileAsync(alice.User!.Id);
        var secondProfile = await second.GetProfileAsync(second.User!.Id);

        Assert.Equal(alice.User.Id, aliceProfile.Id);
        Assert.Equal("alice.smith", aliceProfile.UserName);
        Assert.Equal("Alice.Smith", aliceProfile.DisplayName);
        Assert.True(aliceProfile.IsCurrentUser);
        Assert.False(aliceProfile.IsFollowing);
        Assert.Equal(0, aliceProfile.FollowerCount);
        Assert.Equal(0, aliceProfile.FollowingCount);
        Assert.Equal(0, aliceProfile.PostCount);

        // Both emails sanitize to the same user name; the second profile must get a unique one.
        Assert.NotEqual(aliceProfile.UserName, secondProfile.UserName);
        Assert.StartsWith("alice.smith", secondProfile.UserName, StringComparison.Ordinal);
        Assert.True(UserNameIsValid(secondProfile.UserName));
    }

    private static bool UserNameIsValid(string userName) =>
        userName.Length is >= 3 and <= 30
        && char.IsAsciiLetterOrDigit(userName[0])
        && char.IsAsciiLetterOrDigit(userName[^1])
        && userName.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    [Fact]
    public async Task Profiles_Can_Be_Updated_And_Searched()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);
        var update = await client.Http.PutAsJsonAsync(
            "/api/users/me",
            new UpdateMyProfileRequest("  Ada Lovelace  ", "  Mathematician, first programmer.  "),
            ApiClient.Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<UserProfileDto>(ApiClient.Json))!;
        Assert.Equal("Ada Lovelace", updated.DisplayName);
        Assert.Equal("Mathematician, first programmer.", updated.Bio);

        using var searcher = await ApiClient.CreateAuthenticatedAsync(factory);
        var byDisplay = await SearchAsync(searcher, "lovelace");
        Assert.Contains(byDisplay.Items, user => user.Id == client.User!.Id);

        var byUserName = await SearchAsync(searcher, updated.UserName);
        Assert.Contains(byUserName.Items, user => user.Id == client.User!.Id);

        var nothing = await SearchAsync(searcher, "definitely-not-here");
        Assert.Empty(nothing.Items);
    }

    [Fact]
    public async Task Profile_Updates_Are_Validated()
    {
        using var client = await ApiClient.CreateAuthenticatedAsync(factory);

        var empty = await client.Http.PutAsJsonAsync("/api/users/me", new UpdateMyProfileRequest("   ", null), ApiClient.Json);
        var problem = await empty.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("displayName", problem.ErrorKeys());

        var longBio = await client.Http.PutAsJsonAsync(
            "/api/users/me",
            new UpdateMyProfileRequest("Ok", new string('x', 501)),
            ApiClient.Json);
        var bioProblem = await longBio.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("bio", bioProblem.ErrorKeys());
    }

    [Fact]
    public async Task Following_Is_Immediate_Idempotent_And_Reflected_In_Counts()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);

        var follow = await alice.FollowAsync(bob.User!.Id);
        Assert.Equal(HttpStatusCode.NoContent, follow.StatusCode);

        // Duplicate follows never create a second relationship.
        var again = await alice.FollowAsync(bob.User.Id);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        var bobsProfile = await alice.GetProfileAsync(bob.User.Id);
        Assert.True(bobsProfile.IsFollowing);
        Assert.Equal(1, bobsProfile.FollowerCount);

        var alicesProfile = await bob.GetProfileAsync(alice.User!.Id);
        Assert.False(alicesProfile.IsFollowing);
        Assert.Equal(1, alicesProfile.FollowingCount);

        var followers = await bob.Http.GetFromJsonAsync<PagedResult<UserSummaryDto>>(
            $"/api/users/{bob.User.Id}/followers", ApiClient.Json);
        Assert.Equal(alice.User.Id, Assert.Single(followers!.Items).Id);

        var following = await alice.Http.GetFromJsonAsync<PagedResult<UserSummaryDto>>(
            $"/api/users/{alice.User.Id}/following", ApiClient.Json);
        Assert.Equal(bob.User.Id, Assert.Single(following!.Items).Id);
    }

    [Fact]
    public async Task Unfollowing_Removes_The_Relationship_And_Is_Idempotent()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await alice.FollowAsync(bob.User!.Id);

        var unfollow = await alice.UnfollowAsync(bob.User.Id);
        Assert.Equal(HttpStatusCode.NoContent, unfollow.StatusCode);

        var again = await alice.UnfollowAsync(bob.User.Id);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        var profile = await alice.GetProfileAsync(bob.User.Id);
        Assert.False(profile.IsFollowing);
        Assert.Equal(0, profile.FollowerCount);
        Assert.Equal(0, (await alice.GetProfileAsync(alice.User!.Id)).FollowingCount);
    }

    [Fact]
    public async Task Self_Follow_Is_Rejected_And_Unknown_Users_Are_Not_Found()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);

        var self = await alice.FollowAsync(alice.User!.Id);
        var problem = await self.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("userId", problem.ErrorKeys());

        var missing = await alice.FollowAsync(Guid.NewGuid());
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);

        var missingProfile = await alice.Http.GetAsync(new Uri($"/api/users/{Guid.NewGuid()}", UriKind.Relative));
        await missingProfile.ReadProblemAsync(HttpStatusCode.NotFound);

        var missingFollowers = await alice.Http.GetAsync(new Uri($"/api/users/{Guid.NewGuid()}/followers", UriKind.Relative));
        await missingFollowers.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Follow_Relationships_Are_Directed()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
        await alice.FollowAsync(bob.User!.Id);

        // Alice follows Bob, so Bob's feed-relevant state changes – but Bob does not follow Alice back.
        var bobsViewOfAlice = await bob.GetProfileAsync(alice.User!.Id);
        Assert.False(bobsViewOfAlice.IsFollowing);
        Assert.Equal(0, bobsViewOfAlice.FollowingCount);
        Assert.Equal(1, bobsViewOfAlice.FollowerCount);
    }

    [Fact]
    public async Task Follower_And_Following_Lists_Paginate()
    {
        using var target = await ApiClient.CreateAuthenticatedAsync(factory);
        for (var index = 0; index < 3; index++)
        {
            using var follower = await ApiClient.CreateAuthenticatedAsync(factory);
            await follower.FollowAsync(target.User!.Id);
        }

        var page1 = await target.Http.GetFromJsonAsync<PagedResult<UserSummaryDto>>(
            $"/api/users/{target.User!.Id}/followers?page=1&pageSize=2", ApiClient.Json);
        var page2 = await target.Http.GetFromJsonAsync<PagedResult<UserSummaryDto>>(
            $"/api/users/{target.User.Id}/followers?page=2&pageSize=2", ApiClient.Json);

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page1.TotalPages);
        Assert.Single(page2!.Items);
        Assert.DoesNotContain(page2.Items[0].Id, page1.Items.Select(user => user.Id));
    }

    [Fact]
    public async Task Profile_Pictures_Can_Be_Set_Fetched_And_Removed()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
        using var bob = await ApiClient.CreateAuthenticatedAsync(factory);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(TestFiles.Read("sample.png")), "file", "avatar.png" },
        };
        var upload = await alice.Http.PutAsync(new Uri("/api/users/me/profile-picture", UriKind.Relative), form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var profile = (await upload.Content.ReadFromJsonAsync<UserProfileDto>(ApiClient.Json))!;
        Assert.True(profile.HasProfilePicture);

        // Any authenticated user can fetch the avatar (it is rendered in feeds and lists).
        var picture = await bob.Http.GetAsync(new Uri($"/api/users/{alice.User!.Id}/profile-picture", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("image/png", picture.Content.Headers.ContentType?.MediaType);

        var stored = factory.StoredFiles();
        Assert.Contains(stored, path => path.Contains($"{Path.DirectorySeparatorChar}profiles{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var remove = await alice.Http.DeleteAsync(new Uri("/api/users/me/profile-picture", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var gone = await bob.Http.GetAsync(new Uri($"/api/users/{alice.User.Id}/profile-picture", UriKind.Relative));
        await gone.ReadProblemAsync(HttpStatusCode.NotFound);
        Assert.DoesNotContain(factory.StoredFiles(), path => path.Contains($"{Path.DirectorySeparatorChar}profiles{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Profile_Pictures_Are_Validated_Like_Uploads()
    {
        using var alice = await ApiClient.CreateAuthenticatedAsync(factory);

        using var notAnImage = new MultipartFormDataContent
        {
            { new ByteArrayContent("this is not a PNG"u8.ToArray()), "file", "evil.png" },
        };
        var rejected = await alice.Http.PutAsync(new Uri("/api/users/me/profile-picture", UriKind.Relative), notAnImage);
        var problem = await rejected.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("file", problem.ErrorKeys());

        using var missing = new MultipartFormDataContent();
        var noFile = await alice.Http.PutAsync(new Uri("/api/users/me/profile-picture", UriKind.Relative), missing);
        var noFileProblem = await noFile.ReadProblemAsync(HttpStatusCode.BadRequest);
        Assert.Contains("file", noFileProblem.ErrorKeys());

        Assert.False((await alice.GetProfileAsync(alice.User!.Id)).HasProfilePicture);
    }

    private static async Task<PagedResult<UserSummaryDto>> SearchAsync(ApiClient client, string search)
    {
        var response = await client.Http.GetAsync(new Uri($"/api/users?search={Uri.EscapeDataString(search)}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<UserSummaryDto>>(ApiClient.Json))!;
    }
}
