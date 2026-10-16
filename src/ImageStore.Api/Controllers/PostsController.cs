using ImageStore.Api.Security;
using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ImageStore.Api.Controllers;

/// <summary>
/// Image posts, their content, comments and reactions. A post is accessible to its author and to
/// the followers of the author; for everyone else it is indistinguishable from a missing post
/// (404). Editing and deleting are reserved to the author (comments additionally to the post
/// author as moderator, see <see cref="ICommentService"/>).
/// </summary>
[ApiController]
[Route("api/posts")]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
public sealed class PostsController(
    IPostService postService,
    ICommentService commentService,
    IReactionService reactionService) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    /// <summary>Publishes one of the caller's images as a post.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ImagePostDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<ImagePostDto>> Create(CreatePostRequest request, CancellationToken cancellationToken)
    {
        var post = await postService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { postId = post.Id }, post);
    }

    /// <summary>Returns one post with author, counts and the caller's reaction.</summary>
    [HttpGet("{postId:guid}")]
    [ProducesResponseType<ImagePostDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ImagePostDto> Get(Guid postId, CancellationToken cancellationToken) =>
        postService.GetAsync(postId, cancellationToken);

    /// <summary>Edits the title and description of the caller's post.</summary>
    [HttpPut("{postId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<ImagePostDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ImagePostDto> Update(Guid postId, UpdatePostRequest request, CancellationToken cancellationToken) =>
        postService.UpdateAsync(postId, request, cancellationToken);

    /// <summary>Deletes the caller's post with its comments and reactions; the image itself stays in the gallery.</summary>
    [HttpDelete("{postId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Delete(Guid postId, CancellationToken cancellationToken)
    {
        await postService.DeleteAsync(postId, cancellationToken);
        return NoContent();
    }

    /// <summary>Streams the post's image for display. Accessible to the author and followers only.</summary>
    [HttpGet("{postId:guid}/content")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> GetContent(Guid postId, CancellationToken cancellationToken)
    {
        var content = await postService.GetContentAsync(postId, cancellationToken);

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(content.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        SetPrivateCaching();

        return File(
            content.Content,
            content.ContentType,
            content.LastModifiedUtc,
            new EntityTagHeaderValue(content.EntityTag),
            enableRangeProcessing: true);
    }

    /// <summary>Downloads the post's image (<c>Content-Disposition: attachment</c>).</summary>
    [HttpGet("{postId:guid}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Download(Guid postId, CancellationToken cancellationToken)
    {
        var content = await postService.GetContentAsync(postId, cancellationToken);
        SetPrivateCaching();

        return File(
            content.Content,
            content.ContentType,
            content.FileName,
            content.LastModifiedUtc,
            new EntityTagHeaderValue(content.EntityTag),
            enableRangeProcessing: true);
    }

    /// <summary>Comments of a post, oldest first (paginated).</summary>
    [HttpGet("{postId:guid}/comments")]
    [ProducesResponseType<PagedResult<CommentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<PagedResult<CommentDto>> ListComments(Guid postId, [FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        commentService.ListAsync(postId, query, cancellationToken);

    [HttpGet("{postId:guid}/comments/{commentId:guid}")]
    public Task<CommentDto> GetComment(Guid postId, Guid commentId, CancellationToken cancellationToken) =>
        commentService.GetAsync(postId, commentId, cancellationToken);

    /// <summary>Adds a comment to an accessible post.</summary>
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("SocialActivity")]
    [HttpPost("{postId:guid}/comments")]
    [Consumes("application/json")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<CommentDto>> CreateComment(Guid postId, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        var comment = await commentService.CreateAsync(postId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, comment);
    }

    /// <summary>Edits the caller's own comment.</summary>
    [HttpPut("{postId:guid}/comments/{commentId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<CommentDto> UpdateComment(Guid postId, Guid commentId, UpdateCommentRequest request, CancellationToken cancellationToken) =>
        commentService.UpdateAsync(postId, commentId, request, cancellationToken);

    /// <summary>Deletes a comment: by its author, or by the post author (moderation).</summary>
    [HttpDelete("{postId:guid}/comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> DeleteComment(Guid postId, Guid commentId, CancellationToken cancellationToken)
    {
        await commentService.DeleteAsync(postId, commentId, cancellationToken);
        return NoContent();
    }

    /// <summary>Reaction summary of a post: total, per-type counts and the caller's reaction.</summary>
    [HttpGet("{postId:guid}/reactions")]
    [ProducesResponseType<ReactionSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ReactionSummaryDto> GetReactions(Guid postId, CancellationToken cancellationToken) =>
        reactionService.GetSummaryAsync(postId, cancellationToken);

    /// <summary>Sets or changes the caller's reaction (one active reaction per user and post).</summary>
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("SocialActivity")]
    [HttpPut("{postId:guid}/my-reaction")]
    [Consumes("application/json")]
    [ProducesResponseType<ReactionSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ReactionSummaryDto> SetMyReaction(Guid postId, SetReactionRequest request, CancellationToken cancellationToken) =>
        reactionService.SetReactionAsync(postId, request, cancellationToken);

    /// <summary>Removes the caller's reaction (idempotent).</summary>
    [HttpDelete("{postId:guid}/my-reaction")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> RemoveMyReaction(Guid postId, CancellationToken cancellationToken)
    {
        await reactionService.RemoveReactionAsync(postId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Private content: never stored by shared caches, and revalidated (cheaply, via ETag) on
    /// every use so the follow-based access check always runs.
    /// </summary>
    private void SetPrivateCaching() => Response.Headers.CacheControl = "private, no-cache";
}
