using ImageStore.Application.Common;
using ImageStore.Application.Images;

namespace ImageStore.Application.Social;

/// <summary>User profiles, search, following and profile pictures.</summary>
public interface ISocialService
{
    /// <summary>Any authenticated user may view any profile (profiles are public within the app).</summary>
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task<PagedResult<UserSummaryDto>> SearchUsersAsync(UserSearchQuery query, CancellationToken cancellationToken);

    /// <summary>Updates the calling user's display name and bio.</summary>
    Task<UserProfileDto> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Follows a user immediately (no approval workflow). Idempotent: following twice is not an
    /// error and never creates a duplicate relationship. Following yourself is rejected.
    /// </summary>
    Task FollowAsync(Guid followedUserId, CancellationToken cancellationToken);

    /// <summary>Stops following a user. Idempotent: succeeds even when not following.</summary>
    Task UnfollowAsync(Guid followedUserId, CancellationToken cancellationToken);

    Task<PagedResult<UserSummaryDto>> ListFollowersAsync(Guid userId, PagedQuery query, CancellationToken cancellationToken);

    Task<PagedResult<UserSummaryDto>> ListFollowingAsync(Guid userId, PagedQuery query, CancellationToken cancellationToken);

    /// <summary>Replaces the calling user's profile picture (validated JPEG/PNG/WebP, ≤ 2 MiB).</summary>
    Task<UserProfileDto> SetProfilePictureAsync(UploadFile file, CancellationToken cancellationToken);

    /// <summary>Removes the calling user's profile picture (succeeds even when there is none).</summary>
    Task RemoveProfilePictureAsync(CancellationToken cancellationToken);

    /// <summary>Profile pictures are visible to every authenticated user (avatars in feeds and lists).</summary>
    Task<ProfilePictureContent> GetProfilePictureAsync(Guid userId, CancellationToken cancellationToken);
}
