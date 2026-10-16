using ImageStore.Application.Images.Storage;
using ImageStore.UnitTests.TestSupport;

namespace ImageStore.UnitTests.Storage;

/// <summary>
/// Profile pictures live under <c>{root}/profiles/</c> with server-generated names. The storage
/// abstraction accepts exactly those paths and rejects everything else – including traversal
/// attempts and paths that merely start with the profiles segment.
/// </summary>
public sealed class ProfilePictureStorageTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly Infrastructure.Storage.LocalImageFileStorage _storage;

    public ProfilePictureStorageTests() =>
        _storage = LocalImageFileStorageTests.CreateStorage(Path.Combine(_temp.Path, "uploads"), _temp.Path);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void CreateRelativePath_Builds_Server_Generated_Paths()
    {
        var userId = Guid.NewGuid();
        Assert.Equal($"profiles/{userId:D}.jpg", ProfilePictureStoragePaths.CreateRelativePath(userId, ".jpg"));
        Assert.Equal($"profiles/{userId:D}.webp", ProfilePictureStoragePaths.CreateRelativePath(userId, ".webp"));

        Assert.Throws<ArgumentException>(() => ProfilePictureStoragePaths.CreateRelativePath(Guid.Empty, ".jpg"));
        Assert.Throws<ArgumentException>(() => ProfilePictureStoragePaths.CreateRelativePath(userId, "jpg"));
        Assert.Throws<ArgumentException>(() => ProfilePictureStoragePaths.CreateRelativePath(userId, ".jpg/../../x"));
    }

    [Theory]
    [InlineData("profiles/019a1b2c-0000-4000-8000-000000000000.png", true)]
    [InlineData("profiles/019a1b2c-0000-4000-8000-000000000000.PNG", false)]
    [InlineData("profiles/../019a1b2c-0000-4000-8000-000000000001.png", false)]
    [InlineData("profiles/019a1b2c-0000-4000-8000-000000000000.png/extra", false)]
    [InlineData("../profiles/019a1b2c-0000-4000-8000-000000000000.png", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("profiles/evil", false)]
    [InlineData("", false)]
    public void Only_Strict_Profile_Paths_Are_Valid(string path, bool expected) =>
        Assert.Equal(expected, ProfilePictureStoragePaths.IsValidRelativePath(path));

    [Fact]
    public async Task Staged_Profile_Pictures_Commit_Into_The_Profiles_Directory()
    {
        var userId = Guid.NewGuid();
        var relativePath = ProfilePictureStoragePaths.CreateRelativePath(userId, ".png");
        var content = TestFiles.Read("sample.png");

        await using (var staged = await _storage.StageAsync(new MemoryStream(content), 4096, CancellationToken.None))
        {
            await staged.CommitAsync(relativePath, CancellationToken.None);
        }

        var finalPath = Path.Combine(_storage.RootPath, "profiles", $"{userId:D}.png");
        Assert.Equal(content, await File.ReadAllBytesAsync(finalPath));

        // Reading through the abstraction resolves against the configured root.
        await using var read = _storage.OpenRead(relativePath);
        Assert.Equal(content.Length, read.Length);
    }

    [Fact]
    public async Task Traversal_Paths_Are_Rejected_By_The_Storage_Abstraction()
    {
        var userId = Guid.NewGuid();
        await using var staged = await _storage.StageAsync(new MemoryStream([1, 2, 3]), 16, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidStoragePathException>(
            () => staged.CommitAsync($"profiles/../../{userId:D}.png", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidStoragePathException>(
            () => staged.CommitAsync("profiles/../../../etc/passwd", CancellationToken.None));
        Assert.Throws<InvalidStoragePathException>(() => _storage.OpenRead("profiles/../../secret.png"));
        Assert.Throws<InvalidStoragePathException>(() => _storage.OpenRead($"profiles/{userId:D}.png.exe"));
    }
}
