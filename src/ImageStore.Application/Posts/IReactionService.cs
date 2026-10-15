namespace ImageStore.Application.Posts;

/// <summary>
/// Reactions on image posts. All operations require access to the post (author or follower).
/// Each user has at most one active reaction per post: setting a reaction adds or replaces it,
/// removing deletes it (idempotently).
/// </summary>
public interface IReactionService
{
    /// <summary>Total count, per-type counts (all known types, zero included) and the caller's reaction.</summary>
    Task<ReactionSummaryDto> GetSummaryAsync(Guid postId, CancellationToken cancellationToken);

    /// <summary>Adds the caller's reaction or changes their existing one. Returns the new summary.</summary>
    Task<ReactionSummaryDto> SetReactionAsync(Guid postId, SetReactionRequest request, CancellationToken cancellationToken);

    /// <summary>Removes the caller's reaction. Succeeds even when they had none.</summary>
    Task RemoveReactionAsync(Guid postId, CancellationToken cancellationToken);
}
