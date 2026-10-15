namespace ImageStore.Domain.Images;

/// <summary>
/// Server-determined facts about a stored image file. Every value here is produced by the
/// server (from validated file content and server-generated names), never copied verbatim
/// from client input – except <see cref="OriginalFileName"/>, which is sanitized metadata only.
/// </summary>
public sealed record ImageFileDetails(
    string OriginalFileName,
    string StoredFileName,
    string RelativeStoragePath,
    string ContentType,
    long FileSizeBytes,
    int? Width,
    int? Height);
