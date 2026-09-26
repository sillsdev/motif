using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Commands;

/// <summary>Adds the parser-not-found fact to a parser-unavailable refusal's facts when no executable was found.</summary>
internal static class ParserNotFoundFact
{
    /// <summary>Returns <paramref name="facts"/>, plus the not-found fact when <paramref name="executableMissing"/>.</summary>
    internal static IReadOnlyDictionary<string, string>? Mark(
        IReadOnlyDictionary<string, string>? facts, bool executableMissing)
    {
        if (!executableMissing) return facts;
        var marked = facts is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(facts, StringComparer.Ordinal);
        marked[RefusalFactNames.ParserNotFound] = "true";
        return marked;
    }
}
