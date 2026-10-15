namespace ImageStore.Application.Images.Storage;

/// <summary>
/// Stores image binaries outside the database. Implementations must never accept arbitrary
/// paths: relative paths are server-generated (see <see cref="ImageStoragePaths"/>) and are
/// re-validated before any file-system access.
/// </summary>
public interface IImageFileStorage
{
    /// <summary>
    /// Streams <paramref name="content"/> into a private staging area that is not visible to
    /// readers. Throws <see cref="FileSizeLimitExceededException"/> as soon as more than
    /// <paramref name="maxBytes"/> bytes are received; partial data is removed.
    /// </summary>
    Task<IStagedImageFile> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stored file for reading. Throws <see cref="StoredFileNotFoundException"/> when the
    /// file does not exist and <see cref="InvalidStoragePathException"/> for malformed paths.
    /// </summary>
    Stream OpenRead(string relativePath);

    /// <summary>Deletes a stored file. Returns <c>false</c> when it did not exist.</summary>
    Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

/// <summary>
/// A fully received upload waiting in the staging area. Disposing an uncommitted staged file
/// deletes it, so failed or rejected uploads never leave data behind.
/// </summary>
public interface IStagedImageFile : IAsyncDisposable
{
    long Length { get; }

    /// <summary>Opens the staged content for validation (seekable stream).</summary>
    Stream OpenRead();

    /// <summary>
    /// Atomically moves the staged file to its final location. Never overwrites an existing file;
    /// throws <see cref="StorageConflictException"/> instead.
    /// </summary>
    Task CommitAsync(string relativePath, CancellationToken cancellationToken);
}
