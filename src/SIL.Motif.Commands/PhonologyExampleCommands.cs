using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
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

    private static CommandOutcome<T> Invalid<T>(string message) where T : class? =>
        CommandOutcome<T>.Refused(new Refusal(
            "phonology-examples.invalid", FailureReason.InvalidArgument, message));
}
