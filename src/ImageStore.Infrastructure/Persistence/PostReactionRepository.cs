using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using ImageStore.Domain.Posts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.Infrastructure.Persistence;

internal sealed class PostReactionRepository(ImageStoreDbContext dbContext) : IPostReactionRepository
{
    public Task<PostReaction?> FindUserReactionAsync(Guid postId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.PostReactions.SingleOrDefaultAsync(
            reaction => reaction.PostId == postId && reaction.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyDictionary<ReactionType, int>> CountByTypeAsync(Guid postId, CancellationToken cancellationToken)
    {
        var counts = await dbContext.PostReactions
            .AsNoTracking()
            .Where(reaction => reaction.PostId == postId)
            .GroupBy(reaction => reaction.Reaction)
            .Select(group => new { Reaction = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(entry => entry.Reaction, entry => entry.Count);
    }

    public Task<int> CountAsync(Guid postId, CancellationToken cancellationToken) =>
        dbContext.PostReactions.AsNoTracking().CountAsync(reaction => reaction.PostId == postId, cancellationToken);

    public void Add(PostReaction reaction) => dbContext.PostReactions.Add(reaction);

    public void Remove(PostReaction reaction) => dbContext.PostReactions.Remove(reaction);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            // Two concurrent requests tried to create the user's first reaction for the post;
            // the caller turns the insert into an update and retries.
            dbContext.ChangeTracker.Clear();
            throw new ConflictException("The reaction conflicts with a concurrent update. Please retry.", exception);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
