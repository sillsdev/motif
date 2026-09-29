using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Responses;

/// <summary>One operation inside the <c>show</c> report, with every id rendered as its text form.</summary>
/// <param name="OperationId">The operation's identity within the Proposal.</param>
/// <param name="Kind">The semantic operation kind that names the requested change.</param>
/// <param name="Target">The target entity's portable identity, when the operation has a target.</param>
/// <param name="EntityId">The portable identity of an entity created by the operation, when present.</param>
/// <param name="DependsOn">The operation identities that this operation declares as prerequisites.</param>
/// <param name="AfterJson"><c>null</c> when the operation carries no after-state at all.</param>
public sealed record ProposalOperationView(
    string OperationId,
    string Kind,
    string? Target,
    string? EntityId,
    IReadOnlyList<string> DependsOn,
    string? AfterJson);

/// <summary>The <c>show</c> report: a Proposal's review state and its full operation list.</summary>
/// <param name="ProposalId">The Proposal's portable identity.</param>
/// <param name="Status">The Proposal's current lifecycle status.</param>
/// <param name="Label">The optional name shown for this Proposal.</param>
/// <param name="Comment">The optional explanatory comment attached to this Proposal.</param>
/// <param name="CurrentIntentDigest">
/// <c>null</c> for a Draft: it has no committed revision yet, so it has no digest.
/// </param>
/// <param name="Operations">The operation views included in the report.</param>
/// <param name="SupersededBy">The replacement Proposal's identity, or <c>null</c> when there is none.</param>
/// <param name="ExtensionsJson">Optional JSON containing non-semantic extension data.</param>
public sealed record ProposalDetailProjection(
    string ProposalId,
    string Status,
    string? Label,
    string? Comment,
    string? CurrentIntentDigest,
    IReadOnlyList<ProposalOperationView> Operations,
    string? SupersededBy = null,
    string? ExtensionsJson = null);
