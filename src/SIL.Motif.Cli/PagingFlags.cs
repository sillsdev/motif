using System.Globalization;
using System.Linq;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli;

/// <summary>The page a capped read asked for: <c>--cursor</c> and <c>--limit</c>.</summary>
/// <param name="Offset">How many rows to skip, as the cursor names it.</param>
/// <param name="Limit">The most rows to return.</param>
internal readonly record struct PagingWindow(int Offset, int Limit)
{
    /// <summary>The refusal for a window the flags did not describe, as the one registered paging refusal.</summary>
    internal static Refusal Invalid(string message) =>
        new(RefusalCodes.PagingInvalid, FailureReason.InvalidArgument, message);
}

/// <summary>Reads the paging flags every capped read shares, with the one range <see cref="ResultPaging"/> accepts.</summary>
internal static class PagingFlags
{
    /// <summary>The window the flags name, or the refusal explaining why they do not name one.</summary>
    internal static (PagingWindow Window, Refusal? Refusal) Read(IReadOnlyDictionary<string, string> flags, int defaultLimit)
    {
        var limit = defaultLimit;
        if (flags.TryGetValue("limit", out var limitText) &&
            !int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit))
            return Fail($"--limit must be a whole number from 1 to {ResultPaging.MaxLimit}.");
        var offset = 0;
        if (flags.TryGetValue("cursor", out var cursor) && (cursor == "true" || !ResultPaging.TryOffset(cursor, out offset)))
            return Fail("--cursor must be the nextCursor a previous page returned.");
        if (!ResultPaging.IsValid(offset, limit))
            return Fail($"Use a limit from 1 to {ResultPaging.MaxLimit}.");
        return (new PagingWindow(offset, limit), null);

        static (PagingWindow, Refusal?) Fail(string message) => (default, PagingWindow.Invalid(message));
    }

    /// <summary>The command that reads the page after this one, repeating the caller's own flags and page size.</summary>
    internal static string NextCommand(
        string verb, IReadOnlyList<string> positionals, IReadOnlyDictionary<string, string> flags, string nextCursor, int limit)
    {
        var parts = new List<string> { "motif", verb };
        parts.AddRange(positionals.Select(Quote));
        foreach (var (name, value) in flags)
        {
            if (name is "cursor" or "limit" or "json") continue;
            parts.Add("--" + name);
            if (value != "true") parts.Add(Quote(value));
        }
        parts.Add("--cursor");
        parts.Add(nextCursor);
        parts.Add("--limit");
        parts.Add(limit.ToString(CultureInfo.InvariantCulture));
        return string.Join(' ', parts);

        static string Quote(string text) =>
            text.Length > 0 && text.All(character => !char.IsWhiteSpace(character) && character != '"') ? text
                : "\"" + text.Replace("\"", "\\\"") + "\"";
    }
}
