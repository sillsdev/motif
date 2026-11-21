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

    /// <summary>The offset that starts the page after one of <paramref name="returned"/> rows, or null at the end.</summary>
    public static int? NextOffset(int offset, int returned, int total) =>
        offset + returned < total ? offset + returned : null;

    /// <summary>The rows from <paramref name="offset"/> on, at most <paramref name="limit"/> of them.</summary>
    public static IReadOnlyList<T> Window<T>(IReadOnlyList<T> all, int offset, int limit) =>
        all.Skip(offset).Take(limit).ToArray();
}
