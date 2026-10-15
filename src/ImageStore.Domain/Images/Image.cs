using ImageStore.Domain.Common;

namespace ImageStore.Domain.Images;

/// <summary>
/// Metadata for an image owned by a single user. The binary content lives on the file system;
/// this entity only stores the server-generated relative path to it.
/// </summary>
public sealed class Image
{
    private readonly List<Tag> _tags = [];

    private Image()
    {
        // Required by EF Core.
    }

    public Guid Id { get; private set; }

    /// <summary>The owning user. Always taken from the authenticated principal, never from input.</summary>
    public Guid OwnerUserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;

    public string StoredFileName { get; private set; } = string.Empty;

    /// <summary>Path relative to the configured storage root, e.g. <c>{userId}/{imageId}.jpg</c>.</summary>
    public string RelativeStoragePath { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long FileSizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<Tag> Tags => _tags;

    public static Image Create(
        Guid id,
        Guid ownerUserId,
        string title,
        string? description,
        ImageFileDetails file,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (id == Guid.Empty)
        {
            throw new DomainValidationException("id", "An image must have an identifier.");
        }

        if (ownerUserId == Guid.Empty)
        {
            throw new DomainValidationException("ownerUserId", "An image must have an owner.");
        }

        var image = new Image
        {
            Id = id,
            OwnerUserId = ownerUserId,
            Title = NormalizeTitle(title),
            Description = NormalizeDescription(description),
            OriginalFileName = RequireText(file.OriginalFileName, "originalFileName", ImageRules.OriginalFileNameMaxLength),
            StoredFileName = RequireText(file.StoredFileName, "storedFileName", ImageRules.StoredFileNameMaxLength),
            RelativeStoragePath = RequireText(file.RelativeStoragePath, "relativeStoragePath", ImageRules.RelativeStoragePathMaxLength),
            ContentType = RequireText(file.ContentType, "contentType", ImageRules.ContentTypeMaxLength),
            FileSizeBytes = file.FileSizeBytes > 0
                ? file.FileSizeBytes
                : throw new DomainValidationException("fileSizeBytes", "An image file cannot be empty."),
            Width = ValidateDimension(file.Width, "width"),
            Height = ValidateDimension(file.Height, "height"),
            CreatedAtUtc = EnsureUtc(createdAtUtc, "createdAtUtc"),
        };

        return image;
    }

    /// <summary>Updates the user-editable metadata. File-related fields are immutable.</summary>
    public void UpdateMetadata(string title, string? description, IReadOnlyCollection<Tag> tags, DateTime updatedAtUtc)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);
        var timestamp = EnsureUtc(updatedAtUtc, "updatedAtUtc");

        ReplaceTags(tags);
        Title = normalizedTitle;
        Description = normalizedDescription;
        UpdatedAtUtc = timestamp;
    }

    /// <summary>Replaces the tag set. Tags must belong to the image owner and be unique.</summary>
    public void ReplaceTags(IReadOnlyCollection<Tag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (tags.Count > ImageRules.MaxTagsPerImage)
        {
            throw new DomainValidationException("tags", $"An image can have at most {ImageRules.MaxTagsPerImage} tags.");
        }

        if (tags.Any(tag => tag.OwnerUserId != OwnerUserId))
        {
            throw new DomainValidationException("tags", "Tags must belong to the owner of the image.");
        }

        if (tags.Select(tag => tag.NormalizedName).Distinct(StringComparer.Ordinal).Count() != tags.Count)
        {
            throw new DomainValidationException("tags", "Tags must be unique.");
        }

        var requestedIds = tags.Select(tag => tag.Id).ToHashSet();
        _tags.RemoveAll(existing => !requestedIds.Contains(existing.Id));
        foreach (var tag in tags)
        {
            if (!_tags.Exists(existing => existing.Id == tag.Id))
            {
                _tags.Add(tag);
            }
        }
    }

    private static string NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("title", "Title is required.");
        }

        if (trimmed.Length > ImageRules.TitleMaxLength)
        {
            throw new DomainValidationException("title", $"Title must be at most {ImageRules.TitleMaxLength} characters long.");
        }

        return trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > ImageRules.DescriptionMaxLength)
        {
            throw new DomainValidationException(
                "description",
                $"Description must be at most {ImageRules.DescriptionMaxLength} characters long.");
        }

        return trimmed;
    }

    private static string RequireText(string? value, string propertyName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException(propertyName, $"{propertyName} is required.");
        }

        if (value.Length > maxLength)
        {
            throw new DomainValidationException(propertyName, $"{propertyName} must be at most {maxLength} characters long.");
        }

        return value;
    }

    private static int? ValidateDimension(int? value, string propertyName) =>
        value is null or > 0
            ? value
            : throw new DomainValidationException(propertyName, $"{propertyName} must be a positive number.");

    private static DateTime EnsureUtc(DateTime value, string propertyName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new DomainValidationException(propertyName, $"{propertyName} must be a UTC timestamp.");
}
