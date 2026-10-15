using ImageStore.Application.Common;

namespace ImageStore.Application.Social;

/// <summary>A user as shown in lists (search results, followers, following).</summary>
/// <param name="Id">The user identifier.</param>
/// <param name="UserName">Unique user name.</param>
/// <param name="DisplayName">Free-text display name.</param>
/// <param name="HasProfilePicture">Whether <c>GET /api/users/{id}/profile-picture</c> returns an image.</param>
/// <param name="IsFollowing">Whether the calling user follows this user (viewer-relative).</param>
public sealed record UserSummaryDto(
    Guid Id,
    string UserName,
    string DisplayName,
    bool HasProfilePicture,
    bool IsFollowing);

/// <summary>A full user profile as shown on the profile page.</summary>
/// <param name="FollowerCount">Number of users following this user.</param>
/// <param name="FollowingCount">Number of users this user follows.</param>
/// <param name="PostCount">Number of published image posts (only meaningful when the caller may see them).</param>
/// <param name="IsFollowing">Whether the calling user follows this user.</param>
/// <param name="IsCurrentUser">Whether the profile belongs to the calling user.</param>
public sealed record UserProfileDto(
    Guid Id,
    string UserName,
    string DisplayName,
    string? Bio,
    bool HasProfilePicture,
    int FollowerCount,
    int FollowingCount,
    int PostCount,
    bool IsFollowing,
    bool IsCurrentUser,
    DateTime CreatedAtUtc);

/// <summary>Edits the calling user's profile. The user name cannot be changed.</summary>
/// <param name="DisplayName">Required display name (≤ 100 characters).</param>
/// <param name="Bio">Optional biography (≤ 500 characters); empty clears it.</param>
public sealed record UpdateMyProfileRequest(string? DisplayName, string? Bio);

/// <summary>Query-string parameters for searching users.</summary>
public sealed class UserSearchQuery : PagedQuery
{
    /// <summary>Case-insensitive prefix/substring match on user name and display name.</summary>
    public string? Search { get; set; }
}

/// <summary>The content of a stored profile picture, ready to be streamed to the client.</summary>
public sealed record ProfilePictureContent(
    Stream Content,
    string ContentType,
    DateTimeOffset LastModifiedUtc,
    string EntityTag);
