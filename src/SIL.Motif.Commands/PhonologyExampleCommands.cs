using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Saves and reads cited phonology Example Set revisions in a project's Motif store.</summary>
public static class PhonologyExampleCommands
{
    /// <summary>Saves a new Example Set revision or advances the named set at its exact current revision.</summary>
    /// <param name="request">The project, immutable set content, and optional revision and Proposal bindings.</param>
    public static CommandOutcome<PhonologyExamplesSavedResponse> Save(SavePhonologyExamplesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatedPhonologyExamples validated;
        try
        {
            validated = PhonologyExampleValidation.ValidateAndCopy(request.Name, request.Examples,
                request.ExpectedRevisionId, request.ProposalId);
        }
        catch (ArgumentException exception)
        {
            return Invalid<PhonologyExamplesSavedResponse>(exception.Message);
        }

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            try
            {
                var saved = new PhonologyExampleSetRepository(database).Save(validated.Name,
                    validated.Examples, validated.ExpectedRevisionId, validated.ProposalId);
                return CommandOutcome<PhonologyExamplesSavedResponse>.Success(
                    new PhonologyExamplesSavedResponse(saved));
            }
            catch (PhonologyExampleSetRevisionConflictException exception)
            {
                return CommandOutcome<PhonologyExamplesSavedResponse>.Refused(new Refusal(
                    "phonology-examples.revision-conflict", FailureReason.Refused, exception.Message));
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<PhonologyExamplesSavedResponse>.Refused(new Refusal(
                    "phonology-examples.proposal-not-found", FailureReason.NotFound, exception.Message));
            }
            catch (ArgumentException exception)
            {
                return Invalid<PhonologyExamplesSavedResponse>(exception.Message);
            }
        });
    }

    /// <summary>Lists current Example Set revisions for the project in stable identifier order.</summary>
    /// <param name="request">The project, bounded page size, and optional project-scoped cursor.</param>
    public static CommandOutcome<PhonologyExamplesListResponse> List(ListPhonologyExamplesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            PhonologyExampleValidation.ValidatePageSize(request.PageSize);
        }
        catch (ArgumentException exception)
        {
            return Invalid<PhonologyExamplesListResponse>(exception.Message);
        }

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            try
            {
                return CommandOutcome<PhonologyExamplesListResponse>.Success(
                    new PhonologyExampleSetRepository(database).List(request.PageSize, request.Continuation));
            }
            catch (ArgumentException exception)
            {
                return Invalid<PhonologyExamplesListResponse>(exception.Message);
            }
        });
    }

    /// <summary>Reads one immutable Example Set revision and a bounded page of its examples.</summary>
    /// <param name="request">The project, exact revision, page size, and optional revision-scoped cursor.</param>
    public static CommandOutcome<PhonologyExamplesPageResponse> Show(ShowPhonologyExamplesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            PhonologyExampleValidation.ValidateRevisionId(request.RevisionId);
            PhonologyExampleValidation.ValidatePageSize(request.PageSize);
            PhonologyExampleValidation.ValidatePageCursor(request.Continuation, request.RevisionId);
        }
        catch (ArgumentException exception)
        {
            return Invalid<PhonologyExamplesPageResponse>(exception.Message);
        }

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            try
            {
                var page = new PhonologyExampleSetRepository(database).Read(request.RevisionId,
                    request.PageSize, request.Continuation);
                return CommandOutcome<PhonologyExamplesPageResponse>.Success(page);
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<PhonologyExamplesPageResponse>.Refused(new Refusal(
                    "phonology-examples.revision-not-found", FailureReason.NotFound, exception.Message));
            }
            catch (ArgumentException exception)
            {
                return Invalid<PhonologyExamplesPageResponse>(exception.Message);
            }
        });
    }

    /// <summary>Queues an advisory check of one saved revision against the current Baseline's grammar.</summary>
    /// <param name="request">The project, exact revision, optional Baseline binding and optional ceilings.</param>
    public static CommandOutcome<JobEnqueuedResponse> Check(PhonologyCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limits = request.Limits ?? PhonologyExampleValidation.DefaultLimits;
        try
        {
            PhonologyExampleValidation.ValidateRevisionId(request.RevisionId);
        }
        catch (ArgumentException exception)
        {
            return Invalid<JobEnqueuedResponse>(exception.Message);
        }
        if (!PhonologyExampleValidation.AreLimitsInRange(limits))
            return Invalid<JobEnqueuedResponse>("The step, work and time ceilings must be positive and at most " +
                $"{PhonologyExampleValidation.MaxStepCap.ToString(CultureInfo.InvariantCulture)}, " +
                $"{PhonologyExampleValidation.MaxWorkCap.ToString(CultureInfo.InvariantCulture)} and " +
                $"{PhonologyExampleValidation.MaxTimeoutMs.ToString(CultureInfo.InvariantCulture)} milliseconds.");

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            try
            {
                new PhonologyExampleSetRepository(database).Read(request.RevisionId, 1, null);
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                    "phonology-examples.revision-not-found", FailureReason.NotFound, exception.Message));
            }
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var current = new BaselineRepository(database).GetCurrentEvidence(workspaceKey);
            if (current is null)
                return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal("phonology-examples.no-baseline",
                    FailureReason.Refused, "No Baseline is recorded. Capture a Baseline before checking phonology examples."));
            if (request.Baseline is not null && request.Baseline != current.Baseline.Token)
                return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal("phonology-examples.baseline-changed",
                    FailureReason.Refused, "The named Baseline is no longer the current one; check against the current Baseline."));
            var jobId = CanonicalId.Mint("job/").Value;
            var input = JsonSerializer.Serialize(new PhonologyCheckJobInput(request.RevisionId,
                current.Baseline.Token, limits), MotifJson.CreateOptions());
            var job = new JobRepository(database).Create(jobId, workspaceKey, PhonologyCheckJobHandler.JobKind, input,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(job.JobId, job.Kind, workspaceKey));
        });
    }

    /// <summary>Reads the stored result of one phonology check as a summary page or one complete example.</summary>
    /// <param name="request">The project, the check's Assessment id, and the closed page or example selection.</param>
    public static CommandOutcome<PhonologyResultsResponse> Results(PhonologyResultsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Read is null)
            return Invalid<PhonologyResultsResponse>("A results request names a page or an example to read.");
        if (request.Read is PhonologyResultsRead.Page { PageSize: var pageSize, Continuation: var continuation })
        {
            try
            {
                PhonologyExampleValidation.ValidatePageSize(pageSize);
                PhonologyExampleValidation.ValidatePageCursor(continuation, request.AssessmentId);
            }
            catch (ArgumentException exception)
            {
                return Invalid<PhonologyResultsResponse>(exception.Message);
            }
        }

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var results = new PhonologyAssessmentRepository(database);
            try
            {
                PhonologyResultsResponse response = request.Read switch
                {
                    PhonologyResultsRead.Page page =>
                        results.ReadPage(request.AssessmentId, page.PageSize, page.Continuation),
                    PhonologyResultsRead.Example example =>
                        results.ReadExample(request.AssessmentId, example.ExampleId),
                    _ => throw new ArgumentException("Unknown results selection."),
                };
                return CommandOutcome<PhonologyResultsResponse>.Success(response);
            }
            catch (KeyNotFoundException exception)
            {
                var code = exception is PhonologyExampleNotFoundException
                    ? "phonology-examples.example-not-found" : "phonology-examples.assessment-not-found";
                return CommandOutcome<PhonologyResultsResponse>.Refused(new Refusal(
                    code, FailureReason.NotFound, exception.Message));
            }
            catch (ArgumentException exception)
            {
                return Invalid<PhonologyResultsResponse>(exception.Message);
            }
        });
    }

    private static CommandOutcome<T> Invalid<T>(string message) where T : class? =>
        CommandOutcome<T>.Refused(new Refusal(
            "phonology-examples.invalid", FailureReason.InvalidArgument, message));
}
