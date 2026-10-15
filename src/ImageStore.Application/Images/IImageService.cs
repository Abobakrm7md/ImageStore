using ImageStore.Application.Common;

namespace ImageStore.Application.Images;

/// <summary>Image use cases. Every operation is scoped to the current authenticated user.</summary>
public interface IImageService
{
    Task<PagedResult<ImageDto>> ListAsync(ImageListQuery query, CancellationToken cancellationToken);

    Task<ImageDto> GetAsync(Guid imageId, CancellationToken cancellationToken);

    /// <summary>
    /// Validates every file first; if any file is invalid nothing is stored. Otherwise all files
    /// are committed to storage and their metadata saved in a single database transaction.
    /// </summary>
    Task<IReadOnlyList<ImageDto>> UploadAsync(UploadImagesCommand command, CancellationToken cancellationToken);

    Task<ImageDto> UpdateAsync(Guid imageId, UpdateImageRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid imageId, CancellationToken cancellationToken);

    Task<ImageContent> GetContentAsync(Guid imageId, CancellationToken cancellationToken);

    UploadPolicyDto GetUploadPolicy();
}
