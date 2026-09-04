using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using ProjectionText = SIL.Motif.Projection.Rendering.CommandTextRenderer;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// Turns a Proposal command's typed <see cref="CommandOutcome{T}"/> into the same human text and
/// <c>--json</c> shape the pre-typed handlers used to render themselves. A <see cref="Refusal"/>
/// becomes a <see cref="FailureEnvelope"/> only here — a command handler never constructs one.
/// </summary>
public static class ProposalCommandRenderer
{
    /// <param name="successAsJson">
    /// Whether a successful result renders as JSON. Several verbs never grew a JSON success shape
    /// (their usage text does not advertise <c>--json</c>) and must keep printing human text on
    /// success even when the caller passed <c>--json</c>; a refusal always follows <paramref name="asJson"/>
    /// regardless, matching every verb's documented failure contract.
    /// </param>
    public static CommandResult Render<T>(CommandOutcome<T> outcome, bool asJson, bool successAsJson = true)
        where T : class =>
        outcome.Succeeded
            ? RenderSuccess(outcome.Value!, asJson && successAsJson)
            : RenderRefusal(outcome.Refusal!, asJson);

    private static CommandResult RenderSuccess<T>(T value, bool asJson) where T : class
    {
        if (asJson)
            return new CommandResult(0, ProjectionJson.Serialize(value) + Environment.NewLine);

        var text = value switch
        {
            ProjectSummaryProjection p => ProjectionText.Render(p),
            AnalysisAggregateProjection p => ProjectionText.Render(p),
            ProposalListProjection p => ProjectionText.Render(p),
            ProposalDetailProjection p => ProjectionText.Render(p),
            ApplyProjection p => ProjectionText.Render(p),
            AppliedLogProjection p => ProjectionText.Render(p),
            DraftCreatedResponse r => RenderDraftCreated(r),
            SetGlossAddedResponse r => RenderSetGlossAdded(r),
            DeleteLexemeFormAddedResponse r => RenderDeleteLexemeFormAdded(r),
            ComposedOperationsResponse r => RenderComposedOperations(r),
            PromoteGlossAddedResponse r => RenderPromoteGlossAdded(r),
            DraftFieldChangedResponse r => $"Set {r.Field} on draft '{r.DraftName}'.{Environment.NewLine}",
            ProposalFinalizedResponse r => RenderProposalFinalized(r),
            DraftDiscardedResponse r => RenderDraftDiscarded(r),
            ReopenedResponse r => RenderReopened(r),
            DuplicatedResponse r => RenderDuplicated(r),
            OperationsRemovedResponse r => RenderOperationsRemoved(r),
            ProposalSplitResponse r => RenderProposalSplit(r),
            ProposalStatusChangedResponse r => $"Proposal {r.ProposalId} is now '{r.Status}'.{Environment.NewLine}",
            _ => throw new NotSupportedException($"No text rendering registered for '{typeof(T)}'."),
        };
        return new CommandResult(0, text);
    }

    private static CommandResult RenderRefusal(Refusal refusal, bool asJson)
    {
        if (asJson)
        {
            var envelope = new FailureEnvelope(
                refusal.Reason, refusal.Message, refusal.Facts.Count > 0 ? refusal.Facts : null, refusal.Code);
            return new CommandResult(
                FailureEnvelope.ExitCodeFor(refusal.Reason),
                ProjectionJson.Serialize(envelope) + Environment.NewLine, refusal.Reason);
        }

        return new CommandResult(
            FailureEnvelope.ExitCodeFor(refusal.Reason), "error: " + refusal.Message + Environment.NewLine,
            refusal.Reason);
    }

    private static string RenderDraftCreated(DraftCreatedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Created draft '{r.DraftName}'.");
        sb.AppendLine($"  proposalId: {r.ProposalId}");
        if (r.Label is not null) sb.AppendLine($"  label:       {r.Label}");
        return sb.ToString();
    }

    private static string RenderSetGlossAdded(SetGlossAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Added operation '{r.OperationId}' ({LexicalSenseOperationKinds.SetGloss}) to draft '{r.DraftName}'.");
        sb.AppendLine($"  target: {r.Target}");
        sb.AppendLine($"  after:  ws={r.Ws} text=\"{r.Text}\"");
        if (r.DependsOn.Count > 0) sb.AppendLine($"  dependsOn: {string.Join(", ", r.DependsOn)}");
        sb.AppendLine($"Draft now has {r.OperationCount} operation(s).");
        return sb.ToString();
    }

