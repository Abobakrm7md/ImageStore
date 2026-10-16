using ImageStore.Domain.Common;
using ImageStore.Domain.Images;
using ImageStore.Domain.Social;

namespace ImageStore.Domain.Posts;

/// <summary>
/// A social post that publishes one of the author's images to their followers. The post is
/// deliberately separate from <see cref="Image"/>: the image keeps its file metadata and stays
/// in the owner's private gallery, while the post carries the social side (title, description,
/// comments, reactions). One image can be published as at most one post (unique index); deleting
/// the image removes the post, deleting the post only unpublishes the image.
/// </summary>
public sealed class ImagePost
{
    private ImagePost()
    {
        // Required by EF Core.
    }

    public Guid Id { get; private set; }

    /// <summary>The publishing user. Always taken from the authenticated principal.</summary>
    public Guid AuthorUserId { get; private set; }

    /// <summary>The published image, owned by the author.</summary>
    public Guid ImageId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Loaded on demand (never null after a repository read that includes it).</summary>
    public Image? Image { get; private set; }

    /// <summary>The author's profile, loaded on demand.</summary>
    public UserProfile? Author { get; private set; }

    public static ImagePost Create(
        Guid id,
        Guid authorUserId,
        Guid imageId,
        string title,
        string? description,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new DomainValidationException("id", "A post must have an identifier.");
        }

        if (authorUserId == Guid.Empty)
        {
            throw new DomainValidationException("authorUserId", "A post must have an author.");
        }

        if (imageId == Guid.Empty)
        {
            throw new DomainValidationException("imageId", "A post must reference an image.");
        }

        return new ImagePost
        {
            Id = id,
            AuthorUserId = authorUserId,
            ImageId = imageId,
            Title = NormalizeTitle(title),
            Description = NormalizeDescription(description),
            CreatedAtUtc = EnsureUtc(createdAtUtc, "createdAtUtc"),
        };
    }

    /// <summary>Updates the post text. The image and the author can never be changed.</summary>
    public void Update(string title, string? description, DateTime updatedAtUtc)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);
        var timestamp = EnsureUtc(updatedAtUtc, "updatedAtUtc");

        Title = normalizedTitle;
        Description = normalizedDescription;
        UpdatedAtUtc = timestamp;
    }

    private static string NormalizeTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("title", "Title is required.");
        }

        if (trimmed.Length > PostRules.TitleMaxLength)
        {
            throw new DomainValidationException("title", $"Title must be at most {PostRules.TitleMaxLength} characters long.");
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

        if (trimmed.Length > PostRules.DescriptionMaxLength)
        {
            throw new DomainValidationException(
                "description",
                $"Description must be at most {PostRules.DescriptionMaxLength} characters long.");
        }

        return trimmed;
    }

    private static DateTime EnsureUtc(DateTime value, string propertyName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new DomainValidationException(propertyName, $"{propertyName} must be a UTC timestamp.");
}
