using ImageStore.Api.Security;
using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImageStore.Api.Controllers;

/// <summary>
/// The personalized feed: posts of the users the caller follows, newest first, paginated by the
/// server. Follow relationships are applied inside the database query – the feed never contains
/// posts of unfollowed users, and unfollowing takes effect immediately.
/// </summary>
[ApiController]
[Route("api/feed")]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
public sealed class FeedController(IPostService postService) : ControllerBase
{
    /// <summary>Returns one page of the feed (chronological, no ranking).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ImagePostDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<PagedResult<ImagePostDto>> Get([FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        postService.GetFeedAsync(query, cancellationToken);
}
