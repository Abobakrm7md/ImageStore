using ImageStore.Application.Common;
using ImageStore.Application.Images;

namespace ImageStore.Application.Posts;

/// <summary>
/// Image posts and the personalized feed. A post is accessible to its author and to the users
/// following the author; every other caller receives the same 404 as for a missing post.
/// </summary>
public interface IPostService
{
    /// <summary>Publishes one of the calling user's images (one active post per image).</summary>
    Task<ImagePostDto> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken);

    /// <summary>Edits the calling user's post.</summary>
    Task<ImagePostDto> UpdateAsync(Guid postId, UpdatePostRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes the calling user's post (comments and reactions go with it; the image stays).</summary>
    Task DeleteAsync(Guid postId, CancellationToken cancellationToken);

    Task<ImagePostDto> GetAsync(Guid postId, CancellationToken cancellationToken);

    /// <summary>The calling user's personalized feed: posts of followed users, newest first.</summary>
    Task<PagedResult<ImagePostDto>> GetFeedAsync(PagedQuery query, CancellationToken cancellationToken);

    /// <summary>Published posts of one user; requires the caller to be that user or a follower.</summary>
    Task<PagedResult<ImagePostDto>> ListUserPostsAsync(Guid userId, PagedQuery query, CancellationToken cancellationToken);

    /// <summary>Streams the post's image after the access check (display or download).</summary>
    Task<ImageContent> GetContentAsync(Guid postId, CancellationToken cancellationToken);
}
