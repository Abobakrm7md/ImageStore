using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageStore.Infrastructure.Persistence;

public static partial class DatabaseInitializer
{
    /// <summary>
    /// Prepares the database at start-up: applies migrations when enabled (SQL Server) or creates
    /// the schema of the development in-memory store.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var dbContext = scope.ServiceProvider.GetRequiredService<ImageStoreDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (!dbContext.Database.IsRelational())
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            LogInMemoryDatabase(logger);
            return;
        }

        if (options.ApplyMigrationsOnStartup)
        {
            LogApplyingMigrations(logger);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying pending database migrations.")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Using the non-persistent in-memory database. Data is lost when the application stops.")]
    private static partial void LogInMemoryDatabase(ILogger logger);
}
