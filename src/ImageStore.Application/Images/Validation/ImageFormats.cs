namespace ImageStore.Application.Images.Validation;

public enum ImageFormat
{
    Jpeg,
    Png,
    WebP,
}

/// <summary>Describes a supported format: its canonical MIME type and file extensions.</summary>
public sealed record ImageFormatDescriptor(
    ImageFormat Format,
    string MimeType,
    string CanonicalExtension,
    IReadOnlyList<string> Extensions);

/// <summary>
/// The formats the server knows how to verify. Configuration can only narrow this list – adding
/// a new format requires a signature check in <see cref="ImageFormatInspector"/>.
/// </summary>
public static class ImageFormats
{
    public static readonly ImageFormatDescriptor Jpeg = new(ImageFormat.Jpeg, "image/jpeg", ".jpg", [".jpg", ".jpeg"]);
    public static readonly ImageFormatDescriptor Png = new(ImageFormat.Png, "image/png", ".png", [".png"]);
    public static readonly ImageFormatDescriptor WebP = new(ImageFormat.WebP, "image/webp", ".webp", [".webp"]);

    public static IReadOnlyList<ImageFormatDescriptor> All { get; } = [Jpeg, Png, WebP];

    public static ImageFormatDescriptor Get(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => Jpeg,
        ImageFormat.Png => Png,
        ImageFormat.WebP => WebP,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown image format."),
    };

    public static ImageFormatDescriptor? FindByExtension(string? extension) =>
        string.IsNullOrEmpty(extension)
            ? null
            : All.FirstOrDefault(descriptor => descriptor.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

    public static ImageFormatDescriptor? FindByMimeType(string? mimeType) =>
        string.IsNullOrEmpty(mimeType)
            ? null
            : All.FirstOrDefault(descriptor => string.Equals(descriptor.MimeType, mimeType, StringComparison.OrdinalIgnoreCase));
}
