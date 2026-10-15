using System.Text.RegularExpressions;
using ImageStore.Domain.Social;

namespace ImageStore.Application.Images.Storage;

/// <summary>
/// Builds and validates storage paths of the form <c>profiles/{userId}.{ext}</c> for profile
/// pictures. Like image paths, both segments are server-generated, so a valid path can never
/// contain traversal sequences or client-supplied names.
/// </summary>
public static partial class ProfilePictureStoragePaths
{
    public const string DirectoryName = "profiles";

    public static string CreateRelativePath(Guid userId, string extension)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier is required.", nameof(userId));
        }

        if (!ExtensionRegex().IsMatch(extension))
        {
            throw new ArgumentException("The extension must look like '.jpg'.", nameof(extension));
        }

        var path = $"{DirectoryName}/{userId:D}{extension}";
        return IsValidRelativePath(path)
            ? path
            : throw new ArgumentException("The profile picture path is not valid.", nameof(extension));
    }

    /// <summary>Strict allow-list check used before every file-system access.</summary>
    public static bool IsValidRelativePath(string? relativePath) =>
        relativePath is not null
        && relativePath.Length <= SocialRules.ProfilePicturePathMaxLength
        && RelativePathRegex().IsMatch(relativePath);

    [GeneratedRegex(@"^\.[a-z0-9]{1,10}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex(
        @"^profiles/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.[a-z0-9]{1,10}\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex RelativePathRegex();
}
