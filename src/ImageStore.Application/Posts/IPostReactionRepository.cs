using ImageStore.Domain.Posts;

namespace ImageStore.Application.Posts;

/// <summary>Persistence operations for reactions. Post-level access is checked by the caller.</summary>
public interface IPostReactionRepository
{
    /// <summary>The tracked reaction of one user for one post; <c>null</c> when there is none.</summary>
    Task<PostReaction?> FindUserReactionAsync(Guid postId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Reaction counts grouped by type (types without reactions are absent).</summary>
    Task<IReadOnlyDictionary<ReactionType, int>> CountByTypeAsync(Guid postId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid postId, CancellationToken cancellationToken);

    void Add(PostReaction reaction);

    void Remove(PostReaction reaction);

    /// <summary>
    /// Commits pending changes atomically. A violation of the unique (PostId, UserId) index –
    /// two concurrent requests adding a first reaction – becomes
    /// <see cref="Common.ConflictException"/> so the caller can retry as an update.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
