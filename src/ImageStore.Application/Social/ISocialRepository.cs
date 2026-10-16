using ImageStore.Application.Common;
using ImageStore.Domain.Social;

namespace ImageStore.Application.Social;

/// <summary>
/// Persistence operations for profiles and follow relationships. Implementations translate
/// <see cref="ConflictException"/> for unique-key violations so callers can treat races uniformly.
/// </summary>
public interface ISocialRepository
{
    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Searches profiles by user name or display name (case-insensitive substring match).</summary>
    Task<PagedResult<UserSummaryDto>> SearchProfilesAsync(Guid viewerUserId, string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Whether a profile other than <paramref name="excludeUserId"/>'s own uses the user name.</summary>
    Task<bool> IsUserNameTakenAsync(string normalizedUserName, Guid excludeUserId, CancellationToken cancellationToken);

    Task<bool> IsFollowingAsync(Guid followerUserId, Guid followedUserId, CancellationToken cancellationToken);

    /// <summary>The tracked follow row, used for removal; <c>null</c> when the relationship does not exist.</summary>
    Task<UserFollow?> FindFollowAsync(Guid followerUserId, Guid followedUserId, CancellationToken cancellationToken);

    /// <summary>The users following <paramref name="userId"/>, newest follow first.</summary>
    Task<PagedResult<UserSummaryDto>> ListFollowersAsync(Guid viewerUserId, Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The users <paramref name="userId"/> follows, newest follow first.</summary>
    Task<PagedResult<UserSummaryDto>> ListFollowingAsync(Guid viewerUserId, Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountFollowersAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountFollowingAsync(Guid userId, CancellationToken cancellationToken);

    void Add(UserProfile profile);

    void Add(UserFollow follow);

    void Remove(UserFollow follow);

    /// <summary>Commits pending changes atomically. Unique-key violations become <see cref="ConflictException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
