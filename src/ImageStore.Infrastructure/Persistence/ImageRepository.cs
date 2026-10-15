using ImageStore.Application.Common;
using ImageStore.Application.Images;
using ImageStore.Domain.Images;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.Infrastructure.Persistence;

internal sealed class ImageRepository(ImageStoreDbContext dbContext) : IImageRepository
{
    internal const string LikeEscapeCharacter = "\\";
    private const int MaxTagCreationAttempts = 3;

    public Task<Image?> GetOwnedAsync(Guid ownerUserId, Guid imageId, CancellationToken cancellationToken) =>
        dbContext.Images
            .AsNoTracking()
            .Include(image => image.Tags)
            .SingleOrDefaultAsync(image => image.Id == imageId && image.OwnerUserId == ownerUserId, cancellationToken);

    public Task<Image?> GetOwnedForUpdateAsync(Guid ownerUserId, Guid imageId, CancellationToken cancellationToken) =>
        dbContext.Images
            .Include(image => image.Tags)
            .SingleOrDefaultAsync(image => image.Id == imageId && image.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<PagedResult<Image>> ListOwnedAsync(
        Guid ownerUserId,
        ImageListCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var filtered = BuildFilteredQuery(ownerUserId, criteria);
        var totalCount = await filtered.CountAsync(cancellationToken);
        var items = await BuildPageQuery(filtered, criteria).ToListAsync(cancellationToken);

        return new PagedResult<Image>(items, criteria.Page, criteria.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<Tag>> GetOrCreateTagsAsync(
        Guid ownerUserId,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            // New tags are committed immediately; other pending changes would be committed with them.
            throw new InvalidOperationException("Tags must be resolved before other changes are made in the unit of work.");
        }

        var normalizedNames = names.Select(Tag.Normalize).Distinct(StringComparer.Ordinal).ToList();
        for (var attempt = 1; ; attempt++)
        {
            var existing = await dbContext.Tags
                .Where(tag => tag.OwnerUserId == ownerUserId && normalizedNames.Contains(tag.NormalizedName))
                .ToListAsync(cancellationToken);

            // Grouping tolerates duplicates, which providers without unique constraints may contain.
            var byName = existing
                .GroupBy(tag => tag.NormalizedName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(tag => tag.Id).First(), StringComparer.Ordinal);

            var created = names
                .Where(name => !byName.ContainsKey(Tag.Normalize(name)))
                .Select(name => Tag.Create(Guid.CreateVersion7(), ownerUserId, name))
                .ToList();

            if (created.Count > 0)
            {
                dbContext.Tags.AddRange(created);
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception) && attempt < MaxTagCreationAttempts)
                {
                    // A concurrent request created one of these tags first: discard ours and reuse theirs.
                    foreach (var tag in created)
                    {
                        dbContext.Entry(tag).State = EntityState.Detached;
                    }

                    continue;
                }

                foreach (var tag in created)
                {
                    byName[tag.NormalizedName] = tag;
                }
            }

            return names.Select(name => byName[Tag.Normalize(name)]).DistinctBy(tag => tag.Id).ToList();
        }
    }

    public async Task<IReadOnlyList<Tag>> FindTagsUsedOnlyByAsync(
        Guid ownerUserId,
        Guid imageId,
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Tags
            .Where(tag => tag.OwnerUserId == ownerUserId
                && tagIds.Contains(tag.Id)
                && !tag.Images.Any(image => image.Id != imageId))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> AnyExistAsync(Guid ownerUserId, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken) =>
        dbContext.Images
            .AsNoTracking()
            .AnyAsync(image => image.OwnerUserId == ownerUserId && imageIds.Contains(image.Id), cancellationToken);

    public void Add(Image image) => dbContext.Images.Add(image);

    public void Remove(Image image) => dbContext.Images.Remove(image);

    public void RemoveTags(IEnumerable<Tag> tags) => dbContext.Tags.RemoveRange(tags);

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

    /// <summary>Owner filter plus optional search and tag filters (exposed for SQL translation tests).</summary>
    internal IQueryable<Image> BuildFilteredQuery(Guid ownerUserId, ImageListCriteria criteria)
    {
        var query = dbContext.Images.AsNoTracking().Where(image => image.OwnerUserId == ownerUserId);

        if (!string.IsNullOrEmpty(criteria.Search))
        {
            // User input is escaped so that %, _ and [ are matched literally.
            var pattern = $"%{EscapeLikePattern(criteria.Search)}%";
            query = query.Where(image =>
                EF.Functions.Like(image.Title, pattern, LikeEscapeCharacter)
                || (image.Description != null && EF.Functions.Like(image.Description, pattern, LikeEscapeCharacter)));
        }

        if (criteria.NormalizedTag is { } normalizedTag)
        {
            query = query.Where(image => image.Tags.Any(tag => tag.NormalizedName == normalizedTag));
        }

        return query;
    }

    internal static IQueryable<Image> BuildPageQuery(IQueryable<Image> filtered, ImageListCriteria criteria) =>
        ApplySorting(filtered, criteria)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Include(image => image.Tags);

    internal static string EscapeLikePattern(string value) =>
        value
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscapeCharacter + "[", StringComparison.Ordinal);

    // The Id tiebreaker keeps pagination stable when sort values are equal.
    private static IQueryable<Image> ApplySorting(IQueryable<Image> query, ImageListCriteria criteria) =>
        (criteria.SortBy, criteria.SortDirection) switch
        {
            (ImageSortField.Title, SortDirection.Asc) => query.OrderBy(image => image.Title).ThenBy(image => image.Id),
            (ImageSortField.Title, SortDirection.Desc) => query.OrderByDescending(image => image.Title).ThenByDescending(image => image.Id),
            (ImageSortField.FileSize, SortDirection.Asc) => query.OrderBy(image => image.FileSizeBytes).ThenBy(image => image.Id),
            (ImageSortField.FileSize, SortDirection.Desc) => query.OrderByDescending(image => image.FileSizeBytes).ThenByDescending(image => image.Id),
            (_, SortDirection.Asc) => query.OrderBy(image => image.CreatedAtUtc).ThenBy(image => image.Id),
            _ => query.OrderByDescending(image => image.CreatedAtUtc).ThenByDescending(image => image.Id),
        };

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
