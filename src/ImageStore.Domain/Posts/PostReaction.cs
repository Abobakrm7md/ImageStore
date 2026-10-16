using ImageStore.Domain.Common;

namespace ImageStore.Domain.Posts;

/// <summary>
/// A user's reaction to an image post. Each user has at most one active reaction per post
/// (unique index on <c>(PostId, UserId)</c>); changing a reaction updates the row instead of
/// inserting a second one.
/// </summary>
public sealed class PostReaction
{
    private PostReaction()
    {
        // Required by EF Core.
    }

    public Guid Id { get; private set; }

    public Guid PostId { get; private set; }

    public Guid UserId { get; private set; }

    public ReactionType Reaction { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public static PostReaction Create(Guid id, Guid postId, Guid userId, ReactionType reaction, DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new DomainValidationException("id", "A reaction must have an identifier.");
        }

        if (postId == Guid.Empty)
        {
            throw new DomainValidationException("postId", "A reaction must belong to a post.");
        }

        if (userId == Guid.Empty)
        {
            throw new DomainValidationException("userId", "A reaction must have a user.");
        }

        if (!ReactionTypes.IsDefined(reaction))
        {
            throw new DomainValidationException("reaction", "Unknown reaction type.");
        }

        return new PostReaction
        {
            Id = id,
            PostId = postId,
            UserId = userId,
            Reaction = reaction,
            CreatedAtUtc = EnsureUtc(createdAtUtc, "createdAtUtc"),
        };
    }

    /// <summary>Switches to a different reaction type. Setting the same type again is a no-op.</summary>
    public void ChangeReaction(ReactionType reaction, DateTime updatedAtUtc)
    {
        if (!ReactionTypes.IsDefined(reaction))
        {
            throw new DomainValidationException("reaction", "Unknown reaction type.");
        }

        if (reaction == Reaction)
        {
            return;
        }

        Reaction = reaction;
        UpdatedAtUtc = EnsureUtc(updatedAtUtc, "updatedAtUtc");
    }

    private static DateTime EnsureUtc(DateTime value, string propertyName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new DomainValidationException(propertyName, $"{propertyName} must be a UTC timestamp.");
}
