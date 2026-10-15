using ImageStore.Application.Common;
using ImageStore.Application.Images.Storage;
using ImageStore.Application.Images.Validation;
using ImageStore.Domain.Images;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImageStore.Application.Images;

public sealed partial class ImageService(
    IImageRepository repository,
    IImageFileStorage storage,
    ICurrentUser currentUser,
    IOptions<ImageUploadOptions> uploadOptions,
    TimeProvider timeProvider,
    ILogger<ImageService> logger) : IImageService
{
    public async Task<PagedResult<ImageDto>> ListAsync(ImageListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var ownerUserId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        ImageRequestValidator.ValidateListQuery(query, errors);
        errors.ThrowIfAny();

        var sortBy = query.SortBy ?? ImageSortField.CreatedAt;
        var criteria = new ImageListCriteria(
            Page: query.Page ?? 1,
            PageSize: query.PageSize ?? ImageRequestValidator.DefaultPageSize,
            Search: string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            NormalizedTag: string.IsNullOrWhiteSpace(query.Tag) ? null : Tag.Normalize(query.Tag),
            SortBy: sortBy,
            SortDirection: query.SortDirection ?? (sortBy == ImageSortField.CreatedAt ? SortDirection.Desc : SortDirection.Asc));

        var page = await repository.ListOwnedAsync(ownerUserId, criteria, cancellationToken);
        return new PagedResult<ImageDto>(page.Items.Select(ToDto).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<ImageDto> GetAsync(Guid imageId, CancellationToken cancellationToken)
    {
        var ownerUserId = currentUser.GetRequiredUserId();
        var image = await repository.GetOwnedAsync(ownerUserId, imageId, cancellationToken)
            ?? throw new NotFoundException("Image");
        return ToDto(image);
    }

    public async Task<IReadOnlyList<ImageDto>> UploadAsync(UploadImagesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var ownerUserId = currentUser.GetRequiredUserId();
        var options = uploadOptions.Value;

        var errors = new ValidationErrors();
        if (command.Files.Count == 0)
        {
            errors.Add("files", "Select at least one image to upload.");
        }
        else if (command.Files.Count > options.MaxFilesPerRequest)
        {
            errors.Add("files", $"At most {options.MaxFilesPerRequest} files can be uploaded at once.");
        }

        ImageRequestValidator.ValidateMetadata(command.Title, titleRequired: false, command.Description, command.Tags, errors);
        errors.ThrowIfAny();

        var staged = new List<StagedUpload>(command.Files.Count);
        try
        {
            // Phase 1: receive and verify every file in the private staging area.
            for (var index = 0; index < command.Files.Count; index++)
            {
                var (upload, error) = await StageAndInspectAsync(command.Files[index], options, cancellationToken);
                if (upload is not null)
                {
                    staged.Add(upload);
                }
                else
                {
                    errors.Add($"files[{index}]", error!);
                }
            }

            errors.ThrowIfAny();

            // Phase 2: move the verified files into place and record their metadata.
            return await PersistAsync(ownerUserId, command, staged, cancellationToken);
        }
        finally
        {
            // Uncommitted staging files (rejected uploads, failures) are always removed.
            foreach (var upload in staged)
            {
                await upload.File.DisposeAsync();
            }
        }
    }

    public async Task<ImageDto> UpdateAsync(Guid imageId, UpdateImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerUserId = currentUser.GetRequiredUserId();

        var errors = new ValidationErrors();
        ImageRequestValidator.ValidateMetadata(request.Title, titleRequired: true, request.Description, request.Tags, errors);
        errors.ThrowIfAny();

        var image = await repository.GetOwnedForUpdateAsync(ownerUserId, imageId, cancellationToken)
            ?? throw new NotFoundException("Image");

        var previousTagIds = image.Tags.Select(tag => tag.Id).ToList();
        var tags = await ResolveTagsAsync(ownerUserId, request.Tags, cancellationToken);
        image.UpdateMetadata(request.Title!, request.Description, tags, timeProvider.GetUtcNow().UtcDateTime);

        var releasedTagIds = previousTagIds.Except(tags.Select(tag => tag.Id)).ToList();
        if (releasedTagIds.Count > 0)
        {
            var orphanedTags = await repository.FindTagsUsedOnlyByAsync(ownerUserId, imageId, releasedTagIds, cancellationToken);
            repository.RemoveTags(orphanedTags);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return ToDto(image);
    }

    public async Task DeleteAsync(Guid imageId, CancellationToken cancellationToken)
    {
        var ownerUserId = currentUser.GetRequiredUserId();
        var image = await repository.GetOwnedForUpdateAsync(ownerUserId, imageId, cancellationToken)
            ?? throw new NotFoundException("Image");

        var tagIds = image.Tags.Select(tag => tag.Id).ToList();
        var orphanedTags = await repository.FindTagsUsedOnlyByAsync(ownerUserId, imageId, tagIds, cancellationToken);
        repository.Remove(image);
        repository.RemoveTags(orphanedTags);

        // Database first: the file is only removed once the metadata delete has committed. If the
        // commit fails, the file is untouched and the image stays fully usable. The request token is
        // deliberately not forwarded so a client disconnect cannot interrupt the two-step operation.
        await repository.SaveChangesAsync(CancellationToken.None);

        try
        {
            if (!await storage.DeleteAsync(image.RelativeStoragePath, CancellationToken.None))
            {
                LogFileAlreadyMissing(logger, image.Id);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidStoragePathException)
        {
            // The metadata is gone, so the file is unreachable; storage maintenance reports it.
            LogOrphanedFileAfterDelete(logger, exception, image.Id);
        }
    }

    public async Task<ImageContent> GetContentAsync(Guid imageId, CancellationToken cancellationToken)
    {
        var ownerUserId = currentUser.GetRequiredUserId();
        var image = await repository.GetOwnedAsync(ownerUserId, imageId, cancellationToken)
            ?? throw new NotFoundException("Image");

        Stream content;
        try
        {
            content = storage.OpenRead(image.RelativeStoragePath);
        }
        catch (StoredFileNotFoundException)
        {
            LogStoredFileMissing(logger, image.Id);
            throw;
        }
        catch (InvalidStoragePathException)
        {
            LogInvalidStoragePath(logger, image.Id);
            throw;
        }

        return new ImageContent(
            content,
            image.ContentType,
            image.OriginalFileName,
            new DateTimeOffset(image.CreatedAtUtc, TimeSpan.Zero),
            $"\"{image.Id:N}\"");
    }

    public UploadPolicyDto GetUploadPolicy()
    {
        var options = uploadOptions.Value;
        return new UploadPolicyDto(
            options.MaxFileSizeBytes,
            options.MaxFilesPerRequest,
            [.. options.AllowedExtensions],
            [.. options.AllowedContentTypes],
            ImageRules.TitleMaxLength,
            ImageRules.DescriptionMaxLength,
            ImageRules.MaxTagsPerImage,
            ImageRules.TagNameMaxLength);
    }

    private async Task<(StagedUpload? Upload, string? Error)> StageAndInspectAsync(
        UploadFile file,
        ImageUploadOptions options,
        CancellationToken cancellationToken)
    {
        var originalFileName = FileNameSanitizer.Sanitize(file.FileName);
        var extension = FileNameSanitizer.GetExtension(originalFileName);
        var label = $"'{originalFileName}'";

        // Cheap checks first, before reading any content.
        if (!options.IsExtensionAllowed(extension))
        {
            return (null, $"{label}: file type '{(extension.Length == 0 ? "(none)" : extension)}' is not allowed. Allowed types: {string.Join(", ", options.AllowedExtensions)}.");
        }

        if (file.Length > options.MaxFileSizeBytes)
        {
            return (null, $"{label}: the file exceeds the maximum size of {FormatBytes(options.MaxFileSizeBytes)}.");
        }

        IStagedImageFile stagedFile;
        try
        {
            await using var source = file.OpenReadStream();
            stagedFile = await storage.StageAsync(source, options.MaxFileSizeBytes, cancellationToken);
        }
        catch (FileSizeLimitExceededException)
        {
            // The declared length can lie; the limit is enforced on the bytes actually received.
            return (null, $"{label}: the file exceeds the maximum size of {FormatBytes(options.MaxFileSizeBytes)}.");
        }

        var keep = false;
        try
        {
            if (stagedFile.Length == 0)
            {
                return (null, $"{label}: the file is empty.");
            }

            ImageInspectionResult inspection;
            await using (var content = stagedFile.OpenRead())
            {
                inspection = ImageFormatInspector.Inspect(content);
            }

            if (!inspection.IsValid)
            {
                return (null, $"{label}: {inspection.Error}");
            }

            var format = ImageFormats.Get(inspection.Format!.Value);
            if (!options.IsContentTypeAllowed(format.MimeType))
            {
                return (null, $"{label}: {format.MimeType} images are not allowed.");
            }

            if (!format.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return (null, $"{label}: the file content is {format.MimeType}, which does not match the '{extension}' extension.");
            }

            if ((long)inspection.Width * inspection.Height > options.MaxImagePixels)
            {
                return (null, $"{label}: the image dimensions ({inspection.Width}×{inspection.Height}) exceed the allowed maximum.");
            }

            keep = true;
            return (new StagedUpload(stagedFile, originalFileName, format, inspection.Width, inspection.Height), null);
        }
        finally
        {
            if (!keep)
            {
                await stagedFile.DisposeAsync();
            }
        }
    }

    private async Task<IReadOnlyList<ImageDto>> PersistAsync(
        Guid ownerUserId,
        UploadImagesCommand command,
        IReadOnlyList<StagedUpload> uploads,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tags = await ResolveTagsAsync(ownerUserId, command.Tags, cancellationToken);
        var images = new List<Image>(uploads.Count);
        var committedPaths = new List<string>(uploads.Count);

        try
        {
            foreach (var upload in uploads)
            {
                var imageId = Guid.CreateVersion7();
                var storedFileName = ImageStoragePaths.CreateStoredFileName(imageId, upload.Format.CanonicalExtension);
                var relativePath = ImageStoragePaths.CreateRelativePath(ownerUserId, storedFileName);
                var title = string.IsNullOrWhiteSpace(command.Title)
                    ? FileNameSanitizer.ToTitle(upload.OriginalFileName)
                    : command.Title;

                var image = Image.Create(
                    imageId,
                    ownerUserId,
                    title,
                    command.Description,
                    new ImageFileDetails(
                        upload.OriginalFileName,
                        storedFileName,
                        relativePath,
                        upload.Format.MimeType,
                        upload.File.Length,
                        upload.Width,
                        upload.Height),
                    now);
                image.ReplaceTags(tags);

                await upload.File.CommitAsync(relativePath, cancellationToken);
                committedPaths.Add(relativePath);
                images.Add(image);
                repository.Add(image);
            }

            // From here on the files are in place; finish the database write even if the client
            // disconnects, otherwise a cancelled commit could leave its outcome uncertain.
            await repository.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            await CompensateFailedUploadAsync(ownerUserId, images.Select(image => image.Id).ToList(), committedPaths, exception);
            throw;
        }

        LogImagesUploaded(logger, images.Count, ownerUserId);
        return images.Select(ToDto).ToList();
    }

    /// <summary>
    /// Removes files committed by an upload whose database write failed. If the database cannot
    /// confirm that the records are absent, the files are kept: an orphaned file is harmless and
    /// is reported by storage maintenance, whereas deleting the file of a committed record would
    /// break that image.
    /// </summary>
    private async Task CompensateFailedUploadAsync(
        Guid ownerUserId,
        IReadOnlyCollection<Guid> imageIds,
        IReadOnlyList<string> committedPaths,
        Exception failure)
    {
        if (committedPaths.Count == 0)
        {
            return;
        }

        bool recordsExist;
        try
        {
            recordsExist = imageIds.Count > 0 && await repository.AnyExistAsync(ownerUserId, imageIds, CancellationToken.None);
        }
        catch (Exception verificationFailure)
        {
            LogCompensationSkipped(logger, verificationFailure, committedPaths.Count);
            return;
        }

        if (recordsExist)
        {
            LogUploadCommittedDespiteError(logger, failure, committedPaths.Count);
            return;
        }

        foreach (var path in committedPaths)
        {
            try
            {
                await storage.DeleteAsync(path, CancellationToken.None);
            }
            catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
            {
                LogCleanupFailed(logger, cleanupFailure);
            }
        }

        LogUploadRolledBack(logger, failure, committedPaths.Count);
    }

    private Task<IReadOnlyList<Tag>> ResolveTagsAsync(
        Guid ownerUserId,
        IReadOnlyList<string>? requestedTags,
        CancellationToken cancellationToken)
    {
        var names = (requestedTags ?? [])
            .Select(Tag.Clean)
            .Where(name => name.Length > 0)
            .DistinctBy(Tag.Normalize, StringComparer.Ordinal)
            .ToList();

        return names.Count == 0
            ? Task.FromResult<IReadOnlyList<Tag>>([])
            : repository.GetOrCreateTagsAsync(ownerUserId, names, cancellationToken);
    }

    internal static ImageDto ToDto(Image image) => new(
        image.Id,
        image.Title,
        image.Description,
        image.OriginalFileName,
        image.ContentType,
        image.FileSizeBytes,
        image.Width,
        image.Height,
        image.Tags.Select(tag => tag.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(),
        image.CreatedAtUtc,
        image.UpdatedAtUtc);

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} bytes",
    };

    private sealed record StagedUpload(
        IStagedImageFile File,
        string OriginalFileName,
        ImageFormatDescriptor Format,
        int Width,
        int Height);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored {Count} uploaded image(s) for user {UserId}.")]
    private static partial void LogImagesUploaded(ILogger logger, int count, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload failed; removed {Count} newly stored file(s).")]
    private static partial void LogUploadRolledBack(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upload reported an error but its {Count} record(s) were committed; files kept.")]
    private static partial void LogUploadCommittedDespiteError(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not verify the outcome of a failed upload; kept {Count} file(s) for storage maintenance to reconcile.")]
    private static partial void LogCompensationSkipped(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to remove a file while rolling back an upload.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Image {ImageId} was deleted but its file was already missing.")]
    private static partial void LogFileAlreadyMissing(ILogger logger, Guid imageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Image {ImageId} was deleted but its file could not be removed; it is now orphaned.")]
    private static partial void LogOrphanedFileAfterDelete(ILogger logger, Exception exception, Guid imageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The file for image {ImageId} is missing from storage.")]
    private static partial void LogStoredFileMissing(ILogger logger, Guid imageId);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Image {ImageId} has an invalid storage path; possible data tampering.")]
    private static partial void LogInvalidStoragePath(ILogger logger, Guid imageId);
}
