using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>
    /// Adds a person's edit of one String field, or one writing system of a MultiString field, to the pending-changes
    /// Draft, or updates the edit the Draft already holds for that field and writing system, creating the Draft when
    /// there is none (ADR 0095 decision 4). An edit back to the text it began from takes the earlier edit out.
    /// </summary>
    public static CommandOutcome<ComposedOperationsResponse> StageTextRichString(StageTextRichStringRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion ?? MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = PendingChanges.DraftName;
            try
            {
                draftName = PendingChanges.EnsurePendingDraft(database, new ProposalRepository(database));
                var target = CanonicalId.Parse(request.Target);
                var intent = new SetTextRichStringIntent(target, request.RichString);
                var authored = SetTextRichStringComposer.Authored(intent);
                return Single(ComposeStepsIntoDraft(database, project, draftName,
                [
                    new ComposeStep(null, SetTextRichStringComposer.Name,
                        (cache, _) => (authored, SetTextRichStringComposer.Build(cache, intent)),
                        ReplacePrevious: (_, operation) => IsRichStringEdit(operation, target, request.RichString.Field, request.RichString.Ws),
                        Authored: authored, Independent: true),
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
    /// Takes the pending-changes Draft's edit of one String field, or one alternative of a MultiString field, out of
    /// it; the Draft is unchanged when it holds none, and is not created.
    /// </summary>
    public static CommandOutcome<ComposedOperationsResponse> DiscardTextRichString(TextRichStringFieldRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion ?? MotifProductVersion.CurrentText, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            if (repository.FindPendingChangesDraft() is not { DraftName: { } draftName })
                return CommandOutcome<ComposedOperationsResponse>.Success(
                    new ComposedOperationsResponse(PendingChanges.DraftName, SetTextRichStringComposer.Name, [], 0));
            try
            {
                var target = CanonicalId.Parse(request.Target);
                var authored = JsonSerializer.SerializeToElement(new { target = target.Value, field = request.Field, ws = request.Ws });
                return Single(ComposeStepsIntoDraft(database, project, draftName,
                [
                    new ComposeStep(null, SetTextRichStringComposer.Name,
                        (_, _) => (authored, Array.Empty<OperationEnvelope>()),
                        ReplacePrevious: (_, operation) => IsRichStringEdit(operation, target, request.Field, request.Ws), Authored: authored, Independent: true),
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

    /// <summary>The pending-changes Draft's edit of one String field or MultiString alternative, or null when it holds none.</summary>
    public static CommandOutcome<PendingTextRichStringResponse> PendingTextRichString(TextRichStringFieldRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion ?? MotifProductVersion.CurrentText, (database, _) =>
        {
            var target = CanonicalId.Parse(request.Target);
            if (!TryPendingOperations(new ProposalRepository(database), out var operations, out var refusal))
                return CommandOutcome<PendingTextRichStringResponse>.Refused(refusal);
            var operation = operations.LastOrDefault(item => IsRichStringEdit(item, target, request.Field, request.Ws));
            return CommandOutcome<PendingTextRichStringResponse>.Success(new PendingTextRichStringResponse(
                operation is null ? null : TextRichStringPayload.Parse(OperationSlotRegistry.After(operation))));
        });

    // One edit per owner, field and writing system (tags are case-insensitive); English and Arabic are two edits.
    private static bool IsRichStringEdit(OperationEnvelope operation, CanonicalId target, string field, string? ws) =>
        operation.Kind == TextRichStringOperationKinds.Set && operation.Target is { } existing && CanonicalId.ByGuid.Equals(existing, target) &&
        operation.After is { } after && TextRichStringPayload.AddressOf(after) is var (staged, stagedWs) && staged == field &&
        string.Equals(stagedWs, ws, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The pending-changes Draft's edit of one String field or MultiString alternative, or null when it holds none.</summary>
public sealed record PendingTextRichStringResponse(TextRichStringPayload? RichString);
