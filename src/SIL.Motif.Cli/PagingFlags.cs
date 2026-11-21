using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli;

/// <summary>The page a capped read asked for: <c>--offset</c>, <c>--limit</c>, <c>--page</c> or <c>--all</c>.</summary>
/// <param name="Offset">How many rows to skip.</param>
/// <param name="Limit">The most rows to return.</param>
/// <param name="All">Whether the caller asked for every row, so no page is cut.</param>
internal readonly record struct PagingWindow(int Offset, int Limit, bool All)
{
    /// <summary>The refusal for a window the flags did not describe, as the one registered paging refusal.</summary>
    internal static Refusal Invalid(string message) =>
        new(RefusalCodes.PagingInvalid, FailureReason.InvalidArgument, message);
}

/// <summary>Reads the paging flags every capped read shares, with the one range <see cref="ResultPaging"/> accepts.</summary>
internal static class PagingFlags
{
    /// <summary>The window the flags name, or the refusal explaining why they do not name one.</summary>
    internal static (PagingWindow Window, Refusal? Refusal) Read(
        IReadOnlyDictionary<string, string> flags, int defaultLimit, bool allowAll = true)
    {
        var all = flags.ContainsKey("all") && allowAll;
        if (all && (flags.ContainsKey("offset") || flags.ContainsKey("limit") || flags.ContainsKey("page")))
            return Fail("Give --all or a page (--offset, --limit, --page), not both.");
        if (flags.ContainsKey("page") && flags.ContainsKey("offset"))
            return Fail("Give --page or --offset, not both.");
        var limit = defaultLimit;
        if (flags.TryGetValue("limit", out var limitText) && !TryCount(limitText, out limit))
            return Fail($"--limit must be a whole number from 1 to {ResultPaging.MaxLimit}.");
        var offset = 0;
        if (flags.TryGetValue("offset", out var offsetText) && !TryCount(offsetText, out offset))
            return Fail("--offset must be a whole number of zero or more.");
        if (flags.TryGetValue("page", out var pageText))
        {
            if (!TryCount(pageText, out var page) || page < 1 || (long)(page - 1) * Math.Max(limit, 1) > int.MaxValue)
                return Fail("--page must be a positive whole number.");
            offset = (page - 1) * limit;
        }
        if (!ResultPaging.IsValid(offset, limit))
            return Fail($"Use an offset of zero or more and a limit from 1 to {ResultPaging.MaxLimit}.");
        return (new PagingWindow(offset, limit, all), null);

        static bool TryCount(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        static (PagingWindow, Refusal?) Fail(string message) => (default, PagingWindow.Invalid(message));
    }
}
