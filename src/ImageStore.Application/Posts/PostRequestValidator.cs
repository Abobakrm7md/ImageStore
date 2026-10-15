using ImageStore.Application.Common;
using ImageStore.Domain.Posts;

namespace ImageStore.Application.Posts;

/// <summary>Request-level validation rules shared by the post, comment and reaction use cases.</summary>
internal static class PostRequestValidator
{
    public static void ValidatePostText(string? title, bool titleRequired, string? description, ValidationErrors errors)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (titleRequired && trimmedTitle.Length == 0)
        {
            errors.Add("title", "Title is required.");
        }
        else if (trimmedTitle.Length > PostRules.TitleMaxLength)
        {
            errors.Add("title", $"Title must be at most {PostRules.TitleMaxLength} characters long.");
        }

        if (description?.Trim().Length > PostRules.DescriptionMaxLength)
        {
            errors.Add("description", $"Description must be at most {PostRules.DescriptionMaxLength} characters long.");
        }
    }

    public static void ValidateCommentContent(string? content, ValidationErrors errors)
    {
        var trimmed = content?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add("content", "A comment cannot be empty.");
        }
        else if (trimmed.Length > PostRules.CommentMaxLength)
        {
            errors.Add("content", $"A comment must be at most {PostRules.CommentMaxLength} characters long.");
        }
    }
}
