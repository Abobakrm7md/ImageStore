namespace ImageStore.Infrastructure.Persistence;

public enum DatabaseProvider
{
    /// <summary>SQL Server (the production database).</summary>
    SqlServer,

    /// <summary>
    /// Non-persistent EF Core in-memory store for trying the app without SQL Server.
    /// Refused outside the Development and Testing environments.
    /// </summary>
    InMemory,
}

/// <summary>Bound from the <c>Database</c> configuration section.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string ConnectionStringName = "ImageStore";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;

    /// <summary>Apply pending EF Core migrations at start-up (recommended for development only).</summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    public string InMemoryDatabaseName { get; set; } = "ImageStore";
}
