using ImageStore.Domain.Images;
using ImageStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImageStore.IntegrationTests.TestSupport;

/// <summary>
/// Hosts the real API in memory. Each factory instance gets its own in-memory database and its
/// own temporary storage directory, which is deleted when the factory is disposed.
/// SQL Server is replaced by the EF Core in-memory provider; SQL Server specifics (migrations,
/// query translation) are covered separately in <c>PersistenceTests</c>.
/// </summary>
public class ImageStoreApiFactory : WebApplicationFactory<Program>
{
    public const long MaxFileSizeBytes = 64 * 1024;
    public const int MaxFilesPerRequest = 3;

    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "imagestore-it", Guid.NewGuid().ToString("N"));

    public string StorageRoot => Path.Combine(_testRoot, "uploads");

    public FailingSaveChangesInterceptor SaveChangesFailure { get; } = new();

    /// <summary>Isolated web root, so tests never depend on a locally built SPA in src/ImageStore.Api/wwwroot.</summary>
    public string WebRoot => Path.Combine(_testRoot, "wwwroot");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(WebRoot);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "InMemory",
            ["Database:InMemoryDatabaseName"] = $"imagestore-it-{Guid.NewGuid():N}",
            ["Storage:RootPath"] = StorageRoot,
            ["ImageUpload:MaxFileSizeBytes"] = MaxFileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ImageUpload:MaxFilesPerRequest"] = MaxFilesPerRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:Authentication:PermitLimit"] = "100000",
            ["StorageMaintenance:Enabled"] = "false",
            ["Swagger:Enabled"] = "true",
        }));

        builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ImageStoreDbContext>(options => options.AddInterceptors(SaveChangesFailure)));
    }

    public async Task<T> WithDbContextAsync<T>(Func<ImageStoreDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ImageStoreDbContext>());
    }

    /// <summary>All stored image files (excluding the staging area).</summary>
    public string[] StoredFiles() =>
        Directory.Exists(StorageRoot)
            ? Directory.GetFiles(StorageRoot, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.staging{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToArray()
            : [];

    public string[] StagingFiles() =>
        Directory.Exists(Path.Combine(StorageRoot, ".staging")) ? Directory.GetFiles(Path.Combine(StorageRoot, ".staging")) : [];

    public string TestRoot => _testRoot;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}

/// <summary>Simulates a database failure while images are being inserted.</summary>
public sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
{
    public bool FailImageInserts { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (FailImageInserts && eventData.Context!.ChangeTracker.Entries<Image>().Any(entry => entry.State == EntityState.Added))
        {
            throw new DbUpdateException("Simulated database failure.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
