namespace ImageStore.Domain.Posts;

/// <summary>
/// Limits shared by the post domain model, the application validators and the database schema.
/// </summary>
public static class PostRules
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int CommentMaxLength = 2000;
}
