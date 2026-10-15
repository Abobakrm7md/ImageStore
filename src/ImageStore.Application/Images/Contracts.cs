namespace ImageStore.Application.Images;

/// <summary>Image metadata returned by the API. Never contains file-system paths.</summary>
/// <param name="Id">The image identifier.</param>
/// <param name="Title">User-editable title.</param>
/// <param name="Description">Optional user-editable description.</param>
/// <param name="OriginalFileName">The sanitized name of the uploaded file (metadata only).</param>
/// <param name="ContentType">MIME type detected from the file signature.</param>
/// <param name="FileSizeBytes">File size in bytes.</param>
/// <param name="Width">Pixel width, when it could be determined.</param>
/// <param name="Height">Pixel height, when it could be determined.</param>
/// <param name="Tags">Tags attached to the image, ordered alphabetically.</param>
/// <param name="CreatedAtUtc">Upload timestamp (UTC).</param>
/// <param name="UpdatedAtUtc">Last metadata update (UTC), if any.</param>
public sealed record ImageDto(
    Guid Id,
    string Title,
    string? Description,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    int? Width,
    int? Height,
    IReadOnlyList<string> Tags,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>Sortable fields for the image list.</summary>
public enum ImageSortField
{
    CreatedAt,
    Title,
    FileSize,
}

public enum SortDirection
{
    Asc,
    Desc,
}

/// <summary>Query-string parameters for listing images.</summary>
public sealed class ImageListQuery
{
    /// <summary>1-based page number (default 1).</summary>
    public int? Page { get; set; }

    /// <summary>Items per page, 1-100 (default 12).</summary>
    public int? PageSize { get; set; }

    /// <summary>Case-insensitive text matched against title and description.</summary>
    public string? Search { get; set; }

    /// <summary>Only return images that have this tag.</summary>
    public string? Tag { get; set; }

    /// <summary>Sort field (default <c>createdAt</c>).</summary>
    public ImageSortField? SortBy { get; set; }

    /// <summary>Sort direction (default <c>desc</c> for dates, <c>asc</c> otherwise).</summary>
    public SortDirection? SortDirection { get; set; }
}

/// <summary>Normalized listing criteria handed to the repository.</summary>
public sealed record ImageListCriteria(
    int Page,
    int PageSize,
    string? Search,
    string? NormalizedTag,
    ImageSortField SortBy,
    SortDirection SortDirection);

/// <summary>Editable image metadata. File content and ownership cannot be changed.</summary>
/// <param name="Title">Required title.</param>
/// <param name="Description">Optional description; empty clears it.</param>
/// <param name="Tags">Complete replacement tag list (omit or empty to remove all tags).</param>
public sealed record UpdateImageRequest(string? Title, string? Description, IReadOnlyList<string>? Tags);

/// <summary>A file received from the client. <see cref="DeclaredContentType"/> is informational only.</summary>
public sealed record UploadFile(string FileName, long Length, string? DeclaredContentType, Func<Stream> OpenReadStream);

/// <summary>Upload one or more files that share the same optional metadata.</summary>
public sealed record UploadImagesCommand(
    IReadOnlyList<UploadFile> Files,
    string? Title,
    string? Description,
    IReadOnlyList<string>? Tags);

/// <summary>The content of a stored image, ready to be streamed to the client.</summary>
public sealed record ImageContent(
    Stream Content,
    string ContentType,
    string FileName,
    DateTimeOffset LastModifiedUtc,
    string EntityTag);

/// <summary>Upload limits and metadata rules, exposed so clients can validate before uploading.</summary>
/// <param name="MaxFileSizeBytes">Maximum size of a single file.</param>
/// <param name="MaxFilesPerRequest">Maximum number of files per upload request.</param>
/// <param name="AllowedExtensions">Accepted file extensions.</param>
/// <param name="AllowedContentTypes">Accepted MIME types (verified against the file signature).</param>
/// <param name="TitleMaxLength">Maximum title length.</param>
/// <param name="DescriptionMaxLength">Maximum description length.</param>
/// <param name="MaxTagsPerImage">Maximum number of tags per image.</param>
/// <param name="TagMaxLength">Maximum length of a tag.</param>
public sealed record UploadPolicyDto(
    long MaxFileSizeBytes,
    int MaxFilesPerRequest,
    IReadOnlyList<string> AllowedExtensions,
    IReadOnlyList<string> AllowedContentTypes,
    int TitleMaxLength,
    int DescriptionMaxLength,
    int MaxTagsPerImage,
    int TagMaxLength);
