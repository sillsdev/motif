using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// One Proposal's changes as a single list in the order a person walks them: grammar changes first in
/// declared dependency order, then word changes in Text order.
/// </summary>
/// <param name="ProposalId">The Proposal read, or empty when no pending Draft exists.</param>
/// <param name="IntentDigest">The digest of the content read, or empty when there is none.</param>
/// <param name="Revision">The Draft revision, or the intent digest of a finalized Proposal.</param>
/// <param name="Entries">The changes in walking order.</param>
/// <param name="Trial">The Proposal's latest Trial, or <see langword="null"/> when none was run.</param>
/// <param name="Readiness">Whether the Trial's Correctness evidence permits Apply, or null without a Trial.</param>
/// <param name="Fits">How each word change fits the project now.</param>
/// <param name="LookedAtCount">The number of entries still marked as looked at.</param>
public sealed record ProposalReviewResponse(
    string ProposalId,
    string IntentDigest,
    string Revision,
    IReadOnlyList<ReviewEntry> Entries,
    TrialRecord? Trial,
    ReadinessProjection? Readiness,
    IReadOnlyList<ChangeFit> Fits,
    int LookedAtCount)
{
    /// <summary>The Proposal's own label, or null when it has none.</summary>
    public string? Label { get; init; }

    /// <summary>The digest of the Trial evidence a looked-at mark is judged against, or "none".</summary>
    public string EvidenceDigest { get; init; } = "none";

    /// <summary>Whether the Proposal is a Draft that is not yet finalized.</summary>
    public bool IsDraft { get; init; }

    /// <summary>The Draft's name while <see cref="IsDraft"/>, which Undo edits; null for a finalized Proposal.</summary>
    public string? DraftName { get; init; }

    /// <summary>Whether the Proposal holds the pending changes, which the window never calls a Proposal.</summary>
    public bool IsPendingChanges { get; init; }
}

/// <summary>One change in a Proposal's review list.</summary>
/// <param name="EntryKey">Stable within the Proposal: the change id of a word change, else the first operation id.</param>
/// <param name="Ordinal">The entry's position in the walking order, from zero.</param>
/// <param name="Title">What a person reads to recognise the change.</param>
/// <param name="OperationIds">The Proposal operations this entry owns.</param>
/// <param name="DependsOnEntries">The entry keys this entry needs to remain present.</param>
/// <param name="ContentDigest">The RFC 8785 digest of this entry's operations.</param>
/// <param name="LookedAt">Whether the entry is marked looked at against the current content and evidence.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WordChangeEntry), "word")]
[JsonDerivedType(typeof(GrammarChangeEntry), "grammar")]
public abstract record ReviewEntry(
    string EntryKey,
    int Ordinal,
    string Title,
    IReadOnlyList<string> OperationIds,
    IReadOnlyList<string> DependsOnEntries,
    string ContentDigest,
    bool LookedAt)
{
    /// <summary>The distinct sources this entry's operations cite, in operation order; empty when none cite one.</summary>
    public IReadOnlyList<CitationView> Citations { get; init; } = [];
}

/// <summary>
/// A change to one word's analyses: the authored change and its operations, how it fits the project now, and the
/// reading it addresses, today and, when a Trial measured it, with the Proposal.
/// </summary>
public sealed record WordChangeEntry(
    string EntryKey,
    int Ordinal,
    string Title,
    IReadOnlyList<string> OperationIds,
    IReadOnlyList<string> DependsOnEntries,
    string ContentDigest,
    bool LookedAt,
    PendingChange Change,
    ChangeFit? Fit,
    Paired<ParserReading?> Reading)
    : ReviewEntry(EntryKey, Ordinal, Title, OperationIds, DependsOnEntries, ContentDigest, LookedAt);

/// <summary>
/// A change to the grammar or lexicon, one composer call or one unclaimed operation. It names the composer (or the
/// operation kind when none produced it), a summary sentence, the effects the Trial's Dry Run observed for its
/// operations, and, for a retirement, the approved readings it affects and its connected parts.
/// </summary>
public sealed record GrammarChangeEntry(
    string EntryKey,
    int Ordinal,
    string Title,
    IReadOnlyList<string> OperationIds,
    IReadOnlyList<string> DependsOnEntries,
    string ContentDigest,
    bool LookedAt,
    string Composer,
    string Summary,
    IReadOnlyList<EffectView> Effects,
    IReadOnlyList<RetirementAffectedReading> AffectedReadings,
    IReadOnlyList<RetirementReviewPart> Parts)
    : ReviewEntry(EntryKey, Ordinal, Title, OperationIds, DependsOnEntries, ContentDigest, LookedAt);

/// <summary>The outcome of marking one review entry looked at, or clearing the mark.</summary>
/// <param name="ProposalId">The Proposal the entry belongs to.</param>
/// <param name="EntryKey">The entry the mark names.</param>
/// <param name="LookedAt">Whether the entry is now marked.</param>
public sealed record ReviewMarkResponse(string ProposalId, string EntryKey, bool LookedAt);
