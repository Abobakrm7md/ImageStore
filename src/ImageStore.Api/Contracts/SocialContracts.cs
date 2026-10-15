namespace ImageStore.Api.Contracts;

/// <summary>Multipart form for uploading a profile picture.</summary>
public sealed class ProfilePictureForm
{
    /// <summary>The picture file (JPEG, PNG or WebP, at most 2 MiB). Form field name: <c>file</c>.</summary>
    public IFormFile? File { get; set; }
}
