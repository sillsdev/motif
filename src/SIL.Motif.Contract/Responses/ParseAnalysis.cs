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
