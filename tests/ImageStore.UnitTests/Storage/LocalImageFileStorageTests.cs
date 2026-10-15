using ImageStore.Application.Images.Storage;
using ImageStore.Infrastructure.Storage;
using ImageStore.UnitTests.TestSupport;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ImageStore.UnitTests.Storage;

public sealed class LocalImageFileStorageTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly LocalImageFileStorage _storage;
    private readonly string _root;

    public LocalImageFileStorageTests()
    {
        _root = Path.Combine(_temp.Path, "uploads");
        _storage = CreateStorage(_root, _temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    private static string NewRelativePath(string extension = ".png") =>
        ImageStoragePaths.CreateRelativePath(Guid.NewGuid(), ImageStoragePaths.CreateStoredFileName(Guid.NewGuid(), extension));

    [Fact]
    public async Task Stage_Then_Commit_Moves_The_File_Into_Place()
    {
        var content = TestFiles.Read("sample.png");
        var relativePath = NewRelativePath();

        await using (var staged = await _storage.StageAsync(new MemoryStream(content), 1024, CancellationToken.None))
        {
            Assert.Equal(content.Length, staged.Length);
            await staged.CommitAsync(relativePath, CancellationToken.None);
        }

        var finalPath = Path.Combine(_root, relativePath);
        Assert.Equal(content, await File.ReadAllBytesAsync(finalPath));
        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
    }

    [Fact]
    public async Task Disposing_An_Uncommitted_Staged_File_Deletes_It()
    {
        await using (var staged = await _storage.StageAsync(new MemoryStream(TestFiles.Read("sample.jpg")), 4096, CancellationToken.None))
        {
            Assert.Single(Directory.GetFiles(_storage.StagingPath));
        }

        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
    }

    [Fact]
    public async Task Staging_Stops_At_The_Size_Limit_And_Leaves_No_Partial_File()
    {
        var oversized = new MemoryStream(new byte[10_000]);

        await Assert.ThrowsAsync<FileSizeLimitExceededException>(() => _storage.StageAsync(oversized, 9_999, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Commit_Never_Overwrites_An_Existing_File()
    {
        var relativePath = NewRelativePath();
        await using (var first = await _storage.StageAsync(new MemoryStream([1, 2, 3]), 100, CancellationToken.None))
        {
            await first.CommitAsync(relativePath, CancellationToken.None);
        }

        await using (var second = await _storage.StageAsync(new MemoryStream([9, 9, 9, 9]), 100, CancellationToken.None))
        {
            await Assert.ThrowsAsync<StorageConflictException>(() => second.CommitAsync(relativePath, CancellationToken.None));
        }

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(_root, relativePath)));
        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData(".staging/whatever.tmp")]
    [InlineData("11111111-2222-3333-4444-555555555555/../../../outside.png")]
    public async Task Rejects_Path_Traversal_For_Every_Operation(string maliciousPath)
    {
        var outside = Path.Combine(_temp.Path, "outside.png");
        await File.WriteAllBytesAsync(outside, [42]);

        Assert.Throws<InvalidStoragePathException>(() => _storage.OpenRead(maliciousPath));
        await Assert.ThrowsAsync<InvalidStoragePathException>(() => _storage.DeleteAsync(maliciousPath, CancellationToken.None));
        await using (var staged = await _storage.StageAsync(new MemoryStream([1]), 10, CancellationToken.None))
        {
            await Assert.ThrowsAsync<InvalidStoragePathException>(() => staged.CommitAsync(maliciousPath, CancellationToken.None));
        }

        Assert.True(File.Exists(outside), "Files outside the storage root must never be touched.");
    }

    [Fact]
    public async Task Missing_Files_Are_Reported_Consistently()
    {
        var relativePath = NewRelativePath(".webp");

        Assert.Throws<StoredFileNotFoundException>(() => _storage.OpenRead(relativePath));
        Assert.False(await _storage.DeleteAsync(relativePath, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Removes_An_Existing_File()
    {
        var relativePath = NewRelativePath();
        await using (var staged = await _storage.StageAsync(new MemoryStream([1, 2]), 10, CancellationToken.None))
        {
            await staged.CommitAsync(relativePath, CancellationToken.None);
        }

        Assert.True(await _storage.DeleteAsync(relativePath, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, relativePath)));
    }

    [Fact]
    public void Relative_Root_Paths_Resolve_Against_The_Content_Root()
    {
        var resolved = LocalImageFileStorage.ResolveRootPath("../data/uploads", Path.Combine(_temp.Path, "app"));

        Assert.Equal(Path.Combine(_temp.Path, "data", "uploads"), resolved);
        Assert.Throws<InvalidOperationException>(() => LocalImageFileStorage.ResolveRootPath(" ", _temp.Path));
    }

    internal static LocalImageFileStorage CreateStorage(string rootPath, string contentRoot) =>
        new(Options.Create(new FileStorageOptions { RootPath = rootPath }), new TestHostEnvironment(contentRoot));

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "ImageStore.UnitTests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
