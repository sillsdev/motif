using SIL.Motif.Commands.Store;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Runner.Operations;
using SIL.LCModel;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Worker.Baselines;
using System.Text.Json;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Requires the newest Trial's current green gate for Autonomous Apply (ADR 0017 and ADR 0021).</summary>
internal static class AutonomousTrialGate
{
    // A Guide-only or Notes-only Proposal can lose no word, so its Trial has nothing to gate.
    private static bool ExemptProposal(ProjectLocator project, ProposalRecord proposal)
    {
        if (proposal.ProposalJson is not { } json) return false;
        var operations = ProposalJsonParser.Parse(json).Operations;
        if (operations.All(operation => operation.Kind != EditTextNotesOperationKinds.Set) &&
            operations.All(operation => operation.Target is null)) return false;
        var sections = ProjectReadCache.ReadSaved(project, (_, cache) => GdrReader.List(cache)
            .Select(item => SIL.Motif.Runner.Operations.OperationSlotRegistry.ByGuid(SIL.Motif.Contract.Ids.CanonicalId.Parse(item.Id)).Value)
            .ToHashSet(StringComparer.Ordinal));
        return GuideNotesOnlyProposal.ExemptionFor(operations, sections) is not null;
    }

    internal static Refusal? Check(MotifDatabase database, ProjectLocator project, ProposalRecord proposal, LcmCache? live = null)
    {
        var key = ProjectWorkspaceKey.Compute(project);
        string? jobId = null;
        Refusal Refuse(string reason) => new(RefusalCodes.ApplyTrialGate, FailureReason.Refused,
            "Autonomous Apply requires the newest Trial of this Proposal revision and project state to have a green gate. " +
            reason + " Run a fresh Trial with --wait, review its named losses and stopped words, and use --expect-loss " +
            "only for intended losses when starting that Trial.",
            new Dictionary<string, string> { ["proposalId"] = proposal.ProposalId.Value, ["jobId"] = jobId ?? "" });
        try
        {
            if (ExemptProposal(project, proposal)) return null;
            var configuration = new ProjectConfigurationReader().Read(project);
            if (configuration.GateWordSets is null)
            {
                var existing = configuration.ReviewWordSets.FirstOrDefault();
                var declaration = "[gate]\nword-sets = [\"" + (existing?.Id ?? "gold") + "\"]";
                if (existing is null)
                    declaration += "\n\n[[review-word-set]]\nid = \"gold\"\nname = \"Gold words\"\nrole = \"regression\"\nmanifest = \"gold.jsonl\"";
                return new Refusal(RefusalCodes.ApplyGateUndeclared, FailureReason.Refused,
                    "Autonomous Apply requires a declared gate. Add this TOML to " + ProjectConfigurationReader.PathFor(project) +
                    ":\n" + declaration + "\nSupply the named word-set manifest (or use selection = \"Your saved Selection\" instead of manifest), " +
                    "then run a fresh Trial. The gate reuses [gain-gate] and [parsimony] block-on.");
            }
            if (Trials.SelectJob(database, key, proposal.ProposalId.Value) is { } job)
            {
                jobId = job.JobId;
                if (TrialSampleKind.IsWorkingSet(TrialJobInput.Parse(job.InputJson).Sample))
                    return Refuse("The newest Trial is a working-set Trial that measured only a chosen word list, so it cannot " +
                        "authorize Apply. Run a Trial without --no-base-sample.");
                if (job.Status != JobStatus.Completed)
                    return Refuse("The newest Trial is " + JobStatusJson.ToWire(job.Status) + ".");
                var read = new TrialReviewCommands(database).Read(new ReviewReadRequest(key, proposal.ProposalId.Value, JobId: job.JobId));
                if (!read.Succeeded || read.Value is not AvailableTrialResult { Gate: { } gate } available)
                    return Refuse("The newest Trial has no retained gate verdict; older evidence cannot authorize this Apply.");
                if (available.Report.Pins.IntentDigest != proposal.IntentDigest ||
                    available.Report.Freshness.Relation != FreshnessRelation.Current)
                    return Refuse("The newest Trial's evidence is historical or its current identity cannot be verified.");
                if (available.Report.DeclaredGate is not { } declaration ||
                    !DeclaredTrialGatePolicy.Current(database, project, configuration, declaration))
                    return Refuse("The Trial did not capture the current declared gate scope and policy.");
                if (live is not null)
                {
                    var baseline = available.Report.Pins.BaselineToken;
                    var input = TrialJobInput.Parse(job.InputJson);
                    var captured = input.ReviewInputs is { } reference ? ReviewInputArtifacts.Read(database, reference) : null;
                    if (baseline.ProjectionVersion != BaselineSemanticDigest.ProjectionVersion ||
                        baseline.ProjectIdentity != live.LangProject.Guid.ToString("D") ||
                        baseline.SemanticSnapshotDigest != BaselineSemanticDigest.Compute(live) ||
                        captured?.Baseline != baseline || captured.BaselineSidecarDigest is not { } measuredSidecars ||
                        ProgressSourceIdentity.CaptureSidecars(project.FullFwDataPath) is not { } liveSidecars ||
                        measuredSidecars != liveSidecars.Digest)
                        return Refuse("The loaded live project's semantic state or writing-system files differ from the Trial Baseline.");
                }
                return gate.State == TrialGateState.Green ? null : Refuse("The newest Trial's gate is " +
                    gate.State.ToString().ToLowerInvariant() + ": " + (gate.Reason ?? "Read the Trial report."));
            }
            return Refuse("No Trial exists for this Proposal.");
        }
        catch (Exception exception) when (exception is JsonException or ContractParseException or InvalidDataException or
            InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or IOException or UnauthorizedAccessException or ProjectConfigurationException)
        {
            return Refuse("The retained Trial evidence could not be read: " + exception.Message);
        }
    }
}
