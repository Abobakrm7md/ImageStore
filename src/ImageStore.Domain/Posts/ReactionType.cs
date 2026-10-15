namespace ImageStore.Domain.Posts;

/// <summary>
/// The reactions a user can give to an image post. Stored as integers with fixed values, so new
/// reactions can be appended later without touching existing rows. Names are exposed to clients
/// as camelCase strings (<c>like</c>, <c>love</c>, …).
/// </summary>
public enum ReactionType
{
    Like = 1,
    Love = 2,
    Laugh = 3,
    Wow = 4,
    Sad = 5,
    Dislike = 6,
}

public static class ReactionTypes
{
    /// <summary>All defined reaction types in display order.</summary>
    public static IReadOnlyList<ReactionType> All { get; } =
    [
        ReactionType.Like,
        ReactionType.Love,
        ReactionType.Laugh,
        ReactionType.Wow,
        ReactionType.Sad,
        ReactionType.Dislike,
    ];

    public static bool IsDefined(ReactionType reaction) => All.Contains(reaction);

    /// <summary>
    /// Parses the camelCase wire name of a reaction. Numeric strings are rejected: clients must
    /// use the stable names, not the underlying enum values.
    /// </summary>
    public static bool TryParse(string? name, out ReactionType reaction)
    {
        reaction = default;
        if (string.IsNullOrWhiteSpace(name) || char.IsDigit(name[0]))
        {
            return false;
        }

        return Enum.TryParse<ReactionType>(name, ignoreCase: true, out reaction) && IsDefined(reaction);
    }
}
