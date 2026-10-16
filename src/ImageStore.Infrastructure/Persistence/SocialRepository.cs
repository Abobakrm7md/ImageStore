using ImageStore.Application.Common;
using ImageStore.Application.Social;
using ImageStore.Domain.Social;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.Infrastructure.Persistence;

internal sealed class SocialRepository(ImageStoreDbContext dbContext) : ISocialRepository
{
    public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserProfiles.SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public Task<PagedResult<UserSummaryDto>> SearchProfilesAsync(
        Guid viewerUserId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.UserProfiles.AsNoTracking();

        if (!string.IsNullOrEmpty(search))
        {
            // User input is escaped so that %, _, [ and \ are matched literally.
            var pattern = $"%{ImageRepository.EscapeLikePattern(search)}%";
            query = query.Where(profile =>
                EF.Functions.Like(profile.UserName, pattern, ImageRepository.LikeEscapeCharacter)
                || EF.Functions.Like(profile.DisplayName, pattern, ImageRepository.LikeEscapeCharacter));
        }

        return PageAsync(
            query.OrderBy(profile => profile.UserName).ThenBy(profile => profile.UserId),
            viewerUserId,
            page,
            pageSize,
            cancellationToken);
    }

    public Task<bool> IsUserNameTakenAsync(string normalizedUserName, Guid excludeUserId, CancellationToken cancellationToken) =>
        dbContext.UserProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.NormalizedUserName == normalizedUserName && profile.UserId != excludeUserId, cancellationToken);

    public Task<bool> IsFollowingAsync(Guid followerUserId, Guid followedUserId, CancellationToken cancellationToken) =>
        dbContext.UserFollows
            .AsNoTracking()
            .AnyAsync(follow => follow.FollowerUserId == followerUserId && follow.FollowedUserId == followedUserId, cancellationToken);

    public Task<UserFollow?> FindFollowAsync(Guid followerUserId, Guid followedUserId, CancellationToken cancellationToken) =>
        dbContext.UserFollows.SingleOrDefaultAsync(
            follow => follow.FollowerUserId == followerUserId && follow.FollowedUserId == followedUserId,
            cancellationToken);

    public Task<PagedResult<UserSummaryDto>> ListFollowersAsync(
        Guid viewerUserId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        PageAsync(
            from follow in dbContext.UserFollows.AsNoTracking()
            join profile in dbContext.UserProfiles.AsNoTracking() on follow.FollowerUserId equals profile.UserId
            where follow.FollowedUserId == userId
            orderby follow.CreatedAtUtc descending, follow.FollowerUserId descending
            select profile,
            viewerUserId,
            page,
            pageSize,
            cancellationToken);

    public Task<PagedResult<UserSummaryDto>> ListFollowingAsync(
        Guid viewerUserId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        PageAsync(
            from follow in dbContext.UserFollows.AsNoTracking()
            join profile in dbContext.UserProfiles.AsNoTracking() on follow.FollowedUserId equals profile.UserId
            where follow.FollowerUserId == userId
            orderby follow.CreatedAtUtc descending, follow.FollowedUserId descending
            select profile,
            viewerUserId,
            page,
            pageSize,
            cancellationToken);

    public Task<int> CountFollowersAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserFollows.AsNoTracking().CountAsync(follow => follow.FollowedUserId == userId, cancellationToken);

    public Task<int> CountFollowingAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserFollows.AsNoTracking().CountAsync(follow => follow.FollowerUserId == userId, cancellationToken);

    public void Add(UserProfile profile) => dbContext.UserProfiles.Add(profile);

    public void Add(UserFollow follow) => dbContext.UserFollows.Add(follow);

    public void Remove(UserFollow follow) => dbContext.UserFollows.Remove(follow);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw new ConflictException("The change conflicts with a concurrent update. Please retry.", exception);
        }
    }

    /// <summary>
    /// Pages an ordered profile query and projects viewer-relative summaries (the "am I following
    /// them" flag is computed inside the database, never by loading all follows).
    /// </summary>
    private async Task<PagedResult<UserSummaryDto>> PageAsync(
        IQueryable<UserProfile> ordered,
        Guid viewerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await ordered.CountAsync(cancellationToken);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(profile => new
            {
                profile.UserId,
                profile.UserName,
                profile.DisplayName,
                HasProfilePicture = profile.ProfilePicturePath != null,
                IsFollowing = viewerUserId != profile.UserId
                    && dbContext.UserFollows.Any(follow =>
                        follow.FollowerUserId == viewerUserId && follow.FollowedUserId == profile.UserId),
            })
            .ToListAsync(cancellationToken);

        var summaries = items
            .Select(item => new UserSummaryDto(item.UserId, item.UserName, item.DisplayName, item.HasProfilePicture, item.IsFollowing))
            .ToList();
        return new PagedResult<UserSummaryDto>(summaries, page, pageSize, totalCount);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
