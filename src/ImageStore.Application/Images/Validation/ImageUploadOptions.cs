using Microsoft.Extensions.Options;

namespace ImageStore.Application.Images.Validation;

/// <summary>Upload limits, bound from the <c>ImageUpload</c> configuration section.</summary>
public sealed class ImageUploadOptions
{
    public const string SectionName = "ImageUpload";
    public const long AbsoluteMaxFileSizeBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>Maximum size of a single file in bytes (default 10 MiB).</summary>
    public long MaxFileSizeBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>Maximum number of files in one upload request.</summary>
    public int MaxFilesPerRequest { get; set; } = 10;

    /// <summary>
    /// Maximum width × height. Protects clients from tiny files that declare enormous
    /// canvases ("decompression bombs").
    /// </summary>
    public long MaxImagePixels { get; set; } = 100_000_000;

    /// <summary>Accepted extensions, e.g. <c>.jpg</c>. Must map to a supported format.</summary>
    public IList<string> AllowedExtensions { get; set; } = [];

    /// <summary>Accepted MIME types, e.g. <c>image/png</c>. Checked against the detected format.</summary>
    public IList<string> AllowedContentTypes { get; set; } = [];

    public bool IsExtensionAllowed(string extension) =>
        AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public bool IsContentTypeAllowed(string contentType) =>
        AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Fails application start-up when the upload configuration is inconsistent.</summary>
public sealed class ImageUploadOptionsValidator : IValidateOptions<ImageUploadOptions>
{
    public ValidateOptionsResult Validate(string? name, ImageUploadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.MaxFileSizeBytes is <= 0 or > ImageUploadOptions.AbsoluteMaxFileSizeBytes)
        {
            failures.Add($"ImageUpload:MaxFileSizeBytes must be between 1 and {ImageUploadOptions.AbsoluteMaxFileSizeBytes}.");
        }

        if (options.MaxFilesPerRequest is < 1 or > 100)
        {
            failures.Add("ImageUpload:MaxFilesPerRequest must be between 1 and 100.");
        }

        if (options.MaxImagePixels <= 0)
        {
            failures.Add("ImageUpload:MaxImagePixels must be positive.");
        }

        if (options.AllowedExtensions.Count == 0)
        {
            failures.Add("ImageUpload:AllowedExtensions must contain at least one extension.");
        }

        if (options.AllowedContentTypes.Count == 0)
        {
            failures.Add("ImageUpload:AllowedContentTypes must contain at least one content type.");
        }

        var supportedExtensions = string.Join(", ", ImageFormats.All.SelectMany(format => format.Extensions));
        foreach (var extension in options.AllowedExtensions)
        {
            var format = ImageFormats.FindByExtension(extension);
            if (format is null)
            {
                failures.Add($"ImageUpload:AllowedExtensions contains unsupported extension '{extension}'. Supported: {supportedExtensions}.");
            }
            else if (!options.IsContentTypeAllowed(format.MimeType))
            {
                failures.Add($"Extension '{extension}' is allowed but its content type '{format.MimeType}' is not listed in ImageUpload:AllowedContentTypes.");
            }
        }

        foreach (var contentType in options.AllowedContentTypes)
        {
            if (ImageFormats.FindByMimeType(contentType) is null)
            {
                failures.Add($"ImageUpload:AllowedContentTypes contains unsupported content type '{contentType}'.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
