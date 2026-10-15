using SIL.LCModel;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Commands;

/// <summary>What a composer definition's lowering may use besides its parsed intent.</summary>
/// <param name="Cache">The project cache the intent is resolved against, already holding earlier steps' changes.</param>
/// <param name="ProposalId">The id of the Draft's Proposal, for composers that name it in what they author.</param>
/// <param name="MintId">Produces a fresh canonical id for an entity or operation the composer creates.</param>
internal sealed record ComposeContext(LcmCache Cache, CanonicalId ProposalId, Func<CanonicalId> MintId);
