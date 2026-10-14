using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>Identifies the persisted Baseline, Selection, parser, and time behind a grammar check.</summary>
/// <param name="BaselineToken">The exact Baseline identity stored with the check.</param>
/// <param name="SelectionSha256">The Selection digest stored with the check.</param>
/// <param name="ParserStamp">The parser identity stored with the check, when available.</param>
/// <param name="CheckedUtc">The time stored with the check.</param>
/// <param name="SourceLastWriteUtc">The source save time, or null when it was not stored with the check.</param>
/// <param name="Trial">The Proposal revision identity for a Trial check, or null for a project check.</param>
public sealed record GrammarCheckProvenance(
    BaselineToken BaselineToken,
    string SelectionSha256,
    string? ParserStamp,
    DateTimeOffset CheckedUtc,
    DateTimeOffset? SourceLastWriteUtc,
    TrialGrammarCheckIdentity? Trial);

/// <summary>Identifies the exact Proposal revision measured by a Trial grammar check.</summary>
/// <param name="ProposalId">The canonical Proposal id stored with the check.</param>
/// <param name="IntentDigest">The Proposal revision digest stored with the check.</param>
public sealed record TrialGrammarCheckIdentity(string ProposalId, string IntentDigest);
