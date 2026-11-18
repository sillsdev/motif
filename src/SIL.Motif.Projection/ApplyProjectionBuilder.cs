using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Receipts;
using System.Collections.Generic;

namespace SIL.Motif.Projection;

/// <summary>
/// Shapes a Runner <see cref="Receipt"/> into an <see cref="ApplyProjection"/>, carrying any cited-operation rows
/// supplied for the Applied Proposal.
/// </summary>
public static class ApplyProjectionBuilder
{
    public static ApplyProjection Build(string proposalId, Receipt receipt, ReadinessProjection? readiness = null,
        IReadOnlyList<CitedOperationRow>? citations = null, IReadOnlyList<string>? operationKinds = null)
    {
        var effects = EffectProjectionBuilder.Build(receipt.ActualEffects);
        return new ApplyProjection(
            proposalId,
            receipt.AlreadyApplied,
            receipt.ResultNote,
            effects,
            receipt.EffectDigest,
            new AppliedLogEntrySummary(
                receipt.AppliedLogEntry.ProposalId.ToString("D"),
                receipt.AppliedLogEntry.TimestampUtc,
                receipt.AppliedLogEntry.User,
                receipt.AppliedLogEntry.IntentDigest,
                receipt.Mode,
                receipt.Message),
            readiness,
            citations)
        {
            OperationKinds = operationKinds ?? [],
            Warnings = DryRunProjectionBuilder.ExtractWarnings(effects),
        };
    }
}
