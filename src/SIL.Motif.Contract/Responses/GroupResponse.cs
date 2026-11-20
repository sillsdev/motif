using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The corpus words that end or start in a string, or match a pattern, grouped by candidate stem. Each stem group is
/// marked known (a lexicon stem), fitting (not in the lexicon, but the bare stem or another word shares it) or
/// not-fitting (only this ending attests it). Groups are ordered by token count, then stem.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupResponse(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string Query,
    [property: JsonRequired] string ParseState,
    [property: JsonRequired] string Fold,
    [property: JsonRequired] string ReadAt,
    [property: JsonRequired] int MinStem,
    [property: JsonRequired] int CorpusTypes,
    [property: JsonRequired] int MatchingTypes,
    [property: JsonRequired] int Groups,
    [property: JsonRequired] int Known,
    [property: JsonRequired] int Fitting,
    [property: JsonRequired] int NotFitting,
    [property: JsonRequired] int Page,
    [property: JsonRequired] int Pages,
    [property: JsonRequired] int Limit,
    [property: JsonRequired] bool Capped,
    [property: JsonRequired] IReadOnlyList<string> Notes,
    [property: JsonRequired] IReadOnlyList<StemGroup> Rows)
{
    /// <summary>The current response shape.</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>One candidate stem, its standing and the matching words that share it.</summary>
/// <param name="Stem">The folded stem the words share.</param>
/// <param name="Status">known, fitting or not-fitting.</param>
/// <param name="Tokens">The occurrences of the listed words in the Texts.</param>
/// <param name="StemAttested">Whether the bare stem is itself a corpus word.</param>
/// <param name="Words">The matching words, most frequent first, at most <see cref="WordsShown"/>.</param>
/// <param name="WordsTotal">All matching words of this stem that the parse-state filter kept.</param>
/// <param name="Siblings">Other corpus words that share the stem, at most <see cref="SiblingsShown"/>, in code-point order.</param>
/// <param name="SiblingsTotal">All other corpus words that share the stem.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StemGroup(
    [property: JsonRequired] string Stem,
    [property: JsonRequired] string Status,
    [property: JsonRequired] int Tokens,
    [property: JsonRequired] bool StemAttested,
    [property: JsonRequired] IReadOnlyList<GroupedWord> Words,
    [property: JsonRequired] int WordsTotal,
    [property: JsonRequired] IReadOnlyList<string> Siblings,
    [property: JsonRequired] int SiblingsTotal)
{
    /// <summary>The stem is a lexicon stem form.</summary>
    public const string Known = "known";

    /// <summary>The stem is not in the lexicon, but the bare stem or another corpus word attests it.</summary>
    public const string Fitting = "fitting";

    /// <summary>Nothing but the matching words attests the stem.</summary>
    public const string NotFitting = "not-fitting";

    /// <summary>The most words listed under one stem.</summary>
    public const int WordsShown = 20;

    /// <summary>The most siblings listed under one stem.</summary>
    public const int SiblingsShown = 5;
}

/// <summary>One matching corpus word type.</summary>
/// <param name="Word">The most frequent spelling of the type.</param>
/// <param name="ParseState">approved, parsed or unparsed.</param>
/// <param name="Tokens">How often the type occurs in the Texts.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupedWord(
    [property: JsonRequired] string Word,
    [property: JsonRequired] string ParseState,
    [property: JsonRequired] int Tokens);
