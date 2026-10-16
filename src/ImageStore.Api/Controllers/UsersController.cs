using ImageStore.Api.Contracts;
using ImageStore.Api.Security;
using ImageStore.Application.Common;
using ImageStore.Application.Images;
using ImageStore.Application.Posts;
using ImageStore.Application.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ImageStore.Api.Controllers;

/// <summary>
/// User profiles, search, following and profile pictures. Profiles, follower/following lists and
/// pictures are visible to every authenticated user; published posts are visible to the author
/// and their followers only (enforced by the application layer inside every query).
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
public sealed class UsersController(
    ISocialService socialService,
    IPostService postService,
    ICurrentUser currentUser) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    /// <summary>Searches users by user name or display name (paginated).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    public Task<PagedResult<UserSummaryDto>> Search([FromQuery] UserSearchQuery query, CancellationToken cancellationToken) =>
        socialService.SearchUsersAsync(query, cancellationToken);

    /// <summary>Returns the calling user's own profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<UserProfileDto> Me(CancellationToken cancellationToken) =>
        socialService.GetProfileAsync(currentUser.GetRequiredUserId(), cancellationToken);

    /// <summary>Updates the calling user's display name and bio.</summary>
    [HttpPut("me")]
    [Consumes("application/json")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    public Task<UserProfileDto> UpdateMe(UpdateMyProfileRequest request, CancellationToken cancellationToken) =>
        socialService.UpdateMyProfileAsync(request, cancellationToken);

    /// <summary>Replaces the calling user's profile picture (multipart/form-data, field <c>file</c>).</summary>
    [HttpPut("me/profile-picture")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    public async Task<ActionResult<UserProfileDto>> SetProfilePicture([FromForm] ProfilePictureForm form, CancellationToken cancellationToken)
    {
        if (form.File is null)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["file"] = ["Select a picture to upload."] }));
        }

        var upload = new UploadFile(form.File.FileName, form.File.Length, form.File.ContentType, form.File.OpenReadStream);
        return Ok(await socialService.SetProfilePictureAsync(upload, cancellationToken));
    }

    /// <summary>Removes the calling user's profile picture.</summary>
    [HttpDelete("me/profile-picture")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveProfilePicture(CancellationToken cancellationToken)
    {
        await socialService.RemoveProfilePictureAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Returns one user's profile, including counts and whether the caller follows them.</summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<UserProfileDto> Get(Guid userId, CancellationToken cancellationToken) =>
        socialService.GetProfileAsync(userId, cancellationToken);

    /// <summary>Streams a user's profile picture. <c>404</c> when they have none.</summary>
    [HttpGet("{userId:guid}/profile-picture")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> GetProfilePicture(Guid userId, CancellationToken cancellationToken)
    {
        var picture = await socialService.GetProfilePictureAsync(userId, cancellationToken);

        // Private content: never stored by shared caches and revalidated on every use.
        Response.Headers.CacheControl = "private, no-cache";
        return File(
            picture.Content,
            picture.ContentType,
            picture.LastModifiedUtc,
            new EntityTagHeaderValue(picture.EntityTag),
            enableRangeProcessing: false);
    }

    /// <summary>Published posts of one user, newest first. Requires the caller to be that user or a follower.</summary>
    [HttpGet("{userId:guid}/posts")]
    [ProducesResponseType<PagedResult<ImagePostDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<PagedResult<ImagePostDto>> Posts(Guid userId, [FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        postService.ListUserPostsAsync(userId, query, cancellationToken);

    /// <summary>Follows a user (immediate; idempotent). Following yourself is rejected with <c>400</c>.</summary>
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("SocialActivity")]
    [HttpPost("{userId:guid}/follow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Follow(Guid userId, CancellationToken cancellationToken)
    {
        await socialService.FollowAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Unfollows a user (idempotent). Their follower-only posts immediately become inaccessible.</summary>
    [HttpDelete("{userId:guid}/follow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unfollow(Guid userId, CancellationToken cancellationToken)
    {
        await socialService.UnfollowAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Users following this user, newest follow first (paginated).</summary>
    [HttpGet("{userId:guid}/followers")]
    [ProducesResponseType<PagedResult<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<PagedResult<UserSummaryDto>> Followers(Guid userId, [FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        socialService.ListFollowersAsync(userId, query, cancellationToken);

    /// <summary>Users this user follows, newest follow first (paginated).</summary>
    [HttpGet("{userId:guid}/following")]
    [ProducesResponseType<PagedResult<UserSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<PagedResult<UserSummaryDto>> Following(Guid userId, [FromQuery] PagedQuery query, CancellationToken cancellationToken) =>
        socialService.ListFollowingAsync(userId, query, cancellationToken);
}
