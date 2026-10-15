using ImageStore.Application.Common;
using ImageStore.Application.Posts;
using ImageStore.Domain.Posts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.Infrastructure.Persistence;

internal sealed class CommentRepository(ImageStoreDbContext dbContext) : ICommentRepository
{
    public async Task<PagedResult<Comment>> ListAsync(Guid postId, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Chronological reading order; the (CreatedAtUtc, Id) ordering is total, so pages are stable.
        var query = dbContext.Comments
            .AsNoTracking()
            .Where(comment => comment.PostId == postId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(comment => comment.CreatedAtUtc)
            .ThenBy(comment => comment.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(comment => comment.Author)
            .ToListAsync(cancellationToken);

        return new PagedResult<Comment>(items, page, pageSize, totalCount);
    }

    public Task<Comment?> GetAsync(Guid postId, Guid commentId, CancellationToken cancellationToken) =>
        dbContext.Comments
            .AsNoTracking()
            .Include(comment => comment.Author)
            .SingleOrDefaultAsync(comment => comment.Id == commentId && comment.PostId == postId, cancellationToken);

    public Task<Comment?> GetForUpdateAsync(Guid postId, Guid commentId, CancellationToken cancellationToken) =>
        dbContext.Comments.SingleOrDefaultAsync(
            comment => comment.Id == commentId && comment.PostId == postId,
            cancellationToken);

    public void Add(Comment comment) => dbContext.Comments.Add(comment);

    public void Remove(Comment comment) => dbContext.Comments.Remove(comment);

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

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
