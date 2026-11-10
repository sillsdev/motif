using SIL.Motif.Host;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Store;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>
    /// Adds a person's paragraph edit of one rich-text field to the pending-changes Draft, or updates the edit the
    /// Draft already holds for that field, creating the Draft when there is none (ADR 0095 decision 4).
    /// </summary>
    /// <remarks>
    /// An edit that changes and removes nothing takes the field's earlier edit out of the Draft. Each editable class
    /// has one editable field (<see cref="TextParagraphFields"/>), so the same target and kind is the same field.
    /// </remarks>
    public static CommandOutcome<ComposedOperationsResponse> StageTextParagraphs(StageTextParagraphsRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion ?? MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = PendingChanges.DraftName;
            try
            {
                draftName = PendingChanges.EnsurePendingDraft(new ProposalRepository(database));
                var intent = new SetTextParagraphsIntent(CanonicalId.Parse(request.Target), request.Paragraphs);
                var authored = SetTextParagraphsComposer.Authored(intent);
                return Single(ComposeStepsIntoDraft(database, project, draftName,
                [
                    new ComposeStep(null, SetTextParagraphsComposer.Name,
                        (cache, _) => (authored, SetTextParagraphsComposer.Build(cache, intent)),
                        ReplacePrevious: SameField, Authored: authored),
                ]));
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(request.FwDataPath));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(DraftInvalid(exception.Message, ("draftName", draftName)));
            }
        });

    /// <summary>
    /// Takes the pending-changes Draft's edit of one rich-text field out of it, whatever that edit holds; the Draft
    /// is unchanged when it holds none, and is not created.
    /// </summary>
    public static CommandOutcome<ComposedOperationsResponse> DiscardTextParagraphs(TextParagraphsFieldRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion ?? MotifProductVersion.CurrentText, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            if (repository.FindPendingChangesDraft() is not { DraftName: { } draftName })
                return CommandOutcome<ComposedOperationsResponse>.Success(new ComposedOperationsResponse(PendingChanges.DraftName, SetTextParagraphsComposer.Name, [], 0));
            try
            {
                var target = CanonicalId.Parse(request.Target);
                var authored = System.Text.Json.JsonSerializer.SerializeToElement(new { target = target.Value, field = request.Field });
                return Single(ComposeStepsIntoDraft(database, project, draftName,
                [
                    new ComposeStep(null, SetTextParagraphsComposer.Name,
                        (_, _) => (authored, Array.Empty<SIL.Motif.Contract.Model.OperationEnvelope>()),
                        ReplacePrevious: (_, operation) => IsFieldEdit(operation, target, request.Field), Authored: authored),
                ]));
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(request.FwDataPath));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(DraftInvalid(exception.Message, ("draftName", draftName)));
            }
        });

    /// <summary>The pending-changes Draft's edit of one rich-text field, or null when it holds none.</summary>
    public static CommandOutcome<PendingTextParagraphsResponse> PendingTextParagraphs(TextParagraphsFieldRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion ?? MotifProductVersion.CurrentText, (database, _) =>
        {
            var target = CanonicalId.Parse(request.Target);
            if (!TryPendingOperations(new ProposalRepository(database), out var operations, out var refusal))
                return CommandOutcome<PendingTextParagraphsResponse>.Refused(refusal);
            var operation = operations.LastOrDefault(item => IsFieldEdit(item, target, request.Field));
            return CommandOutcome<PendingTextParagraphsResponse>.Success(new PendingTextParagraphsResponse(
                operation is null ? null : TextParagraphsPayload.Parse(OperationSlotRegistry.After(operation))));
        });

    // False when the pending Draft is unreadable, so it is never taken for one without an edit.
    private static bool TryPendingOperations(ProposalRepository repository,
        out IReadOnlyList<SIL.Motif.Contract.Model.OperationEnvelope> operations, out Refusal refusal)
    {
        operations = [];
        refusal = null!;
        if (repository.FindPendingChangesDraft() is not { DraftName: { } draftName }) return true;
        try
        {
            if (!TryLoadDraft(repository, draftName, out var draft, out string _))
            {
                refusal = DraftNotFound(draftName);
                return false;
            }
            operations = SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(BuildProposalJson(draft)).Operations;
            return true;
        }
        catch (Exception exception) when (!SqliteConnections.IsBusy(exception))
        {
            refusal = DraftInvalidFrom(exception, ("draftName", draftName));
            return false;
        }
    }

    private static bool IsFieldEdit(SIL.Motif.Contract.Model.OperationEnvelope operation, CanonicalId target, string field) =>
        operation.Kind == TextParagraphsOperationKinds.Set && operation.Target == target &&
        operation.After is { } after && TextParagraphsPayload.FieldOf(after) == field;

    private static bool SameField(System.Text.Json.JsonElement authored, SIL.Motif.Contract.Model.OperationEnvelope operation) =>
        ReplacementRule.SameTarget(TextParagraphsOperationKinds.Set).Matches(authored, operation);
}

/// <summary>The pending-changes Draft's edit of one rich-text field, or null when it holds none.</summary>
public sealed record PendingTextParagraphsResponse(TextParagraphsPayload? Paragraphs);
