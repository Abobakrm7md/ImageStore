using ImageStore.Application.Common;
using ImageStore.Domain.Social;

namespace ImageStore.Application.Social;

/// <summary>Request-level validation rules shared by the social use cases.</summary>
internal static class SocialRequestValidator
{
    public const int SearchMaxLength = 100;

    public static void ValidateSearchQuery(UserSearchQuery query, ValidationErrors errors)
    {
        Paging.Validate(query.Page, query.PageSize, errors);

        if (query.Search?.Trim().Length > SearchMaxLength)
        {
            errors.Add("search", $"Search text must be at most {SearchMaxLength} characters long.");
        }
    }

    public static void ValidateProfileUpdate(string? displayName, string? bio, ValidationErrors errors)
    {
        var trimmedDisplayName = displayName?.Trim() ?? string.Empty;
        if (trimmedDisplayName.Length == 0)
        {
            errors.Add("displayName", "A display name is required.");
        }
        else if (trimmedDisplayName.Length > SocialRules.DisplayNameMaxLength)
        {
            errors.Add("displayName", $"The display name must be at most {SocialRules.DisplayNameMaxLength} characters long.");
        }

        if (bio?.Trim().Length > SocialRules.BioMaxLength)
        {
            errors.Add("bio", $"The bio must be at most {SocialRules.BioMaxLength} characters long.");
        }
    }
}
