using System.Globalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
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
        string baselineToken, string reference, string? decisionsDigest,
        IReadOnlyList<ProposalDifferenceUnreadableEvidence>? unreadable = null)
    {
        var tally = built.Tally;
        return new ProposalDifferenceSummary(CanonicalId.Mint("difference/").Value, proposalId, jobId,
            before.AssessmentId, after.AssessmentId, text.TextId.ToString("D"), text.Title, baselineToken,
            new ProposalDifferencePolicy(), tally.Words, tally.Occurrences, built.UnboundOccurrences,
            tally.WithApprovedDecisions, tally.CouldBeBad, tally.Improved, tally.BucketCounts(), tally.MatrixCells(),
            after.Assessor, after.ProposalIntentDigest, Pin(before), Pin(after), Warnings(before, after, reference, unreadable),
            built.MeasuredWordsOutsideText, tally.LimitationCounts(),
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), reference, decisionsDigest,
            built.Words.Count);
    }

    // Names the first three, so the kept summary stays small however many Applied Proposals cannot be read.
    internal static string NameThem(IReadOnlyList<ProposalDifferenceUnreadableEvidence> unreadable) =>
        string.Join(", ", unreadable.Take(3).Select(item => item.Label is { Length: > 0 }
            ? $"'{item.Label}' ({item.ProposalId})" : item.ProposalId)) +
        (unreadable.Count > 3 ? $" and {unreadable.Count - 3} more" : "");

    private static ProposalDifferenceSidePin Pin(AssessmentRecord record) => new(
        record.Invocation?.ExecutableVersion, record.Invocation?.ExecutableBytesSha256, record.GrammarSourceSha256,
        record.Selection.Name, record.Selection.Sha256, record.TokeniserName, record.TokeniserVersion);

    // ADR 0042: a differing parser or tokeniser only annotates; a side with no recorded parser is no mismatch.
    private static string[] Warnings(AssessmentRecord before, AssessmentRecord after, string reference,
        IReadOnlyList<ProposalDifferenceUnreadableEvidence>? unreadable)
    {
        var warnings = new List<string>();
        if (reference == ProposalDifferenceReferences.SourceValidated)
            warnings.Add(ProposalDifferenceWarnings.SourceValidated + ": a named non-human agent staged them from cited " +
                "sources, so they are not a person's decisions.");
        if (unreadable is { Count: > 0 })
            warnings.Add(ProposalDifferenceWarnings.EvidenceUnreadable + ": " + NameThem(unreadable) + ".");
        var beforeParser = before.Invocation;
        var afterParser = after.Invocation;
        if (beforeParser?.ExecutableBytesSha256 is { } beforeSha && afterParser?.ExecutableBytesSha256 is { } afterSha &&
            !string.Equals(beforeSha, afterSha, StringComparison.Ordinal))
            warnings.Add($"{ProposalDifferenceWarnings.ParserDiffers}{ParserIdentity.Describe(beforeParser.ExecutableVersion, beforeSha)} " +
                $"and the after side by {ParserIdentity.Describe(afterParser.ExecutableVersion, afterSha)}. A word that " +
                "differs may differ because the parser changed, not the grammar.");
        if (!string.Equals(before.TokeniserName, after.TokeniserName, StringComparison.Ordinal) ||
            !string.Equals(before.TokeniserVersion, after.TokeniserVersion, StringComparison.Ordinal))
            warnings.Add($"{ProposalDifferenceWarnings.TokeniserDiffers}{before.TokeniserName} {before.TokeniserVersion} and the " +
                $"after side with {after.TokeniserName} {after.TokeniserVersion}, so the two may have measured different words.");
        if (Retried(before) != Retried(after))
            warnings.Add(ProposalDifferenceWarnings.RetryDiffers + " under a longer limit, " +
                "so the two runs used different effective per-word limits.");
        return [.. warnings];
    }

    private static bool Retried(AssessmentRecord record) => record.Invocation?.StoppedWordRetry is { Failure: null };
}
