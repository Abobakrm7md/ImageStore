using System.Text;
using ImageStore.Domain.Common;

namespace ImageStore.Domain.Social;

/// <summary>
/// The social profile of a user, one-to-one with the ASP.NET Core Identity account. Identity
/// keeps the credentials and the email; everything that is shown to other users lives here.
/// The user name is assigned once at registration (derived from the email) and is unique.
/// </summary>
public sealed class UserProfile
{
    private UserProfile()
    {
        // Required by EF Core.
    }

    /// <summary>The Identity user identifier (primary key and foreign key at the same time).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Unique, URL-friendly name, e.g. <c>alice_92</c>.</summary>
    public string UserName { get; private set; } = string.Empty;

    /// <summary>Case-insensitive lookup key for <see cref="UserName"/>.</summary>
    public string NormalizedUserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? Bio { get; private set; }

    /// <summary>Relative storage path of the profile picture (server-generated), if any.</summary>
    public string? ProfilePicturePath { get; private set; }

    public string? ProfilePictureContentType { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public bool HasProfilePicture => ProfilePicturePath is not null;

    public static UserProfile Create(Guid userId, string userName, string displayName, DateTime createdAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainValidationException("userId", "A profile must belong to a user.");
        }

        return new UserProfile
        {
            UserId = userId,
            UserName = NormalizeRequiredUserName(userName),
            NormalizedUserName = NormalizeUserName(userName),
            DisplayName = NormalizeDisplayName(displayName),
            CreatedAtUtc = EnsureUtc(createdAtUtc, "createdAtUtc"),
        };
    }

    /// <summary>Creates a profile with an initial (optional) biography; same rules as above.</summary>
    public static UserProfile Create(
        Guid userId,
        string userName,
        string displayName,
        string? bio,
        DateTime createdAtUtc)
    {
        var profile = Create(userId, userName, displayName, createdAtUtc);
        profile.Bio = NormalizeBio(bio);
        return profile;
    }

    /// <summary>Updates the editable profile fields. The user name is immutable.</summary>
    public void Update(string displayName, string? bio, DateTime updatedAtUtc)
    {
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        var normalizedBio = NormalizeBio(bio);
        var timestamp = EnsureUtc(updatedAtUtc, "updatedAtUtc");

        DisplayName = normalizedDisplayName;
        Bio = normalizedBio;
        UpdatedAtUtc = timestamp;
    }

    public void SetProfilePicture(string relativePath, string contentType, DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > SocialRules.ProfilePicturePathMaxLength)
        {
            throw new DomainValidationException(
                "profilePicturePath",
                $"The profile picture path must be between 1 and {SocialRules.ProfilePicturePathMaxLength} characters long.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > SocialRules.ProfilePictureContentTypeMaxLength)
        {
            throw new DomainValidationException("profilePictureContentType", "The profile picture content type is not valid.");
        }

        var timestamp = EnsureUtc(updatedAtUtc, "updatedAtUtc");
        ProfilePicturePath = relativePath;
        ProfilePictureContentType = contentType;
        UpdatedAtUtc = timestamp;
    }

    public void ClearProfilePicture(DateTime updatedAtUtc)
    {
        ProfilePicturePath = null;
        ProfilePictureContentType = null;
        UpdatedAtUtc = EnsureUtc(updatedAtUtc, "updatedAtUtc");
    }

    /// <summary>
    /// User names are 3-30 characters of letters, digits, dot, underscore and hyphen, and must
    /// start and end with a letter or digit (case-insensitive uniqueness via
    /// <see cref="NormalizedUserName"/>).
    /// </summary>
    public static bool IsValidUserName(string? userName)
    {
        if (userName is null || userName.Length is < SocialRules.UserNameMinLength or > SocialRules.UserNameMaxLength)
        {
            return false;
        }

        if (!IsUserNameEdgeCharacter(userName[0]) || !IsUserNameEdgeCharacter(userName[^1]))
        {
            return false;
        }

        foreach (var character in userName)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public static string NormalizeUserName(string? userName) => userName?.ToUpperInvariant() ?? string.Empty;

    /// <summary>
    /// Derives a user-name candidate from an email address: the local part, lower-cased, with
    /// every disallowed character removed. The result may still be invalid (too short, empty);
    /// callers must check <see cref="IsValidUserName"/> and pick a fallback.
    /// </summary>
    public static string SuggestUserName(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var localPart = email.Split('@')[0].Trim();
        var builder = new StringBuilder(localPart.Length);
        foreach (var character in localPart.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            {
                builder.Append(character);
            }
        }

        var suggested = builder.ToString();
        return suggested.Length > SocialRules.UserNameMaxLength
            ? suggested[..SocialRules.UserNameMaxLength]
            : suggested;
    }

    private static bool IsUserNameEdgeCharacter(char character) => char.IsAsciiLetterOrDigit(character);

    private static string NormalizeRequiredUserName(string? userName)
    {
        var trimmed = userName?.Trim() ?? string.Empty;
        return IsValidUserName(trimmed)
            ? trimmed
            : throw new DomainValidationException(
                "userName",
                $"User names must be {SocialRules.UserNameMinLength}-{SocialRules.UserNameMaxLength} characters long " +
                "and may only contain letters, digits, dot, underscore and hyphen.");
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("displayName", "A display name is required.");
        }

        if (trimmed.Length > SocialRules.DisplayNameMaxLength)
        {
            throw new DomainValidationException(
                "displayName",
                $"The display name must be at most {SocialRules.DisplayNameMaxLength} characters long.");
        }

        return trimmed;
    }

    private static string? NormalizeBio(string? bio)
    {
        var trimmed = bio?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > SocialRules.BioMaxLength)
        {
            throw new DomainValidationException("bio", $"The bio must be at most {SocialRules.BioMaxLength} characters long.");
        }

        return trimmed;
    }

    private static DateTime EnsureUtc(DateTime value, string propertyName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new DomainValidationException(propertyName, $"{propertyName} must be a UTC timestamp.");
}
