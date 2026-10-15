using System.Buffers;
using System.Text;
using ImageStore.Domain.Images;

namespace ImageStore.Application.Images.Validation;

/// <summary>
/// Turns a client-supplied file name into safe display metadata. The result is never used to
/// build a storage path; it is only stored and echoed back (e.g. in download headers).
/// </summary>
public static class FileNameSanitizer
{
    public const string Fallback = "image";

    private static readonly SearchValues<char> ForbiddenCharacters = SearchValues.Create("<>:\"/\\|?*");

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Fallback;
        }

        // Drop any directory component, whichever separator the client's OS uses.
        var name = fileName;
        var lastSeparator = name.AsSpan().LastIndexOfAny('/', '\\');
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..];
        }

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (!char.IsControl(character) && !ForbiddenCharacters.Contains(character))
            {
                builder.Append(character);
            }
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length == 0)
        {
            return Fallback;
        }

        return Truncate(cleaned, ImageRules.OriginalFileNameMaxLength);
    }

    /// <summary>Lower-case extension including the dot, or an empty string.</summary>
    public static string GetExtension(string sanitizedFileName) =>
        Path.GetExtension(sanitizedFileName).ToLowerInvariant();

    /// <summary>Default title for an upload: the file name without its extension.</summary>
    public static string ToTitle(string sanitizedFileName)
    {
        var title = Path.GetFileNameWithoutExtension(sanitizedFileName).Trim();
        return title.Length == 0 ? "Untitled image" : Truncate(title, ImageRules.TitleMaxLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        // Keep a short extension so the truncated name still describes its type.
        var extension = Path.GetExtension(value);
        if (extension.Length is 0 or > 16)
        {
            extension = string.Empty;
        }

        var cut = maxLength - extension.Length;
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--; // Never split a surrogate pair.
        }

        return string.Concat(value.AsSpan(0, cut).TrimEnd(), extension);
    }
}
