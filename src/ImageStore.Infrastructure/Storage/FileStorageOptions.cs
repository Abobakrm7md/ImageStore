namespace ImageStore.Infrastructure.Storage;

/// <summary>Bound from the <c>Storage</c> configuration section.</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Root directory for uploaded files. Relative paths are resolved against the application's
    /// content root. Must not be inside the web root; production should use an absolute path on a
    /// persistent volume.
    /// </summary>
    public string RootPath { get; set; } = "storage/uploads";
}

/// <summary>Bound from the <c>StorageMaintenance</c> configuration section.</summary>
public sealed class StorageMaintenanceOptions
{
    public const string SectionName = "StorageMaintenance";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Staging files older than this are leftovers of interrupted uploads and are deleted.</summary>
    public int StagingFileMaxAgeMinutes { get; set; } = 60;

    /// <summary>Report stored files that have no database record.</summary>
    public bool ScanForOrphanedFiles { get; set; } = true;

    /// <summary>
    /// Also delete orphaned files. Off by default: after restoring an older database backup,
    /// "orphans" may be the only remaining copy of an image.
    /// </summary>
    public bool DeleteOrphanedFiles { get; set; }

    /// <summary>Files younger than this are skipped (they may belong to an upload in progress).</summary>
    public int OrphanedFileMinAgeMinutes { get; set; } = 24 * 60;
}
