using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Jobs;

/// <summary>One set of words that shares an exact writing-system input.</summary>
public sealed record ReviewParsePartition(string? WritingSystemId, IReadOnlyList<string> Words,
    IReadOnlyList<string> CaseKeys);

/// <summary>Baseline Assessments produced for one captured writing-system partition.</summary>
public sealed record ReviewBaselinePartition(string? WritingSystemId, IReadOnlyList<string> CaseKeys,
    IReadOnlyList<string> AssessmentIds);

/// <summary>Candidate Assessments produced for one captured writing-system partition.</summary>
public sealed record ReviewAfterPartition(string? WritingSystemId, IReadOnlyList<string> CaseKeys,
    IReadOnlyList<string> AssessmentIds);

/// <summary>Separates parser work by the writing-system identity captured for each case.</summary>
public static class ReviewInputPartitions
{
    /// <summary>Groups words and case references without sharing a parse across writing systems.</summary>
    public static IReadOnlyList<ReviewParsePartition> Create(FrozenReviewInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return Array.AsReadOnly(inputs.Cases.GroupBy(item => item.WritingSystemId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new ReviewParsePartition(group.Key,
                Array.AsReadOnly(group.Select(item => item.Word).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(group.Select(item => item.CaseKey).Order(StringComparer.Ordinal).ToArray())))
            .ToArray());
    }
}
