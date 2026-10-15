using ImageStore.Application.Common;
using ImageStore.Domain.Posts;

namespace ImageStore.Application.Posts;

/// <summary>Persistence operations for comments. Post-level access is checked by the caller.</summary>
public interface ICommentRepository
{
    /// <summary>Comments of a post, oldest first, with author profiles, paginated.</summary>
    Task<PagedResult<Comment>> ListAsync(Guid postId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One comment (with author profile) of a post; <c>null</c> when it does not exist there.</summary>
    Task<Comment?> GetAsync(Guid postId, Guid commentId, CancellationToken cancellationToken);

    /// <summary>The tracked comment for modification.</summary>
    Task<Comment?> GetForUpdateAsync(Guid postId, Guid commentId, CancellationToken cancellationToken);

    void Add(Comment comment);

    void Remove(Comment comment);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
