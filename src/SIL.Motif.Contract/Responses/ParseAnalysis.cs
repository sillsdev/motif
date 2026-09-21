using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>One FieldWorks parse morph with portable source GUIDs and conditional guessed text.</summary>
public sealed record ParseMorph(string? Form, string? Msa, string? InflType, string? GuessedString);

/// <summary>The ordered morph bundles of one parser reading.</summary>
public sealed record ParseAnalysis(IReadOnlyList<ParseMorph> Morphs);

/// <summary>One batch case's morphology and search termination, including partial findings.</summary>
public sealed record ParseWordEvidence(
    string Schema, int Index, string Word, int ElapsedMs, bool Capped, bool TimedOut, bool InvalidShape,
    IReadOnlyList<ParseAnalysis> Analyses, IReadOnlyList<string> Unavailable);

/// <summary>One approved bundle's source references and literal form alternatives.</summary>
public sealed record ApprovedMorph(string? Form, string? Msa, string? InflType, IReadOnlyList<string> Forms);

/// <summary>An approved reading frozen from the source used for parsing.</summary>
public sealed record ApprovedMorphology(IReadOnlyList<ApprovedMorph> Morphs)
{
    public string? SourceWordformGuid { get; init; }
    public string? WritingSystem { get; init; }
}

/// <summary>Positive expectation matches; the status preserves incomplete and unavailable searches.</summary>
public sealed record WordCorrectness(
    int Expected, int Matched, string Status, IReadOnlyList<ApprovedMorphology> Expectations,
    IReadOnlyList<int> Unmatched)
{
    public IReadOnlyList<string> Unavailable { get; init; } = new string[0];
}

/// <summary>
/// One parser reading as a person reads an interlinear gloss: its morphs in order, each resolved from the
/// identifiers in the matching <see cref="ParseAnalysis"/> to the form, gloss and category the project gives it.
/// </summary>
public sealed record ParserReading(IReadOnlyList<ParserReadingMorph> Morphs);

/// <summary>One morph of a <see cref="ParserReading"/>.</summary>
/// <param name="Form">The allomorph as written, with its morph type's affix markers, such as <c>-a</c>.</param>
/// <param name="Gloss">The gloss of the sense that uses this grammatical info, or empty when none does.</param>
/// <param name="Category">The grammatical info's interlinear abbreviation, such as <c>v</c> or <c>v:Any</c>.</param>
/// <param name="InflectionType">The irregularly inflected form type's abbreviation, when the reading used one.</param>
/// <param name="Guessed">Whether the parser guessed this morph rather than finding it in the lexicon.</param>
/// <param name="FieldWorksLink">A <c>silfw:</c> link opening the morph's entry, or <see langword="null"/>.</param>
public sealed record ParserReadingMorph(
    string Form, string Gloss, string Category, string? InflectionType, bool Guessed, string? FieldWorksLink);
