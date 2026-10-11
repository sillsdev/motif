namespace SIL.Motif.Contract.Requests;

/// <summary>Marks one entry of a Proposal's review list as looked at, or clears the mark.</summary>
/// <param name="ProjectPath">The FieldWorks project whose Motif store holds the Proposal.</param>
/// <param name="ProposalId">The Proposal the entry belongs to.</param>
/// <param name="EntryKey">The entry's stable key within the Proposal's review list.</param>
/// <param name="ContentDigest">The entry's content digest when it was looked at.</param>
/// <param name="EvidenceDigest">The Trial evidence the entry was judged against.</param>
/// <param name="LookedAt">True to record the mark, false to clear it.</param>
public sealed record ReviewMarkRequest(
    string ProjectPath, string ProposalId, string EntryKey, string ContentDigest, string EvidenceDigest,
    bool LookedAt);
