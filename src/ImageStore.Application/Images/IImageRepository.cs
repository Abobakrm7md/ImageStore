using ImageStore.Application.Common;
using ImageStore.Domain.Images;

namespace ImageStore.Application.Images;

/// <summary>
/// Persistence operations for images. Every read takes the owner's identifier and applies it
/// inside the database query, so another user's data can never be loaded by mistake.
/// </summary>
public interface IImageRepository
{
    /// <summary>Loads an image (with tags) owned by <paramref name="ownerUserId"/>, read-only.</summary>
    Task<Image?> GetOwnedAsync(Guid ownerUserId, Guid imageId, CancellationToken cancellationToken);

    /// <summary>Loads an image (with tags) owned by <paramref name="ownerUserId"/> for modification.</summary>
    Task<Image?> GetOwnedForUpdateAsync(Guid ownerUserId, Guid imageId, CancellationToken cancellationToken);

    Task<PagedResult<Image>> ListOwnedAsync(Guid ownerUserId, ImageListCriteria criteria, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tag>> FindOwnedTagsAsync(
        Guid ownerUserId,
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the tags among <paramref name="tagIds"/> that no image other than
    /// <paramref name="imageId"/> uses. They become orphans once that image releases them.
    /// </summary>
    Task<IReadOnlyList<Tag>> FindTagsUsedOnlyByAsync(
        Guid ownerUserId,
        Guid imageId,
        IReadOnlyCollection<Guid> tagIds,
        CancellationToken cancellationToken);

    /// <summary>Checks the database (not the change tracker) for any of the given images.</summary>
    Task<bool> AnyExistAsync(Guid ownerUserId, IReadOnlyCollection<Guid> imageIds, CancellationToken cancellationToken);

    void Add(Image image);

    void AddTag(Tag tag);

    void Remove(Image image);

    void RemoveTags(IEnumerable<Tag> tags);

    /// <summary>Commits pending changes atomically. Unique-key violations become <see cref="ConflictException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
