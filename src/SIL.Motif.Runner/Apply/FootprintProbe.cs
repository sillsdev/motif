using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.LCModel;

namespace SIL.Motif.Runner.Apply;

/// <summary>
/// Reads a Proposal's CURRENT footprint — the live, pre-mutation state of every target its
/// operations touch — without mutating anything or opening any unit of work. Used by
/// <see cref="ProposalApplier.Apply"/>'s pre-flight drift check (ADR 0004 decision 3): a bare read is
/// legal at any transaction state (ADR 0006 decision 1), so this probe can run before Apply opens its
/// own committing unit of work.
/// </summary>
/// <remarks>
/// Mirrors the dispatch in <see cref="SIL.Motif.Runner.DryRun.ProposalDryRunner"/> and
/// <see cref="ProposalApplier"/> (an <see cref="OperationHandlerRegistry"/> lookup per operation), but
/// calls each handler's non-mutating <c>ReadCurrentFootprint</c> rather than its
/// resolve/snapshot/lower/snapshot sequence.
/// <para>
/// <b>A sense-removal on an object an earlier merge moves is read without the owner check.</b> It is composed
/// against the state the merge leaves, so it names the survivor as the sense's owner, which the unmerged project does not
/// give it. Its content (notes, examples, subsenses, analyses) is still read and bound, so a concurrent edit is drift;
/// only the expectation that the survivor already owns the sense is relaxed. Both sides of the drift check use this rule.
/// </para>
/// </remarks>
public static class FootprintProbe
{
    public static string ComputeCurrentFootprintDigest(LcmCache cache, Proposal proposal)
    {
        var entries = new List<ExpectedEffect>();
        var mintedTargets = FootprintPlan.TargetsMintedWithinProposal(proposal);
        var movedByEarlierOperation = OperationsOnObjectsMovedByEarlierMerges(cache, proposal);

        foreach (var operation in proposal.Operations)
        {
            // A target this Proposal mints does not exist yet, and has no prior state to have drifted from.
            if (!FootprintPlan.ParticipatesInFootprint(operation, mintedTargets)) continue;
            // Checked here, not caught from the handler, so a handler defect is never reported as stale evidence.
            if (operation.Target is { } target &&
                !cache.ServiceLocator.ObjectRepository.TryGetObject(target.ToGuid(), out _))
            {
                throw new PreconditionRefusal(
                    PreconditionKind.TargetGone,
                    $"Operation {operation.OperationId.Value} ({operation.Kind}) targets {target.Value} ({target.ToGuid()}), which the Dry Run " +
                    "found but which no longer exists in the project. Run a fresh Dry Run before applying.",
                    new Dictionary<string, string>
                    {
                        ["operationId"] = operation.OperationId.Value,
                        ["target"] = target.Value,
                    });
            }

            var handler = OperationHandlerRegistry.Resolve(operation.Kind, "Apply's footprint pre-flight");
            entries.AddRange(movedByEarlierOperation.Contains(operation.OperationId)
                ? LexSenseRemoveHandler.Instance.ReadMovedFootprintEffects(cache, operation)
                : OperationEffectCapture.ReadFootprint(handler, cache, operation));
        }

        entries.AddRange(AffixAuthoringPrerequisites.Read(cache, proposal));
        entries.AddRange(AffixProcessPrerequisiteFootprint.Read(cache, proposal));
        return FootprintDigest.Compute(entries);
    }

    /// <summary>The operations on an object an earlier merge moves onto its survivor.</summary>
    private static HashSet<CanonicalId> OperationsOnObjectsMovedByEarlierMerges(LcmCache cache, Proposal proposal)
    {
        var skipped = new HashSet<CanonicalId>(CanonicalId.ByGuid);
        if (!proposal.Operations.Any(operation => operation.Kind == LexEntryMergeOperationKinds.Merge)) return skipped;

        var moved = new HashSet<CanonicalId>(CanonicalId.ByGuid);
        foreach (var operation in OperationExecutionOrder.Sort(proposal.Operations))
        {
            if (operation.Target is not { } target) continue;
            if (operation.Kind == LexSenseRemoveOperationKinds.Remove && moved.Contains(target)) skipped.Add(operation.OperationId);
            if (operation.Kind == LexEntryMergeOperationKinds.Merge &&
                cache.ServiceLocator.ObjectRepository.TryGetObject(target.ToGuid(), out var merged))
            {
                foreach (var owned in merged.AllOwnedObjects)
                    moved.Add(CanonicalId.FromGuid(owned.Guid));
            }
        }

        return skipped;
    }
}
