using ImageStore.Application.Common;
using ImageStore.Domain.Images;

namespace ImageStore.Application.Images.Validation;

/// <summary>Request-level validation rules shared by the image use cases.</summary>
internal static class ImageRequestValidator
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 200;

    public static void ValidateListQuery(ImageListQuery query, ValidationErrors errors)
    {
        if (query.Page is < 1)
        {
            errors.Add("page", "Page must be 1 or greater.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (query.Search?.Trim().Length > SearchMaxLength)
        {
            errors.Add("search", $"Search text must be at most {SearchMaxLength} characters long.");
        }

        if (query.Tag is not null && Tag.Clean(query.Tag).Length > ImageRules.TagNameMaxLength)
        {
            errors.Add("tag", $"Tag must be at most {ImageRules.TagNameMaxLength} characters long.");
        }
    }

    public static void ValidateMetadata(
        string? title,
        bool titleRequired,
        string? description,
        IReadOnlyList<string>? tags,
        ValidationErrors errors)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (titleRequired && trimmedTitle.Length == 0)
        {
            errors.Add("title", "Title is required.");
        }
        else if (trimmedTitle.Length > ImageRules.TitleMaxLength)
        {
            errors.Add("title", $"Title must be at most {ImageRules.TitleMaxLength} characters long.");
        }

        if (description?.Trim().Length > ImageRules.DescriptionMaxLength)
        {
            errors.Add("description", $"Description must be at most {ImageRules.DescriptionMaxLength} characters long.");
        }

        if (tags is null)
        {
            return;
        }

        var cleaned = tags.Select(Tag.Clean).Where(tag => tag.Length > 0).ToList();
        if (cleaned.Count != tags.Count)
        {
            errors.Add("tags", "Tags cannot be empty.");
        }

        if (cleaned.Any(tag => tag.Length > ImageRules.TagNameMaxLength))
        {
            errors.Add("tags", $"Each tag must be at most {ImageRules.TagNameMaxLength} characters long.");
        }

        if (cleaned.Select(Tag.Normalize).Distinct(StringComparer.Ordinal).Count() > ImageRules.MaxTagsPerImage)
        {
            errors.Add("tags", $"An image can have at most {ImageRules.MaxTagsPerImage} tags.");
        }
    }
}
