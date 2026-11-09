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

    private static bool SameField(System.Text.Json.JsonElement authored, SIL.Motif.Contract.Model.OperationEnvelope operation) =>
        ReplacementRule.SameTarget(TextParagraphsOperationKinds.Set).Matches(authored, operation);
}
