namespace ImageStore.Domain.Social;

/// <summary>
/// Limits shared by the social domain model, the application validators and the database schema.
/// Keeping them in one place prevents the three layers from drifting apart.
/// </summary>
public static class SocialRules
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 30;
    public const int DisplayNameMaxLength = 100;
    public const int BioMaxLength = 500;
    public const int ProfilePicturePathMaxLength = 64;
    public const int ProfilePictureContentTypeMaxLength = 100;

    /// <summary>Maximum size of an uploaded profile picture (2 MiB).</summary>
    public const long MaxProfilePictureBytes = 2 * 1024 * 1024;
}
