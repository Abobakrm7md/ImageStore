using ImageStore.Application.Images;

namespace ImageStore.Api.Contracts;

/// <summary>Multipart form for uploading images.</summary>
public sealed class UploadImagesForm
{
    /// <summary>One or more image files (JPEG, PNG or WebP). Form field name: <c>files</c>.</summary>
    public List<IFormFile> Files { get; set; } = [];

    /// <summary>Optional title applied to every uploaded file (defaults to the file name).</summary>
    public string? Title { get; set; }

    /// <summary>Optional description applied to every uploaded file.</summary>
    public string? Description { get; set; }

    /// <summary>Optional tags applied to every uploaded file (repeat the field for several tags).</summary>
    public List<string>? Tags { get; set; }
}

/// <summary>The images created by an upload request.</summary>
/// <param name="Items">Metadata of the created images, in upload order.</param>
public sealed record UploadImagesResponse(IReadOnlyList<ImageDto> Items);
