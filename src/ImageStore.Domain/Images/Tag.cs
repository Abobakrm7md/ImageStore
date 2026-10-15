using System.Text;
using ImageStore.Domain.Common;

namespace ImageStore.Domain.Images;

/// <summary>
/// A user-private label that can be attached to many images. Tags are scoped to their owner:
/// two users can both have a tag called "travel" without sharing anything.
/// </summary>
public sealed class Tag
{
    private readonly List<Image> _images = [];

    private Tag()
    {
        // Required by EF Core.
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>Display name, trimmed and with internal whitespace collapsed.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Case-insensitive lookup key, unique per owner.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public IReadOnlyCollection<Image> Images => _images;

    public static Tag Create(Guid id, Guid ownerUserId, string name)
    {
        if (id == Guid.Empty)
        {
            throw new DomainValidationException("id", "A tag must have an identifier.");
        }

        if (ownerUserId == Guid.Empty)
        {
            throw new DomainValidationException("ownerUserId", "A tag must have an owner.");
        }

        var cleaned = Clean(name);
        if (!IsValidName(cleaned))
        {
            throw new DomainValidationException(
                "tags",
                $"Tags must be between 1 and {ImageRules.TagNameMaxLength} characters long.");
        }

        return new Tag
        {
            Id = id,
            OwnerUserId = ownerUserId,
            Name = cleaned,
            NormalizedName = Normalize(cleaned),
        };
    }

    /// <summary>Trims the value and collapses runs of whitespace into a single space.</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var previousWasWhitespace = false;
        foreach (var character in name.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            if (char.IsControl(character))
            {
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    public static string Normalize(string? name) => Clean(name).ToUpperInvariant();

    public static bool IsValidName(string cleanedName) =>
        cleanedName.Length is > 0 and <= ImageRules.TagNameMaxLength;
}
