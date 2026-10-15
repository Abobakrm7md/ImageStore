using ImageStore.Application.Common;

namespace ImageStore.Application.Posts;

/// <summary>
/// Comments on image posts. All operations require access to the post (author or follower).
/// Deletion policy: the comment author may delete their own comment, and the post author may
/// additionally moderate (delete) any comment on their post – but never edit someone else's.
/// </summary>
public interface ICommentService
{
    Task<PagedResult<CommentDto>> ListAsync(Guid postId, PagedQuery query, CancellationToken cancellationToken);

    Task<CommentDto> CreateAsync(Guid postId, CreateCommentRequest request, CancellationToken cancellationToken);

    Task<CommentDto> UpdateAsync(Guid postId, Guid commentId, UpdateCommentRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid postId, Guid commentId, CancellationToken cancellationToken);
}
