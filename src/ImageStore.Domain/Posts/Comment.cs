using ImageStore.Domain.Common;
using ImageStore.Domain.Social;

namespace ImageStore.Domain.Posts;

/// <summary>A comment on an image post, written by an authenticated user with access to the post.</summary>
public sealed class Comment
{
    private Comment()
    {
        // Required by EF Core.
    }

    public Guid Id { get; private set; }

    public Guid? ParentCommentId { get; private set; }

    public Guid PostId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>The author's profile, loaded on demand.</summary>
    public UserProfile? Author { get; private set; }

    public static Comment Create(Guid id, Guid postId, Guid authorUserId, string content, DateTime createdAtUtc, Guid? parentCommentId = null)
    {
        if (id == Guid.Empty)
        {
            throw new DomainValidationException("id", "A comment must have an identifier.");
        }

        if (postId == Guid.Empty)
        {
            throw new DomainValidationException("postId", "A comment must belong to a post.");
        }

        if (authorUserId == Guid.Empty)
        {
            throw new DomainValidationException("authorUserId", "A comment must have an author.");
        }

        return new Comment
        {
            Id = id,
            PostId = postId,
            ParentCommentId = parentCommentId,
            AuthorUserId = authorUserId,
            Content = NormalizeContent(content),
            CreatedAtUtc = EnsureUtc(createdAtUtc, "createdAtUtc"),
        };
    }

    public void UpdateContent(string content, DateTime updatedAtUtc)
    {
        var normalized = NormalizeContent(content);
        var timestamp = EnsureUtc(updatedAtUtc, "updatedAtUtc");

        Content = normalized;
        UpdatedAtUtc = timestamp;
    }

    private static string NormalizeContent(string? content)
    {
        var trimmed = content?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("content", "A comment cannot be empty.");
        }

        if (trimmed.Length > PostRules.CommentMaxLength)
        {
            throw new DomainValidationException(
                "content",
                $"A comment must be at most {PostRules.CommentMaxLength} characters long.");
        }

        return trimmed;
    }

    private static DateTime EnsureUtc(DateTime value, string propertyName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new DomainValidationException(propertyName, $"{propertyName} must be a UTC timestamp.");
}