    private static string RenderDeleteLexemeFormAdded(DeleteLexemeFormAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Added operation '{r.OperationId}' ({LexEntryLexemeFormOperationKinds.DeleteLexemeForm}) to draft " +
            $"'{r.DraftName}'.");
        sb.AppendLine($"  target: {r.Target}");
        sb.AppendLine($"Draft now has {r.OperationCount} operation(s).");
        return sb.ToString();
    }

    private static string RenderComposedOperations(ComposedOperationsResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Composed '{r.ComposerName}' against draft '{r.DraftName}': {r.Operations.Count} operation(s) added.");
        foreach (var op in r.Operations)
            sb.AppendLine($"  {op.OperationId}  ({op.Kind})");
        sb.AppendLine($"Draft now has {r.OperationCount} operation(s).");
        return sb.ToString();
    }

    private static string RenderPromoteGlossAdded(PromoteGlossAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Added operation '{r.OperationId}' ({LexicalSenseOperationKinds.SetGloss}) to draft '{r.DraftName}', " +
            $"promoted from corpus '{r.CorpusId}'.");
        sb.AppendLine($"  target: {r.Target}");
        sb.AppendLine($"  after:  ws={r.Ws} text=\"{r.Text}\"");
        if (r.Licence is not null) sb.AppendLine($"  licence: {r.Licence}");
        sb.AppendLine($"Draft now has {r.OperationCount} operation(s).");
        return sb.ToString();
    }

    private static string RenderProposalFinalized(ProposalFinalizedResponse r)
    {
        var sb = new StringBuilder();
        if (r.IsAmend)
        {
            sb.AppendLine($"Amended draft '{r.DraftName}' -> Proposal {r.ProposalId} (status: proposed).");
            sb.AppendLine("  (id unchanged; intentDigest moved to a new revision; prior revision retained)");
        }
        else
        {
            sb.AppendLine($"Finalized draft '{r.DraftName}' -> Proposal {r.ProposalId} (status: proposed).");
        }
        sb.AppendLine($"  operations:   {r.OperationCount}");
        sb.AppendLine($"  intentDigest: {r.IntentDigest}");
        return sb.ToString();
    }

    private static string RenderDraftDiscarded(DraftDiscardedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Discarded draft '{r.DraftName}'.");
        if (r.WasReopened)
        {
            sb.AppendLine(
                "  (it was reopened from a finalized Proposal, which remains at its prior committed revision)");
        }
        return sb.ToString();
    }

    private static string RenderReopened(ReopenedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Reopened Proposal {r.ProposalId} for editing as draft '{r.DraftName}'.");
        if (r.CurrentIntentDigest is not null) sb.AppendLine($"  currentIntentDigest: {r.CurrentIntentDigest}");
        sb.AppendLine($"  operations:          {r.OperationCount}");
        sb.AppendLine(
            "Finalizing this draft will amend the Proposal: same id, new intentDigest, status reset to proposed.");
        return sb.ToString();
    }

    private static string RenderDuplicated(DuplicatedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Duplicated Proposal {r.SourceProposalId} into new draft '{r.DraftName}'.");
        sb.AppendLine($"  proposalId: {r.ProposalId}  (a new Proposal; the source is untouched)");
        sb.AppendLine($"  operations: {r.OperationCount}");
        sb.AppendLine("Finalizing this draft will commit it as a brand-new Proposal.");
        return sb.ToString();
    }

    private static string RenderOperationsRemoved(OperationsRemovedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Removed {DescribeOperationIds(r.RemovedOperationIds)} from draft '{r.DraftName}'.");
        if (r.ForcedDependents.Count > 0)
        {
            sb.AppendLine($"  --force also removed {r.ForcedDependents.Count} dependent operation(s) named above:");
            foreach (var dependent in r.ForcedDependents)
                sb.AppendLine($"  - {dependent.OperationId} ({dependent.Kind})");
        }
        sb.AppendLine($"Draft now has {r.OperationCount} operation(s).");
        sb.AppendLine(
            "Run 'finalize' to commit this as a new revision (an amend, clearing any bound-DryRun " +
            "anchor, if this draft came from 'reopen').");
        return sb.ToString();
    }

    private static string RenderProposalSplit(ProposalSplitResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Split Proposal {r.SourceProposalId} into {r.Drafts.Count} new draft(s).");
        if (r.ForcedSeveredEdgeCount > 0)
        {
            sb.AppendLine(
                $"  --force accepted {r.ForcedSeveredEdgeCount} severed dependency edge(s) named above.");
        }
        foreach (var draft in r.Drafts)
            sb.AppendLine($"  draft '{draft.DraftName}': proposalId={draft.ProposalId} operations={draft.OperationCount}");
        sb.AppendLine($"  (the source Proposal {r.SourceProposalId} is unchanged)");
        sb.AppendLine("Finalize each draft to commit it as a brand-new Proposal.");
        return sb.ToString();
    }

    private static string DescribeOperationIds(IReadOnlyList<string> ids) =>
        ids.Count == 1 ? $"operation '{ids[0]}'" : $"operations {string.Join(", ", ids.Select(i => $"'{i}'"))}";
}
