using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The offset and limit rules every capped read shares: a window over a full list, the offset of the next window,
/// and the one rule for a window past the end, which is an empty page that still names the total.
/// </summary>
public static class ResultPaging
{
    /// <summary>The page size a capped read returns when the caller gives none.</summary>
    public const int DefaultLimit = 25;

    /// <summary>The page size for rows that run to several lines each, such as operations and grammar findings.</summary>
    public const int HeavyRowLimit = 10;

    /// <summary>The most rows one page may ask for.</summary>
    public const int MaxLimit = 200;

    /// <summary>Whether <paramref name="offset"/> and <paramref name="limit"/> name a page; the one range every capped read accepts.</summary>
    public static bool IsValid(int offset, int limit) => offset >= 0 && limit is >= 1 and <= MaxLimit;

    /// <summary>The cursor that starts the page after one of <paramref name="returned"/> rows, or null at the end.</summary>
    public static string? NextCursor(int offset, int returned, int total) =>
        offset + returned < total ? Cursor(offset + returned) : null;

    /// <summary>An opaque cursor for the page that starts <paramref name="offset"/> rows in.</summary>
    public static string Cursor(int offset) =>
        System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("p" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The offset a cursor from <see cref="Cursor"/> names; a missing cursor is the first page, a malformed one is not valid.</summary>
    public static bool TryOffset(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor)) return true;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var text = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(padded));
            return text.Length > 1 && text[0] == 'p' &&
                int.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out offset);
        }
        catch (System.FormatException)
        {
            return false;
        }
    }

    /// <summary>The offset <paramref name="cursor"/> names, or -1 when it is not a cursor this API issued.</summary>
    public static int OffsetOrInvalid(string? cursor) => TryOffset(cursor, out var offset) ? offset : -1;

    /// <summary>The rows from <paramref name="offset"/> on, at most <paramref name="limit"/> of them.</summary>
    public static IReadOnlyList<T> Window<T>(IReadOnlyList<T> all, int offset, int limit) =>
        all.Skip(offset).Take(limit).ToArray();
}
