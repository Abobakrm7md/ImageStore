using System.Net;
using ImageStore.IntegrationTests.TestSupport;
using ImageStore.UnitTests.TestSupport;

namespace ImageStore.IntegrationTests;

/// <summary>
/// File-storage durability: uploads and profile pictures live under the configured
/// <c>Storage:RootPath</c>, keep their relative database paths, and are still served when the
/// application is "restarted" (a second host on the same storage root and database).
/// </summary>
public sealed class StoragePersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imagestore-restart", Guid.NewGuid().ToString("N"));
    private readonly string _databaseName = $"imagestore-restart-{Guid.NewGuid():N}";

    [Fact]
    public async Task Uploaded_Images_And_Profile_Pictures_Survive_An_Application_Restart()
    {
        Guid imageId;
        Guid postId;
        Guid userId;

        await using (var firstHost = CreateHost())
        {
            using var alice = await ApiClient.CreateAuthenticatedAsync(firstHost);
            userId = alice.User!.Id;

            var image = await alice.UploadSingleAsync("sample.png", "Persistent picture");
            imageId = image.Id;
            postId = (await alice.CreatePostAsync(image.Id)).Id;

            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(TestFiles.Read("sample.jpg")), "file", "avatar.jpg" },
            };
            var picture = await alice.Http.PutAsync(new Uri("/api/users/me/profile-picture", UriKind.Relative), form);
            Assert.Equal(HttpStatusCode.OK, picture.StatusCode);

            // The files are on disk under the configured root while the first host is running.
            Assert.Contains(firstHost.StoredFiles(), path => path.Contains(imageId.ToString(), StringComparison.Ordinal));
            Assert.Contains(firstHost.StoredFiles(), path =>
                path.Contains($"{Path.DirectorySeparatorChar}profiles{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        }

        // "Restart": a brand-new host, same database, same storage root. Nothing was deleted.
        await using var secondHost = CreateHost();
        Assert.Contains(secondHost.StoredFiles(), path => path.Contains(imageId.ToString(), StringComparison.Ordinal));

        using var returned = await ApiClient.CreateAuthenticatedAsync(secondHost, $"returned-{Guid.NewGuid():N}@example.com");
        await returned.FollowAsync(userId);

        var content = await returned.Http.GetAsync(new Uri($"/api/posts/{postId}/content", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.True((await content.Content.ReadAsByteArrayAsync()).Length > 0);

        var avatar = await returned.Http.GetAsync(new Uri($"/api/users/{userId}/profile-picture", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        Assert.Equal("image/jpeg", avatar.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_Storage_Root_Comes_From_Configuration_And_Relative_Paths_Are_Stored()
    {
        await using var host = CreateHost();
        using var alice = await ApiClient.CreateAuthenticatedAsync(host);
        var image = await alice.UploadSingleAsync("sample.png");

        var relativePath = await host.WithDbContextAsync(db =>
            Task.FromResult(db.Images.First(candidate => candidate.Id == image.Id).RelativeStoragePath));

        // Only the relative path is stored; the machine-specific root is never persisted.
        Assert.Equal($"{alice.User!.Id}/{image.Id}.png", relativePath);
        Assert.False(Path.IsPathRooted(relativePath));
        Assert.True(File.Exists(Path.Combine(host.StorageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    private ImageStoreApiFactory CreateHost() => new(_root, _databaseName);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
