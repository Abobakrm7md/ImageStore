using ImageStore.Application.Images.Storage;
using ImageStore.Domain.Images;
using ImageStore.Infrastructure.Persistence;
using ImageStore.Infrastructure.Storage;
using ImageStore.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ImageStore.IntegrationTests;

public sealed class StorageMaintenanceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly LocalImageFileStorage _storage;
    private readonly ServiceProvider _services;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public StorageMaintenanceTests()
    {
        _storage = new LocalImageFileStorage(
            Options.Create(new FileStorageOptions { RootPath = Path.Combine(_temp.Path, "uploads") }),
            new Env(_temp.Path));

        var databaseName = $"maintenance-{Guid.NewGuid():N}";
        _services = new ServiceCollection()
            .AddDbContext<ImageStoreDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _temp.Dispose();
    }

    private StorageMaintenanceService CreateService(bool deleteOrphans) => new(
        _storage,
        _services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new StorageMaintenanceOptions
        {
            StagingFileMaxAgeMinutes = 60,
            ScanForOrphanedFiles = true,
            DeleteOrphanedFiles = deleteOrphans,
            OrphanedFileMinAgeMinutes = 60,
        }),
        _time,
        NullLogger<StorageMaintenanceService>.Instance);

    private async Task<string> StoreFileAsync(Guid owner, DateTime lastWriteUtc)
    {
        var relativePath = ImageStoragePaths.CreateRelativePath(owner, ImageStoragePaths.CreateStoredFileName(Guid.NewGuid(), ".png"));
        await using (var staged = await _storage.StageAsync(new MemoryStream(TestFiles.Read("sample.png")), 1024, CancellationToken.None))
        {
            await staged.CommitAsync(relativePath, CancellationToken.None);
        }

        File.SetLastWriteTimeUtc(Path.Combine(_storage.RootPath, relativePath), lastWriteUtc);
        return relativePath;
    }

    [Fact]
    public async Task Deletes_Only_Stale_Staging_Files()
    {
        var stale = Path.Combine(_storage.StagingPath, "stale.tmp");
        var fresh = Path.Combine(_storage.StagingPath, "fresh.tmp");
        await File.WriteAllBytesAsync(stale, [1]);
        await File.WriteAllBytesAsync(fresh, [1]);
        File.SetLastWriteTimeUtc(stale, _time.Now.UtcDateTime.AddHours(-2));

        var report = await CreateService(deleteOrphans: false).RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, report.StagingFilesDeleted);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh), "An upload in progress must not be disturbed.");
    }

    [Fact]
    public async Task Reports_Orphaned_Files_But_Only_Deletes_Them_When_Enabled()
    {
        var owner = Guid.NewGuid();
        var old = _time.Now.UtcDateTime.AddDays(-2);
        var known = await StoreFileAsync(owner, old);
        var orphan = await StoreFileAsync(owner, old);
        var recentOrphan = await StoreFileAsync(owner, _time.Now.UtcDateTime);

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImageStoreDbContext>();
            var id = Guid.Parse(Path.GetFileNameWithoutExtension(known));
            db.Images.Add(Image.Create(id, owner, "known", null, new ImageFileDetails("a.png", Path.GetFileName(known), known, "image/png", 99, 20, 10), DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        var reportOnly = await CreateService(deleteOrphans: false).RunOnceAsync(CancellationToken.None);
        Assert.Equal((1, 0), (reportOnly.OrphanedFilesFound, reportOnly.OrphanedFilesDeleted));
        Assert.True(File.Exists(Path.Combine(_storage.RootPath, orphan)));

        var cleanup = await CreateService(deleteOrphans: true).RunOnceAsync(CancellationToken.None);
        Assert.Equal((1, 1), (cleanup.OrphanedFilesFound, cleanup.OrphanedFilesDeleted));
        Assert.False(File.Exists(Path.Combine(_storage.RootPath, orphan)));
        Assert.True(File.Exists(Path.Combine(_storage.RootPath, known)), "Files with a database record are never touched.");
        Assert.True(File.Exists(Path.Combine(_storage.RootPath, recentOrphan)), "Recent files may belong to an upload in progress.");
    }

    private sealed class Env(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "ImageStore.IntegrationTests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
