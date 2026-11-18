using System.Globalization;
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
    internal static Refusal? Check(MotifDatabase database, ProjectLocator project, ProposalRecord proposal)
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
            var jobs = new JobRepository(database).ListByProjectAndKind(key, "trial")
                .OrderByDescending(job => DateTimeOffset.Parse(job.CreatedUtc, CultureInfo.InvariantCulture))
                .ThenByDescending(job => job.JobId, StringComparer.Ordinal);
            foreach (var job in jobs)
            {
                var input = TrialJobInput.Parse(job.InputJson);
                if (ProposalJsonParser.Parse(input.ProposalJson).ProposalId != proposal.ProposalId) continue;
                jobId = job.JobId;
                if (job.Status != JobStatus.Completed)
                    return Refuse("The newest Trial is " + JobStatusJson.ToWire(job.Status) + ".");
                var read = new TrialReviewCommands(database).Read(new ReviewReadRequest(key, proposal.ProposalId.Value, JobId: job.JobId));
                if (!read.Succeeded || read.Value is not AvailableTrialResult { Gate: { } gate } available)
                    return Refuse("The newest Trial has no retained gate verdict; older evidence cannot authorize this Apply.");
                if (available.Report.Pins.IntentDigest != proposal.IntentDigest ||
                    available.Report.Freshness.Relation != FreshnessRelation.Current)
                    return Refuse("The newest Trial's evidence is historical or its current identity cannot be verified.");
                return gate.State == TrialGateState.Green ? null : Refuse("The newest Trial's gate is " +
                    gate.State.ToString().ToLowerInvariant() + ": " + (gate.Reason ?? "Read the Trial report."));
            }
            return Refuse("No Trial exists for this Proposal.");
        }
        catch (Exception exception) when (exception is JsonException or ContractParseException or InvalidDataException or
            InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException)
        {
            return Refuse("The retained Trial evidence could not be read: " + exception.Message);
        }
    }
}
