namespace ImageStore.Domain.Images;

/// <summary>
/// Limits shared by the domain model, the application validators and the database schema.
/// Keeping them in one place prevents the three layers from drifting apart.
/// </summary>
public static class ImageRules
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int OriginalFileNameMaxLength = 255;
    public const int StoredFileNameMaxLength = 64;
    public const int RelativeStoragePathMaxLength = 128;
    public const int ContentTypeMaxLength = 100;
    public const int MaxTagsPerImage = 20;
    public const int TagNameMaxLength = 50;
}
