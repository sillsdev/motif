using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>How much of the analysis identity comparison was available.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnalysisComparisonAvailability>))]
public enum AnalysisComparisonAvailability
{
    /// <summary>The Baseline analyses and parser morphology were compared by identity.</summary>
    Available,
    /// <summary>Only recorded grades are available; matched analysis identities remain unknown.</summary>
    RecordedGradesOnly,
    /// <summary>No morphology comparison was recorded or reconstructed.</summary>
    Unavailable,
}

/// <summary>One stored analysis and its individual FieldWorks opinion.</summary>
public sealed record ComparedAnalysis(string AnalysisId, string Opinion);

/// <summary>One parser reading's matches, preserving every opinion even when morphologies coincide.</summary>
public sealed record ComparedReading(int Index, IReadOnlyList<ComparedAnalysis> Matches, string? RecordedGrade);

/// <summary>
/// One word's comparison, independent of marking actions. Outcome controls placement, MeaningCode controls
/// grouping, and Headline and Detail are presentation. Absence claims apply only to completed searches.
/// </summary>
public sealed record WordComparison(
    string Standing, WordRowOutcome Outcome, string MeaningCode, string Headline, WordRowTone Tone)
{
    /// <summary>The second line, or empty when the headline needs no qualification.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Whether the parser stopped before completing the search.</summary>
    public bool IsIncomplete { get; init; }

    /// <summary>Whether matched identities are available, only grades were recorded, or neither was available.</summary>
    public AnalysisComparisonAvailability Availability { get; init; } = AnalysisComparisonAvailability.Unavailable;

    /// <summary>Matches and recorded opinions for each parser reading, in producer order.</summary>
    public IReadOnlyList<ComparedReading> Readings { get; init; } = [];

    /// <summary>Approved analyses absent from a completed search; empty on incomplete searches.</summary>
    public IReadOnlyList<ParserReading> MissingApproved { get; init; } = [];

    /// <summary>Approved analysis identities absent from a completed search.</summary>
    public IReadOnlyList<ComparedAnalysis> MissingApprovedAnalyses { get; init; } = [];

    /// <summary>Distinct Disapproved analyses the parser rebuilt, including in partial results.</summary>
    public IReadOnlyList<ComparedAnalysis> RebuiltDisapproved { get; init; } = [];

    /// <summary>Parser reading indices with no stored match; empty when identities could not be compared.</summary>
    public IReadOnlyList<int> ExtraReadingIndices { get; init; } = [];

    /// <summary>Undecided stored analyses absent from a completed search; empty when absence is unknown.</summary>
    public IReadOnlyList<ComparedAnalysis> UndecidedNotBuilt { get; init; } = [];
}

/// <summary>
/// Identity comparison captured against the measured Baseline. Empty sets are known empty; an absent record
/// means the comparison was unavailable. Unbuilt sets carry no absence claim when the search was incomplete.
/// </summary>
public sealed record WordAnalysisComparison(
    IReadOnlyList<ComparedReading> Readings,
    IReadOnlyList<ComparedAnalysis> UnbuiltApproved,
    IReadOnlyList<ComparedAnalysis> UnbuiltUndecided);
