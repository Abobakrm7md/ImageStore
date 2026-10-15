using System.Buffers;
using ImageStore.Application.Images.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ImageStore.Infrastructure.Storage;

/// <summary>
/// Stores images on the local file system under <c>{root}/{userId}/{imageId}.{ext}</c> and
/// profile pictures under <c>{root}/profiles/{userId}.{ext}</c>.
/// <list type="bullet">
/// <item>Uploads are streamed into <c>{root}/.staging</c> and only moved into place (an atomic
/// rename on the same volume) once complete and validated, so readers never see partial files.</item>
/// <item>Existing files are never overwritten.</item>
/// <item>Every relative path is checked against a strict allow-list and the resolved absolute
/// path must stay inside the root, which rules out path traversal even if the database were tampered with.</item>
/// </list>
/// </summary>
public sealed class LocalImageFileStorage : IImageFileStorage
{
    internal const string StagingDirectoryName = ".staging";
    private const int BufferSize = 81920;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _rootWithSeparator;

    public LocalImageFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        RootPath = ResolveRootPath(options.Value.RootPath, environment.ContentRootPath);
        StagingPath = Path.Combine(RootPath, StagingDirectoryName);
        _rootWithSeparator = Path.EndsInDirectorySeparator(RootPath) ? RootPath : RootPath + Path.DirectorySeparatorChar;

        CreateDirectory(RootPath);
        CreateDirectory(StagingPath);
    }

    /// <summary>Absolute storage root (server-side only; never returned to clients).</summary>
    public string RootPath { get; }

    internal string StagingPath { get; }

    public static string ResolveRootPath(string? configuredPath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("Storage:RootPath must be configured.");
        }

        var combined = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(contentRootPath, configuredPath);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(combined));
    }

    public async Task<IStagedImageFile> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        var stagingFile = Path.Combine(StagingPath, $"{Guid.NewGuid():N}.tmp");
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long total = 0;
        try
        {
            await using (var target = new FileStream(stagingFile, CreateFileOptions()))
            {
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new FileSizeLimitExceededException(maxBytes);
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await target.FlushAsync(cancellationToken);
                target.Flush(flushToDisk: true);
            }

            return new StagedImageFile(this, stagingFile, total);
        }
        catch
        {
            TryDeleteFile(stagingFile);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Stream OpenRead(string relativePath)
    {
        var fullPath = ResolveFullPath(relativePath);
        try
        {
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new StoredFileNotFoundException(relativePath, exception);
        }
    }

    public Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveFullPath(relativePath);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Validates a server-generated relative path (an image path or a profile-picture path) and
    /// resolves it to an absolute path that is guaranteed to be inside the storage root.
    /// </summary>
    internal string ResolveFullPath(string relativePath)
    {
        if (!ImageStoragePaths.IsValidRelativePath(relativePath) && !ProfilePictureStoragePaths.IsValidRelativePath(relativePath))
        {
            throw new InvalidStoragePathException("The storage path is not valid.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_rootWithSeparator, PathComparison) || IsInsideStaging(fullPath))
        {
            throw new InvalidStoragePathException("The storage path escapes the storage root.");
        }

        return fullPath;
    }

    /// <summary>Stored files (relative path + last write time) for consistency checks.</summary>
    internal IEnumerable<(string RelativePath, DateTime LastWriteTimeUtc)> EnumerateStoredFiles()
    {
        foreach (var directory in Directory.EnumerateDirectories(RootPath))
        {
            if (string.Equals(Path.GetFileName(directory), StagingDirectoryName, PathComparison))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var relativePath = Path.GetRelativePath(RootPath, file).Replace(Path.DirectorySeparatorChar, '/');
                if (ImageStoragePaths.IsValidRelativePath(relativePath))
                {
                    yield return (relativePath, File.GetLastWriteTimeUtc(file));
                }
            }
        }
    }

    internal IEnumerable<(string FullPath, DateTime LastWriteTimeUtc)> EnumerateStagingFiles() =>
        Directory.EnumerateFiles(StagingPath, "*.tmp").Select(file => (file, File.GetLastWriteTimeUtc(file)));

    internal static bool TryDeleteFile(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                return false;
            }

            File.Delete(fullPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool IsInsideStaging(string fullPath) =>
        fullPath.StartsWith(StagingPath + Path.DirectorySeparatorChar, PathComparison);

    private static FileStreamOptions CreateFileOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0, // Writes are already buffered by StageAsync.
            Options = FileOptions.Asynchronous,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        }

        return options;
    }

    private static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        }
    }

    private sealed class StagedImageFile(LocalImageFileStorage storage, string stagingFile, long length) : IStagedImageFile
    {
        private bool _committed;

        public long Length => length;

        public Stream OpenRead() =>
            new FileStream(stagingFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);

        public Task CommitAsync(string relativePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_committed, this);

            var destination = storage.ResolveFullPath(relativePath);
            CreateDirectory(Path.GetDirectoryName(destination)!);
            try
            {
                // Same-volume rename: atomic, and fails instead of replacing an existing file.
                File.Move(stagingFile, destination, overwrite: false);
            }
            catch (IOException exception) when (File.Exists(destination))
            {
                throw new StorageConflictException(relativePath, exception);
            }

            _committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                TryDeleteFile(stagingFile);
            }

            return ValueTask.CompletedTask;
        }
    }
}
