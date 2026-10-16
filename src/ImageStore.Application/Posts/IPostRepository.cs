using ImageStore.Application.Common;
using ImageStore.Domain.Posts;

namespace ImageStore.Application.Posts;

/// <summary>
/// Persistence operations for image posts and the personalized feed. Access rules ("author or
/// follower") are applied inside the database queries, never after materialization.
/// </summary>
public interface IPostRepository
{
    /// <summary>
    /// The post with author profile, image, counts and the viewer's reaction – but only when the
    /// viewer is the author or follows the author; otherwise <c>null</c>.
    /// </summary>
    Task<PostDetails?> GetAccessibleAsync(Guid postId, Guid viewerUserId, CancellationToken cancellationToken);

    /// <summary>Lightweight access check that returns the post and its image (no counts).</summary>
    Task<ImagePost?> GetAccessiblePostAsync(Guid postId, Guid viewerUserId, CancellationToken cancellationToken);

    /// <summary>The tracked post for modification, only when <paramref name="authorUserId"/> wrote it.</summary>
    Task<ImagePost?> GetOwnedForUpdateAsync(Guid postId, Guid authorUserId, CancellationToken cancellationToken);

    /// <summary>Posts of followed users, newest first, with stable pagination (Id tiebreaker).</summary>
    Task<PagedResult<PostDetails>> ListFeedAsync(Guid viewerUserId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Posts of one author, newest first.</summary>
    Task<PagedResult<PostDetails>> ListByAuthorAsync(Guid authorUserId, Guid viewerUserId, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountByAuthorAsync(Guid authorUserId, CancellationToken cancellationToken);

    /// <summary>Whether the image is already published as a post (one post per image).</summary>
    Task<bool> IsImagePublishedAsync(Guid imageId, CancellationToken cancellationToken);

    void Add(ImagePost post);

    /// <summary>Marks the post for deletion; comments and reactions cascade with it.</summary>
    void Remove(ImagePost post);

    /// <summary>Commits pending changes atomically. Unique-key violations become <see cref="ConflictException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
