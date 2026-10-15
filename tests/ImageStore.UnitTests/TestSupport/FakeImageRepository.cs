using ImageStore.Application.Common;
using ImageStore.Application.Images;
using ImageStore.Domain.Images;

namespace ImageStore.UnitTests.TestSupport;

/// <summary>
/// In-memory repository with unit-of-work semantics (changes become visible on save) and
/// switches to simulate database failures.
/// </summary>
internal sealed class FakeImageRepository : IImageRepository
{
    private readonly List<Image> _pendingAdds = [];
    private readonly List<Image> _pendingRemovals = [];
    private readonly List<Tag> _pendingTagRemovals = [];

    public List<Image> Images { get; } = [];

    public List<Tag> Tags { get; } = [];

    public Exception? SaveFailure { get; set; }

    public bool? AnyExistOverride { get; set; }

    public Exception? AnyExistFailure { get; set; }

    public int SaveCount { get; private set; }

    public Task<Image?> GetOwnedAsync(Guid ownerUserId, Guid imageId, CancellationToken cancellationToken) =>
        Task.FromResult(Images.SingleOrDefault(image => image.Id == imageId && image.OwnerUserId == ownerUserId));

    public Task<Image?> GetOwnedForUpdateAsync(Guid ownerUserId, Guid imageId, CancellationToken cancellationToken) =>
        GetOwnedAsync(ownerUserId, imageId, cancellationToken);

    public Task<PagedResult<Image>> ListOwnedAsync(Guid ownerUserId, ImageListCriteria criteria, CancellationToken cancellationToken)
    {
        var owned = Images.Where(image => image.OwnerUserId == ownerUserId).OrderByDescending(image => image.CreatedAtUtc).ToList();
        var page = owned.Skip((criteria.Page - 1) * criteria.PageSize).Take(criteria.PageSize).ToList();
        return Task.FromResult(new PagedResult<Image>(page, criteria.Page, criteria.PageSize, owned.Count));
    }

    /// <summary>Like the real repository, new tags are committed immediately.</summary>
    public Task<IReadOnlyList<Tag>> GetOrCreateTagsAsync(Guid ownerUserId, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var result = new List<Tag>();
        foreach (var name in names)
        {
            var tag = Tags.FirstOrDefault(t => t.OwnerUserId == ownerUserId && t.NormalizedName == Tag.Normalize(name));
            if (tag is null)
            {
                tag = Tag.Create(Guid.NewGuid(), ownerUserId, name);
                Tags.Add(tag);
            }

            result.Add(tag);
        }

        return Task.FromResult<IReadOnlyList<Tag>>(result);
    }

    public Task<IReadOnlyList<Tag>> FindTagsUsedOnlyByAsync(Guid ownerUserId, Guid imageId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Tag>>(Tags
            .Where(tag => tag.OwnerUserId == ownerUserId && tagIds.Contains(tag.Id))
            .Where(tag => !Images.Any(image => image.Id != imageId && image.Tags.Any(t => t.Id == tag.Id)))
            .ToList());

    public Task<bool> AnyExistAsync(Guid ownerUserId, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken)
    {
        if (AnyExistFailure is not null)
        {
            throw AnyExistFailure;
        }

        return Task.FromResult(AnyExistOverride ?? Images.Any(image => image.OwnerUserId == ownerUserId && imageIds.Contains(image.Id)));
    }

    public void Add(Image image) => _pendingAdds.Add(image);

    public void Remove(Image image) => _pendingRemovals.Add(image);

    public void RemoveTags(IEnumerable<Tag> tags) => _pendingTagRemovals.AddRange(tags);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (SaveFailure is not null)
        {
            ClearPending();
            throw SaveFailure;
        }

        Images.AddRange(_pendingAdds);
        Images.RemoveAll(_pendingRemovals.Contains);
        Tags.RemoveAll(_pendingTagRemovals.Contains);
        ClearPending();
        SaveCount++;
        return Task.CompletedTask;
    }

    private void ClearPending()
    {
        _pendingAdds.Clear();
        _pendingRemovals.Clear();
        _pendingTagRemovals.Clear();
    }
}

internal sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; set; } = userId;
}
