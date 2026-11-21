using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Commands;

/// <summary>
/// Keeps a refusal from flooding the caller: a message or fact longer than the cap keeps its start and its end and
/// says how much of the middle was cut, so a closing "Next:" sentence always survives. The cap only shortens what is
/// printed; the command's own <see cref="Refusal"/> is never changed.
/// </summary>
public static class RefusalText
{
    /// <summary>The most characters of a refusal message a terminal prints.</summary>
    public const int TextLimit = 2000;

    /// <summary>The most characters of a refusal message or fact value a <c>--json</c> failure carries.</summary>
    public const int JsonLimit = 16000;

    private const string NextMarker = "Next:";

    /// <summary>The message cut to about <paramref name="limit"/> characters, middle first, keeping its Next: tail.</summary>
    public static string Bound(string text, int limit = JsonLimit) => Cut(text, limit, "");

    /// <summary>The message cut for a terminal, naming <c>--json</c> as the way to the whole of it and the refusal code.</summary>
    public static string ForTerminal(string text, string? code = null) => text.Length <= TextLimit
        ? text
        : (code is null ? "" : $"[{code}] ") + Cut(text, TextLimit, "; add --json for the whole message");

    /// <summary>The facts with every value cut to <paramref name="limit"/> characters.</summary>
    public static IReadOnlyDictionary<string, string> Bound(IReadOnlyDictionary<string, string> facts, int limit = JsonLimit) =>
        facts.Values.All(value => value.Length <= limit)
            ? facts
            : facts.ToDictionary(pair => pair.Key, pair => Bound(pair.Value, limit), StringComparer.Ordinal);

    private static string Cut(string text, int limit, string hint)
    {
        if (text.Length <= limit) return text;
        var next = text.LastIndexOf(NextMarker, StringComparison.Ordinal);
        var tailLength = next >= 0 ? Math.Min(text.Length - next, limit / 2) : limit / 4;
        var head = limit - tailLength;
        var omitted = text.Length - head - tailLength;
        return text[..head] + $" … ({omitted:N0} chars omitted{hint}) " + text[^tailLength..];
    }
}
