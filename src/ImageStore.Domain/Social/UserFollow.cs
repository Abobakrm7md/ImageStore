using ImageStore.Domain.Common;

namespace ImageStore.Domain.Social;

/// <summary>
/// A directed follow relationship: <see cref="FollowerUserId"/> follows <see cref="FollowedUserId"/>.
/// The pair is the primary key, so a follow can never exist twice; following is immediate
/// (no request/approval workflow).
/// </summary>
public sealed class UserFollow
{
    private UserFollow()
    {
        // Required by EF Core.
    }

    public Guid FollowerUserId { get; private set; }

    public Guid FollowedUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static UserFollow Create(Guid followerUserId, Guid followedUserId, DateTime createdAtUtc)
    {
        if (followerUserId == Guid.Empty)
        {
            throw new DomainValidationException("followerUserId", "The follower is required.");
        }

        if (followedUserId == Guid.Empty)
        {
            throw new DomainValidationException("followedUserId", "The followed user is required.");
        }

        if (followerUserId == followedUserId)
        {
            throw new DomainValidationException("followedUserId", "You cannot follow yourself.");
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainValidationException("createdAtUtc", "createdAtUtc must be a UTC timestamp.");
        }

        return new UserFollow
        {
            FollowerUserId = followerUserId,
            FollowedUserId = followedUserId,
            CreatedAtUtc = createdAtUtc,
        };
    }
}
