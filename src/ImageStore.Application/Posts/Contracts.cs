using ImageStore.Domain.Posts;
using ImageStore.Domain.Social;

namespace ImageStore.Application.Posts;

/// <summary>The author of a post or comment, as needed to render it.</summary>
public sealed record PostAuthorDto(
    Guid Id,
    string UserName,
    string DisplayName,
    bool HasProfilePicture);

/// <summary>The published image of a post. Bytes are served by <c>GET /api/posts/{id}/content</c>.</summary>
public sealed record PostImageDto(
    Guid ImageId,
    string ContentType,
    int? Width,
    int? Height);

/// <summary>Number of reactions of one type.</summary>
public sealed record ReactionCountDto(ReactionType Reaction, int Count);

/// <summary>An image post with everything needed to render it in a feed or on a profile.</summary>
/// <param name="CommentCount">Total number of comments.</param>
/// <param name="ReactionCount">Total number of reactions.</param>
/// <param name="ReactionCounts">Counts for every known reaction type (zero included).</param>
/// <param name="MyReaction">The calling user's current reaction, if any.</param>
/// <param name="IsMine">Whether the calling user is the author (enables edit/delete in the UI).</param>
public sealed record ImagePostDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    PostAuthorDto Author,
    PostImageDto Image,
    int CommentCount,
    int ReactionCount,
    IReadOnlyList<ReactionCountDto> ReactionCounts,
    ReactionType? MyReaction,
    bool IsMine);

/// <summary>Publishes one of the calling user's images as a post.</summary>
/// <param name="ImageId">An image owned by the calling user that is not published yet.</param>
/// <param name="Title">Optional post title; defaults to the image title (≤ 200 characters).</param>
/// <param name="Description">Optional description (≤ 2000 characters).</param>
public sealed record CreatePostRequest(Guid? ImageId, string? Title, string? Description);

/// <summary>Edits the text of a post. The image cannot be replaced.</summary>
public sealed record UpdatePostRequest(string? Title, string? Description);

/// <summary>A comment on a post.</summary>
/// <param name="IsMine">Whether the calling user wrote the comment (enables edit/delete).</param>
/// <param name="CanDelete">Whether the calling user may delete it (author or post owner).</param>
public sealed record CommentDto(
    Guid Id,
    Guid PostId,
    PostAuthorDto Author,
    string Content,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    bool IsMine,
    bool CanDelete);

/// <summary>Adds a comment. Empty or whitespace-only content is rejected.</summary>
public sealed record CreateCommentRequest(string? Content);

/// <summary>Replaces the content of a comment.</summary>
public sealed record UpdateCommentRequest(string? Content);

/// <summary>Aggregated reaction state of a post for the calling user.</summary>
public sealed record ReactionSummaryDto(
    int TotalCount,
    IReadOnlyList<ReactionCountDto> Counts,
    ReactionType? MyReaction);

/// <summary>Sets or changes the calling user's reaction.</summary>
/// <param name="Reaction">One of <c>like</c>, <c>love</c>, <c>laugh</c>, <c>wow</c>, <c>sad</c>, <c>dislike</c>.</param>
public sealed record SetReactionRequest(string? Reaction);

/// <summary>
/// A post with the related data a response needs, assembled by the repository in a few queries.
/// </summary>
public sealed record PostDetails(
    ImagePost Post,
    UserProfile Author,
    int CommentCount,
    IReadOnlyDictionary<ReactionType, int> ReactionCounts,
    ReactionType? MyReaction);
