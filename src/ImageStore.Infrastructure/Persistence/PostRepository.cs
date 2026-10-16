using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using ImageStore.Domain.Posts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.Infrastructure.Persistence;

/// <summary>
/// Posts and the personalized feed. The access rule – a post is visible to its author and to
/// the followers of the author – is part of every query, evaluated by the database.
/// </summary>
internal sealed class PostRepository(ImageStoreDbContext dbContext) : IPostRepository
{
    public async Task<PostDetails?> GetAccessibleAsync(Guid postId, Guid viewerUserId, CancellationToken cancellationToken)
    {
        var post = await GetAccessiblePostAsync(postId, viewerUserId, cancellationToken);
        if (post is null)
        {
            return null;
        }

        var details = await AttachDetailsAsync([post], viewerUserId, cancellationToken);
        return details[0];
    }

    internal IQueryable<ImagePost> BuildAccessibleQuery(Guid viewerUserId) =>
        dbContext.ImagePosts.AsNoTracking().Where(post => post.AuthorUserId == viewerUserId
            || dbContext.UserFollows.Any(follow => follow.FollowerUserId == viewerUserId && follow.FollowedUserId == post.AuthorUserId));

    public Task<ImagePost?> GetAccessiblePostAsync(Guid postId, Guid viewerUserId, CancellationToken cancellationToken) =>
        BuildAccessibleQuery(viewerUserId)
            .Include(post => post.Image)
            .Include(post => post.Author)
            .SingleOrDefaultAsync(post => post.Id == postId, cancellationToken);

    public Task<ImagePost?> GetOwnedForUpdateAsync(Guid postId, Guid authorUserId, CancellationToken cancellationToken) =>
        dbContext.ImagePosts.SingleOrDefaultAsync(
            post => post.Id == postId && post.AuthorUserId == authorUserId,
            cancellationToken);

    public async Task<PagedResult<PostDetails>> ListFeedAsync(
        Guid viewerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Chronological feed of the users the viewer follows. Filtering and paging happen in the
        // database; the (CreatedAtUtc, Id) ordering is total, so pages never overlap or skip.
        var feed = dbContext.ImagePosts
            .AsNoTracking()
            .Where(post => dbContext.UserFollows.Any(follow =>
                follow.FollowerUserId == viewerUserId && follow.FollowedUserId == post.AuthorUserId));

        return await PageAsync(feed, viewerUserId, page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<PostDetails>> ListByAuthorAsync(
        Guid authorUserId,
        Guid viewerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var authored = dbContext.ImagePosts.AsNoTracking().Where(post => post.AuthorUserId == authorUserId);
        return await PageAsync(authored, viewerUserId, page, pageSize, cancellationToken);
    }

    public Task<int> CountByAuthorAsync(Guid authorUserId, CancellationToken cancellationToken) =>
        dbContext.ImagePosts.AsNoTracking().CountAsync(post => post.AuthorUserId == authorUserId, cancellationToken);

    public Task<bool> IsImagePublishedAsync(Guid imageId, CancellationToken cancellationToken) =>
        dbContext.ImagePosts.AsNoTracking().AnyAsync(post => post.ImageId == imageId, cancellationToken);

    public void Add(ImagePost post) => dbContext.ImagePosts.Add(post);

    public void Remove(ImagePost post) => dbContext.ImagePosts.Remove(post);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw new ConflictException("The change conflicts with a concurrent update. Please retry.", exception);
        }
    }

    /// <summary>Feed query, exposed so SQL translation can be verified without a running server.</summary>
    internal IQueryable<ImagePost> BuildFeedQuery(Guid viewerUserId) =>
        dbContext.ImagePosts
            .AsNoTracking()
            .Where(post => dbContext.UserFollows.Any(follow =>
                follow.FollowerUserId == viewerUserId && follow.FollowedUserId == post.AuthorUserId));

    private async Task<PagedResult<PostDetails>> PageAsync(
        IQueryable<ImagePost> filtered,
        Guid viewerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await filtered.CountAsync(cancellationToken);
        var posts = await filtered
            .OrderByDescending(post => post.CreatedAtUtc)
            .ThenByDescending(post => post.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(post => post.Image)
            .Include(post => post.Author)
            .ToListAsync(cancellationToken);

        var details = await AttachDetailsAsync(posts, viewerUserId, cancellationToken);
        return new PagedResult<PostDetails>(details, page, pageSize, totalCount);
    }

    /// <summary>
    /// Loads comment counts, reaction counts and the viewer's own reactions for one page of posts
    /// in three additional queries (no N+1), then assembles them in post order.
    /// </summary>
    private async Task<IReadOnlyList<PostDetails>> AttachDetailsAsync(
        IReadOnlyList<ImagePost> posts,
        Guid viewerUserId,
        CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var postIds = posts.Select(post => post.Id).ToList();

        var commentCounts = await dbContext.Comments
            .AsNoTracking()
            .Where(comment => postIds.Contains(comment.PostId))
            .GroupBy(comment => comment.PostId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Key, entry => entry.Count, cancellationToken);

        var reactionCounts = await dbContext.PostReactions
            .AsNoTracking()
            .Where(reaction => postIds.Contains(reaction.PostId))
            .GroupBy(reaction => new { reaction.PostId, reaction.Reaction })
            .Select(group => new { group.Key.PostId, group.Key.Reaction, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var myReactions = await dbContext.PostReactions
            .AsNoTracking()
            .Where(reaction => reaction.UserId == viewerUserId && postIds.Contains(reaction.PostId))
            .ToDictionaryAsync(reaction => reaction.PostId, reaction => reaction.Reaction, cancellationToken);

        var countsByPost = reactionCounts
            .GroupBy(entry => entry.PostId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<ReactionType, int>)group.ToDictionary(entry => entry.Reaction, entry => entry.Count));

        return posts
            .Select(post => new PostDetails(
                post,
                post.Author ?? throw new InvalidOperationException("The post was loaded without its author."),
                commentCounts.GetValueOrDefault(post.Id),
                countsByPost.GetValueOrDefault(post.Id) ?? EmptyCounts,
                myReactions.TryGetValue(post.Id, out var myReaction) ? myReaction : null))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<ReactionType, int> EmptyCounts =
        new Dictionary<ReactionType, int>();

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
