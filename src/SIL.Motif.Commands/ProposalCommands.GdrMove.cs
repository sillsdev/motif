using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Projection;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>Stages the move of one Guide section, with its subtree, in one Draft save, refusing a section the Draft already moves.</summary>
    public static CommandOutcome<GdrMoveResponse> StageGdrMove(GdrMoveRequest request, string? namedDraft)
    {
        string abbreviation = "", id = "";
        string? parentId = null, previousParentId = null;
        var outcome = ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = namedDraft ?? PendingChanges.DraftName;
            try
            {
                var repository = new ProposalRepository(database);
                if (repository.DraftNameExists(draftName))
                {
                    var held = OperationsOf(database, draftName);
                    if (!held.Succeeded) return CommandOutcome<ComposedOperationsResponse[]>.Refused(held.Refusal!);
                    var target = ReadGuideId(project, request.ProjectPath, request.Id);
                    if (target is not null && held.Value!.Any(operation => operation.Kind == GdrSectionOperationKinds.Move &&
                            operation.Target is { } moved && moved.ToGuid() == CanonicalId.Parse(target).ToGuid()))
                        return CommandOutcome<ComposedOperationsResponse[]>.Refused(new Refusal(RefusalCodes.GdrMoveStale, FailureReason.Refused,
                            $"{draftName} already moves {request.Id}, so a second move would start from a place the Baseline does not show. " +
                            $"Preview the staged move with 'guide show {request.Id} --as-of {draftName}', or remove its operation from the Draft and move again.",
                            new Dictionary<string, string> { ["draftName"] = draftName }));
                }
                var steps = new List<ComposeStep>
                {
                    new(null, "MoveGdrSection", (cache, _) =>
                    {
                        var list = GdrSectionSupport.FindList(cache, GdrSectionOperationKinds.Move) ??
                            throw new GdrSectionRefusal(RefusalCodes.GdrNotFound, "The project has no Grammar Guide list.", FailureReason.NotFound);
                        ICmPossibility Resolve(string text, string role) =>
                            GdrSectionSupport.Find(cache, list, text.Trim()) ?? throw new GdrSectionRefusal(RefusalCodes.GdrNotFound,
                                $"No Grammar Guide section is named '{text}', so it cannot be the {role}.", FailureReason.NotFound);
                        var section = Resolve(request.Id, "section to move");
                        var parent = request.Parent is null ? null : Resolve(request.Parent, "new parent");
                        if (request.After is not null && request.Before is not null)
                            throw new GdrSectionRefusal(RefusalCodes.GdrMovePosition, "Give --after or --before, not both.");
                        for (ICmObject walk = parent!; walk is ICmPossibility step; walk = step.Owner)
                            if (step.Guid == section.Guid)
                                throw new GdrSectionRefusal(RefusalCodes.GdrMoveCycle,
                                    $"{request.Id} cannot move under {request.Parent}: that is the section itself or one of its own descendants.");
                        var sibling = request.After is { } after ? Resolve(after, "section to follow")
                            : request.Before is { } before ? Resolve(before, "section to precede") : null;
                        if (sibling is not null)
                        {
                            var siblings = parent is null ? list.PossibilitiesOS.Cast<ICmPossibility>() : parent.SubPossibilitiesOS;
                            if (sibling.Guid == section.Guid || !siblings.Contains(sibling))
                                throw new GdrSectionRefusal(RefusalCodes.GdrMovePosition,
                                    $"'{(request.After ?? request.Before)}' is not under the new parent, so the section cannot be placed beside it there.");
                        }
                        abbreviation = GdrSectionSupport.NumberOf(cache, section);
                        id = CanonicalId.FromGuid(section.Guid).Value;
                        previousParentId = section.Owner is ICmPossibility owner ? CanonicalId.FromGuid(owner.Guid).Value : null;
                        parentId = parent is null ? null : CanonicalId.FromGuid(parent.Guid).Value;
                        var payload = new Dictionary<string, string> { ["from"] = previousParentId ?? "" };
                        if (parentId is not null) payload["parent"] = parentId;
                        if (sibling is not null) payload[request.After is not null ? "after" : "before"] = CanonicalId.FromGuid(sibling.Guid).Value;
                        var input = JsonSerializer.SerializeToElement(new { target = id, parent = parentId });
                        return (input, new[]
                        {
                            new OperationEnvelope(CanonicalId.Mint(), GdrSectionOperationKinds.Move, target: CanonicalId.Parse(id),
                                after: JsonSerializer.SerializeToElement(payload)),
                        });
                    }),
                    new ComposeStep(null, "Validate", (_, _) =>
                        (JsonSerializer.SerializeToElement(new { }), Array.Empty<OperationEnvelope>())),
                };
                if (namedDraft is null) draftName = PendingChanges.EnsurePendingDraft(database, repository);
                return ComposeStepsIntoDraft(database, project, draftName, steps);
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<ComposedOperationsResponse[]>.Refused(ProjectStoreCommand.BusyBeforeSave(request.ProjectPath));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<ComposedOperationsResponse[]>.Refused(DraftInvalid(exception.Message, ("draftName", draftName)));
            }
        });
        if (!outcome.Succeeded) return CommandOutcome<GdrMoveResponse>.Refused(outcome.Refusal!);
        var all = outcome.Value!;
        return CommandOutcome<GdrMoveResponse>.Success(new GdrMoveResponse(all.LastOrDefault()?.DraftName ?? namedDraft ?? PendingChanges.DraftName,
            id, abbreviation, parentId, previousParentId, all.Sum(item => item.Operations.Count)));
    }

    // The canonical id of the Baseline section named by number or id, or null when the Baseline does not hold it.
    private static string? ReadGuideId(ProjectLocator project, string projectPath, string id) =>
        ProjectReadCache.ReadSaved(project, (_, cache) =>
            GdrSectionSupport.FindList(cache, GdrSectionOperationKinds.Move) is { } list &&
            GdrSectionSupport.Find(cache, list, id.Trim()) is { } found ? CanonicalId.FromGuid(found.Guid).Value : null);
}
