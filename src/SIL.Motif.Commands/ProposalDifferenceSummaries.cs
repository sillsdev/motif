using System.Globalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Builds the summary a Difference keeps for as long as its Proposal exists.
/// </summary>
/// <remarks>
/// The summary copies by value everything the Difference needs from its Trial's Assessments, so it stays readable
/// after they are removed. Every field is a count, a fixed-shape record or a warning, so its size does not grow with
/// the number of words in the Text (pinned by
/// `TheSavedSummaryOfAThousandsOfWordsTextStaysUnderEightKilobytes`).
/// </remarks>
internal static class ProposalDifferenceSummaries
{
    /// <summary>Counts the compared words and copies the pins of both Assessments.</summary>
    internal static ProposalDifferenceSummary Create(ProposalDifferenceCommands.Built built, string jobId,
        string proposalId, AssessmentRecord before, AssessmentRecord after, SIL.Motif.Host.Texts.BaselineTextReadIndex text,
        string baselineToken)
    {
        var buckets = built.Words.SelectMany(word => word.Facts.Buckets.Select(item => item.Bucket).Distinct()
                .Select(bucket => (bucket, word)))
            .GroupBy(pair => pair.bucket).OrderBy(group => (int)group.Key)
            .Select(group => new ProposalDifferenceBucketCount(group.Key, group.Count(),
                group.Sum(pair => pair.word.Occurrences.Count))).ToArray();
        var matrix = built.Words.GroupBy(word => (Before: ProposalDifferenceRepository.StateName(word.Facts.Before),
                After: ProposalDifferenceRepository.StateName(word.Facts.After)))
            .OrderBy(group => group.Key.Before, StringComparer.Ordinal).ThenBy(group => group.Key.After, StringComparer.Ordinal)
            .Select(group => new ProposalDifferenceMatrixCell(group.Key.Before, group.Key.After, group.Count(),
                group.Sum(word => word.Occurrences.Count))).ToArray();
        return new ProposalDifferenceSummary(CanonicalId.Mint("difference/").Value, proposalId, jobId,
            before.AssessmentId, after.AssessmentId, text.TextId.ToString("D"), text.Title, baselineToken,
            new ProposalDifferencePolicy(), built.Words.Count, built.Words.Sum(word => word.Occurrences.Count),
            built.UnboundOccurrences, built.Words.Count(word => word.Facts.After.ApprovedExpectedMorphologyCount > 0),
            built.Words.Count(word => word.Facts.CouldBeBad), built.Words.Count(word => word.Facts.Improved), buckets,
            matrix, after.Assessor, after.ProposalIntentDigest, Pin(before), Pin(after), Warnings(before, after),
            built.MeasuredWordsOutsideText, Limitations(built.Words),
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static ProposalDifferenceSidePin Pin(AssessmentRecord record) => new(
        record.Invocation?.ExecutableVersion, record.Invocation?.ExecutableBytesSha256, record.GrammarSourceSha256,
        record.Selection.Name, record.Selection.Sha256, record.TokeniserName, record.TokeniserVersion);

    // A word counts once for a limitation that touched either checkpoint.
    private static ProposalDifferenceLimitationCount[] Limitations(IReadOnlyList<NewProposalDifferenceWord> words) =>
        words.SelectMany(word => word.Facts.Before.Limitations.Concat(word.Facts.After.Limitations).Distinct()
                .Select(limitation => (limitation, word)))
            .GroupBy(pair => pair.limitation).OrderBy(group => (int)group.Key)
            .Select(group => new ProposalDifferenceLimitationCount(group.Key, group.Count(),
                group.Sum(pair => pair.word.Occurrences.Count))).ToArray();

    // ADR 0042: a differing parser or tokeniser only annotates; a side with no recorded parser is no mismatch.
    private static string[] Warnings(AssessmentRecord before, AssessmentRecord after)
    {
        var warnings = new List<string>();
        var beforeParser = before.Invocation;
        var afterParser = after.Invocation;
        if (beforeParser?.ExecutableBytesSha256 is { } beforeSha && afterParser?.ExecutableBytesSha256 is { } afterSha &&
            !string.Equals(beforeSha, afterSha, StringComparison.Ordinal))
            warnings.Add($"The before side was parsed by {ParserIdentity.Describe(beforeParser.ExecutableVersion, beforeSha)} " +
                $"and the after side by {ParserIdentity.Describe(afterParser.ExecutableVersion, afterSha)}. A word that " +
                "differs may differ because the parser changed, not the grammar.");
        if (!string.Equals(before.TokeniserName, after.TokeniserName, StringComparison.Ordinal) ||
            !string.Equals(before.TokeniserVersion, after.TokeniserVersion, StringComparison.Ordinal))
            warnings.Add($"The before side was tokenised with {before.TokeniserName} {before.TokeniserVersion} and the " +
                $"after side with {after.TokeniserName} {after.TokeniserVersion}, so the two may have measured different words.");
        if (Retried(before) != Retried(after))
            warnings.Add("Only one side reran words stopped at the per-word time limit under a longer limit, " +
                "so the two runs used different effective per-word limits.");
        return [.. warnings];
    }

    private static bool Retried(AssessmentRecord record) => record.Invocation?.StoppedWordRetry is { Failure: null };
}
