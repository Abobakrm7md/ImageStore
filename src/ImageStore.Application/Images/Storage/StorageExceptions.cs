namespace ImageStore.Application.Images.Storage;

/// <summary>An upload exceeded the configured maximum size while it was being received.</summary>
public sealed class FileSizeLimitExceededException : Exception
{
    public FileSizeLimitExceededException(long maxBytes)
        : base($"The file exceeds the maximum allowed size of {maxBytes} bytes.")
    {
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }
}

/// <summary>The metadata exists but the stored file is missing (storage inconsistency).</summary>
public sealed class StoredFileNotFoundException : Exception
{
    public StoredFileNotFoundException(string relativePath, Exception? innerException = null)
        : base("The stored image file could not be found.", innerException)
    {
        RelativePath = relativePath;
    }

    /// <summary>For server-side logging only – never return this value to clients.</summary>
    public string RelativePath { get; }
}

/// <summary>A storage path failed validation (for example a path-traversal attempt).</summary>
public sealed class InvalidStoragePathException : Exception
{
    public InvalidStoragePathException(string message)
        : base(message)
    {
    }
}

/// <summary>A file already exists at the target location; files are never overwritten.</summary>
public sealed class StorageConflictException : Exception
{
    public StorageConflictException(string relativePath, Exception? innerException = null)
        : base("A stored file already exists at the target location.", innerException)
    {
        RelativePath = relativePath;
    }

    public string RelativePath { get; }
}
