using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Retirement;

/// <summary>Reads the exact retirement Draft and its available Dry Run evidence from the project store.</summary>
public static class RetirementProposalReviewQuery
{
    private static readonly JsonSerializerOptions DraftOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Returns a staged retirement review or the precise evidence that is still unavailable.</summary>
    public static CommandOutcome<RetirementReviewQueryResponse> Read(ReadRetirementReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationRegistryBootstrap.Initialize();
        if (!CanonicalId.TryParse(request.DraftId, out var requestedId, out var error))
            return Refused("retirement-review.invalid-draft", FailureReason.InvalidArgument,
                $"'{request.DraftId}' is not a valid Draft Proposal id: {error}");

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            ProposalRecord record;
            try
            {
                record = new ProposalRepository(database).ListDrafts()
                    .SingleOrDefault(item => item.ProposalId == requestedId)!;
            }
            catch (InvalidOperationException exception)
            {
                return Refused("retirement-review.draft-ambiguous", FailureReason.StoreInconsistent,
                    "More than one staged Draft names this Proposal id: " + exception.Message);
            }

            if (record is null)
                return Refused("retirement-review.draft-not-found", FailureReason.NotFound,
                    $"Draft Proposal '{request.DraftId}' was not found.");

            try
            {
                var draft = JsonSerializer.Deserialize<DraftDocument>(record.ProposalJson!, DraftOptions)
                    ?? throw new InvalidDataException("The Draft content is empty.");
                var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
                if (proposal.ProposalId != requestedId)
                    throw new InvalidDataException("The Draft content names another Proposal id.");
                var intentDigest = IntentDigest.Compute(proposal);
                var retirement = RetirementReviewEvidenceCapture.FindRetirementComposition(draft, proposal);
                if (retirement is null)
                    return CommandOutcome<RetirementReviewQueryResponse>.Success(new(false, request.DraftId,
                        "not-applicable", null, null, null, []));

                var unavailable = new List<string>();
                var dryRunJobs = FindDryRunJobs(database, project, request.DraftId);
                DryRunProjection? projection = null;
                JobRecord? latestDryRunJob = null;
                string? dryRunJobId = null;
                string? proposalContentSha256 = null;
                var state = "waiting-for-dry-run";

                if (dryRunJobs.Count > 0)
                {
                    var latest = dryRunJobs[^1];
                    latestDryRunJob = latest;
                    dryRunJobId = latest.JobId;
                    var frozenInput = DryRunJobInput.Parse(latest.InputJson);
                    if (frozenInput.Proposal.IntentDigest != intentDigest)
                        return Refused("retirement-review.stale-dry-run", FailureReason.Refused,
                            "The latest Dry Run describes an earlier Draft revision. Run Dry Run again for the current Draft.");
                    if (frozenInput.Proposal.ProposalId != request.DraftId)
                        throw new InvalidDataException("The latest Dry Run names another Proposal id.");
                    proposalContentSha256 = frozenInput.Proposal.ContentSha256;

                    if (latest.Status == JobStatus.CompletedDryRunOnly)
                    {
                        if (!latest.DryRunPublished || latest.DryRunJson is null)
                            throw new InvalidDataException("The completed Dry Run has no published effects.");
                        var dryRun = JobCommands.ParsePublishedDryRun(latest.DryRunJson);
                        if (dryRun.IntentDigest != intentDigest)
                            return Refused("retirement-review.stale-dry-run", FailureReason.Refused,
                                "The published Dry Run describes an earlier Draft revision. Run Dry Run again for the current Draft.");
                        projection = DryRunProjectionBuilder.Build(request.DraftId, dryRun, proposal);
                        state = "waiting-for-evidence";
                    }
                    else
                    {
                        unavailable.Add("The latest Dry Run has not completed yet.");
                    }
                }
                else
                {
                    unavailable.Add("A Dry Run is not available yet.");
                }

                if (projection is null)
                {
                    unavailable.Add("This Draft is not linked to the issue the rule is meant to address yet.");
                    unavailable.Add("The affected approved readings have not been recorded for this Draft yet.");
                    unavailable.Add("Before-and-after parser results are not available yet.");
                    return CommandOutcome<RetirementReviewQueryResponse>.Success(new(true, request.DraftId, state,
                        dryRunJobId, null, null,
                        Array.AsReadOnly(unavailable.Distinct(StringComparer.Ordinal).ToArray())));
                }

                var retiredIds = retirement.Retirements.SelectMany(item => item.RetiredForms)
                    .Select(item => CanonicalId.Parse(item.Id).ToGuid()).Distinct().ToArray();
                var footprint = ProjectReadCache.ReadSaved(project,
                    (_, cache) => AllomorphReferenceFootprintReader.Read(cache, retiredIds));
                if (footprint.Unavailable.Count > 0)
                    unavailable.AddRange(footprint.Unavailable);

                var candidateJob = FindCandidateJob(database, project, dryRunJobId);
                RetirementProposalReviewProjection? review = null;
                if (candidateJob is null || candidateJob.Status != JobStatus.Completed || candidateJob.ResultJson is null)
                {
                    unavailable.Add("Before-and-after parser results are not available yet.");
                }
                else
                {
                    var evidence = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(
                        candidateJob.ResultJson, MotifJson.CreateOptions())
                        ?? throw new InvalidDataException("The paired Parsimony Reports are empty.");
                    if (evidence.Candidate.ProposalId != request.DraftId ||
                        evidence.Candidate.ProposalIntentDigest != intentDigest ||
                        evidence.Candidate.ProposalContentSha256 != proposalContentSha256 ||
                        evidence.Candidate.DryRunJobId != dryRunJobId ||
                        evidence.Candidate.DryRunEffectDigest != projection.EffectDigest ||
                        evidence.Before.Inputs.BaselineToken != evidence.Candidate.BaselineToken ||
                        evidence.After.Inputs.BaselineToken != evidence.Candidate.BaselineToken ||
                        evidence.Before.Inputs.InputKind != "baseline" ||
                        evidence.After.Inputs.InputKind != "candidate")
                        throw new InvalidDataException("The paired Reports do not match this Draft and Dry Run.");
                    if (latestDryRunJob is null)
                        throw new InvalidDataException("Candidate Reports have no matching completed Dry Run.");
                    review = RetirementReviewProjectionAssembler.Build(database, draft, proposal, projection,
                        latestDryRunJob, candidateJob, retirement, footprint, evidence, unavailable);
                    if (review is not null)
                    {
                        unavailable.AddRange(review.Unavailable);
                        state = review.Unavailable.Count == 0 ? "review-ready" : "waiting-for-evidence";
                    }
                }

                return CommandOutcome<RetirementReviewQueryResponse>.Success(new(true, request.DraftId, state,
                    dryRunJobId, projection, review,
                    Array.AsReadOnly(unavailable.Distinct(StringComparer.Ordinal).ToArray())));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                               InvalidDataException or JsonException or FormatException or
                                               KeyNotFoundException or IOException or NotSupportedException)
            {
                return Refused("retirement-review.evidence-inconsistent", FailureReason.StoreInconsistent,
                    "The staged retirement review could not be read safely: " + exception.Message);
            }
        });
    }

    private static IReadOnlyList<JobRecord> FindDryRunJobs(MotifDatabase database,
        SIL.Motif.Contract.Projects.ProjectLocator project, string proposalId)
    {
        var workspace = ProjectWorkspaceKey.Compute(project);
        var matches = new List<JobRecord>();
        foreach (var job in new JobRepository(database).ListByProjectAndKind(workspace, JobCommands.DryRunKind))
        {
            try
            {
                var input = DryRunJobInput.Parse(job.InputJson);
                if (input.Proposal.ProposalId == proposalId) matches.Add(job);
            }
            catch (InvalidDataException)
            {
                using var document = JsonDocument.Parse(job.InputJson);
                if (document.RootElement.TryGetProperty("proposal", out var proposal) &&
                    proposal.TryGetProperty("proposalId", out var id) && id.GetString() == proposalId)
                    throw;
            }
        }
        return matches.OrderBy(item => item.CreatedUtc, StringComparer.Ordinal)
            .ThenBy(item => item.JobId, StringComparer.Ordinal).ToArray();
    }

    private static JobRecord? FindCandidateJob(MotifDatabase database,
        SIL.Motif.Contract.Projects.ProjectLocator project, string? dryRunJobId)
    {
        if (dryRunJobId is null) return null;
        return new JobRepository(database).ListByProjectAndKind(ProjectWorkspaceKey.Compute(project),
                ParsimonyCandidateEvidenceBuilder.JobKind)
            .Where(job =>
            {
                try
                {
                    using var document = JsonDocument.Parse(job.InputJson);
                    return document.RootElement.TryGetProperty("dryRunJobId", out var value) &&
                        value.GetString() == dryRunJobId;
                }
                catch (JsonException) { return false; }
            })
            .OrderBy(item => item.CreatedUtc, StringComparer.Ordinal)
            .ThenBy(item => item.JobId, StringComparer.Ordinal).LastOrDefault();
    }

    private static CommandOutcome<RetirementReviewQueryResponse> Refused(string code, FailureReason reason,
        string message) => CommandOutcome<RetirementReviewQueryResponse>.Refused(new Refusal(code, reason, message));
}
