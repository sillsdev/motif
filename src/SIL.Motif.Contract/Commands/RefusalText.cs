using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Commands;

/// <summary>
/// Keeps a refusal from flooding the caller: a message or fact longer than the cap keeps its start and says how much
/// was cut. The cap only shortens what is printed; the command's own <see cref="Refusal"/> is never changed.
/// </summary>
public static class RefusalText
{
    /// <summary>The most characters of a refusal message a terminal prints.</summary>
    public const int TextLimit = 2000;

    /// <summary>The most characters of a refusal message or fact value a <c>--json</c> failure carries.</summary>
    public const int JsonLimit = 16000;

    /// <summary>The message cut to <paramref name="limit"/> characters, ending with how many were left out.</summary>
    public static string Bound(string text, int limit = JsonLimit) =>
        text.Length <= limit ? text : text[..limit] + $" ... ({text.Length - limit:N0} more characters not shown)";

    /// <summary>The message cut to <see cref="TextLimit"/> characters for a terminal, naming <c>--json</c> as the way to the rest.</summary>
    public static string ForTerminal(string text) => text.Length <= TextLimit
        ? text
        : text[..TextLimit] + $" ... ({text.Length - TextLimit:N0} more characters not shown; add --json for the whole message)";

    /// <summary>The facts with every value cut to <paramref name="limit"/> characters.</summary>
    public static IReadOnlyDictionary<string, string> Bound(IReadOnlyDictionary<string, string> facts, int limit = JsonLimit) =>
        facts.Values.All(value => value.Length <= limit)
            ? facts
            : facts.ToDictionary(pair => pair.Key, pair => Bound(pair.Value, limit), System.StringComparer.Ordinal);
}
