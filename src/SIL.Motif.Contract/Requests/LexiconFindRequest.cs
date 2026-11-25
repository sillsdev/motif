namespace SIL.Motif.Contract.Requests;

/// <summary>How a lexicon read spells the ids it returns.</summary>
public enum LexiconIdFormat
{
    /// <summary>Canonical ids, the form a Proposal's intent names.</summary>
    Canonical,

    /// <summary>Plain hyphenated guids, the form a FieldWorks project file stores.</summary>
    Guid,
}

/// <summary>Finds lexical entries in a project's current Baseline by form, class, category and gloss.</summary>
/// <param name="ProjectPath">The path to the FieldWorks project.</param>
/// <param name="Query">Text the headword, a gloss or an alternate form contains.</param>
/// <param name="Exact">Text the headword or a form equals, ignoring case; a bare form also finds its marked affix.</param>
/// <param name="MorphTypes">Morph type names or ids an entry's lexeme form may have.</param>
/// <param name="Affix">True keeps affix entries; false keeps the rest.</param>
/// <param name="Category">A category name or id some analysis of the entry names.</param>
/// <param name="Headword">Text the headword starts with.</param>
/// <param name="HeadwordContains">Text the headword contains.</param>
/// <param name="HasGloss">True keeps entries with a gloss; false keeps those without one.</param>
/// <param name="FormRegex">A regular expression some form of the entry matches, searched in NFC text.</param>
/// <param name="Suffix">Text some form of the entry ends with, ignoring case and Unicode normalization form.</param>
/// <param name="InflectionClass">An inflection class name, abbreviation or id; a class also matches by its ancestors.</param>
/// <param name="Offset">The number of matches to skip.</param>
/// <param name="Limit">The most entries one page returns.</param>
/// <param name="IdFormat">How ids are spelled in the result.</param>
/// <param name="At">What to read: <c>baseline</c> (or absent), or a Proposal id for the grammar its latest Trial ran against.</param>
public sealed record LexiconFindRequest(
    string ProjectPath,
    string? Query = null,
    string? Exact = null,
    IReadOnlyList<string>? MorphTypes = null,
    bool? Affix = null,
    string? Category = null,
    string? Headword = null,
    string? HeadwordContains = null,
    bool? HasGloss = null,
    string? FormRegex = null,
    string? Suffix = null,
    string? InflectionClass = null,
    int Offset = 0,
    int Limit = 25,
    LexiconIdFormat IdFormat = LexiconIdFormat.Canonical,
    string? At = null);
