using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>
    /// Stages a new Source in the pending changes. A <c>Folder</c> is recorded in the local file at once, because it
    /// never reaches the project; the folder must exist.
    /// </summary>
    public static CommandOutcome<SourceStageResponse> StageSourceAdd(SourceAddRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Folder is not null && !Directory.Exists(request.Folder))
            return CommandOutcome<SourceStageResponse>.Refused(new Refusal(RefusalCodes.SourceFolderMissing,
                FailureReason.NotFound, $"The folder '{request.Folder}' does not exist on this machine.",
                new Dictionary<string, string> { ["path"] = request.Folder }));
        var id = request.Id?.Trim() ?? "";
        var folder = request.Folder is null ? null : Path.GetFullPath(request.Folder);
        return StageSource(request.ProjectPath, new AddSourceDefinition(), id, "source-add", $"Add Source {id}",
            new { id, name = request.Name, versification = request.Versification, context = request.Context, kind = request.Kind },
            folder);
    }

    /// <summary>Stages a new display name for a Source.</summary>
    public static CommandOutcome<SourceStageResponse> StageSourceSetName(SourceSetNameRequest request) =>
        StageSource(request.ProjectPath, new SetSourceNameDefinition(), request.Source, "source-set-name",
            $"Rename Source {request.Source}", new { source = request.Source, name = request.Name });

    /// <summary>Stages a new Context for a Source.</summary>
    public static CommandOutcome<SourceStageResponse> StageSourceSetContext(SourceSetContextRequest request) =>
        StageSource(request.ProjectPath, new SetSourceContextDefinition(), request.Source, "source-set-context",
            $"Change the Context of Source {request.Source}", new { source = request.Source, context = request.Context });

    /// <summary>Stages a new versification for a Source.</summary>
    public static CommandOutcome<SourceStageResponse> StageSourceSetVersification(SourceSetVersificationRequest request) =>
        StageSource(request.ProjectPath, new SetSourceVersificationDefinition(), request.Source, "source-set-versification",
            $"Change the versification of Source {request.Source}",
            new { source = request.Source, versification = request.Versification });

    /// <summary>Stages the removal of a Source (status Removed).</summary>
    public static CommandOutcome<SourceStageResponse> StageSourceRemove(SourceRemoveRequest request) =>
        StageSource(request.ProjectPath, new SetSourceStatusDefinition(), request.Source, "source-remove",
            $"Remove Source {request.Source}", new { source = request.Source, status = "Removed" });

    /// <summary>Stages the restoring of a Removed Source (status Active).</summary>
    public static CommandOutcome<SourceStageResponse> StageSourceRestore(SourceRestoreRequest request) =>
        StageSource(request.ProjectPath, new SetSourceStatusDefinition(), request.Source, "source-restore",
            $"Restore Source {request.Source}", new { source = request.Source, status = "Active" });

    private static CommandOutcome<SourceStageResponse> StageSource(
        string projectPath, IComposerDefinition definition, string source, string kind, string summary,
        object authoredInput, string? folder = null) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = PendingChanges.DraftName;
            try
            {
                draftName = PendingChanges.EnsurePendingDraft(database, new ProposalRepository(database));
                var authored = JsonSerializer.SerializeToElement(authoredInput);
                var step = ComposeStep.From(definition, authored) with
                {
                    PendingRow = new PendingRowSpec(SourceChangeFit.Domain, kind, source.Trim(), summary),
                };
                var composed = Single(ComposeStepsIntoDraft(database, project, draftName, [step]));
                if (!composed.Succeeded) return CommandOutcome<SourceStageResponse>.Refused(composed.Refusal!);
                if (folder is not null)
                    ScriptureLocalStore.ForInstallation().SetSourcePath(ProjectWorkspaceKey.Compute(project), source.Trim(), folder);
                return CommandOutcome<SourceStageResponse>.Success(
                    new SourceStageResponse(source.Trim(), draftName, composed.Value!.Operations, folder));
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<SourceStageResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(projectPath));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<SourceStageResponse>.Refused(DraftInvalid(exception.Message, ("draftName", draftName)));
            }
        });

    /// <summary>Stages clearing the approval at one Occurrence, which returns it to a guess.</summary>
    public static CommandOutcome<SourceApprovalStageResponse> StageSourceClearApproval(SourceClearApprovalRequest request) =>
        StageApprovals(request.ProjectPath, request.Source, request.Anchor, false,
            (scope, entry) => [(request.Anchor, new { source = request.Source, anchor = request.Anchor, action = SetOccurrenceApprovalDefinition.ClearAction })],
            $"Clear the approval at {request.Anchor} in Source {request.Source}");

    /// <summary>
    /// Stages the approval of an Occurrence, and with <c>Bulk</c> of every other Occurrence of the same wordform in the
    /// Source that is not already approved or stale. Everything is staged in the pending Draft; nothing is applied.
    /// </summary>
    public static CommandOutcome<SourceApprovalStageResponse> StageSourceApprove(SourceApproveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonElement? reading = null;
        if (request.Reading is not null)
        {
            try { reading = JsonDocument.Parse(request.Reading).RootElement.Clone(); }
            catch (JsonException exception)
            {
                return CommandOutcome<SourceApprovalStageResponse>.Refused(new Refusal(RefusalCodes.SourceAnalysisNeeded,
                    FailureReason.InvalidArgument, "The reading is not valid JSON: " + exception.Message));
            }
        }
        object Intent(string anchor, bool bulk) => new
        {
            source = request.Source, anchor, action = SetOccurrenceApprovalDefinition.ApproveAction,
            analysis = request.Analysis, wordform = request.Wordform, reading, bulk,
        };
        return StageApprovals(request.ProjectPath, request.Source, request.Anchor, true, (scope, entry) =>
        {
            var anchors = new List<(string, object)> { (request.Anchor, Intent(request.Anchor, false)) };
            if (!request.Bulk) return anchors;
            var locator = SIL.Motif.Runner.Operations.ScriptureApprovalDocument.IsAnchor(request.Anchor)
                ? SIL.Motif.Runner.Operations.ScriptureApprovalDocument.PassageOf(request.Anchor) : "";
            if (scope.Passage(entry, locator) is not { } passage) return anchors;
            // Words are numbered with the project's tokeniser, as the Source index numbers them.
            var others = ProjectReadCache.ReadSaved(scope.Project, (_, cache) =>
            {
                var tokeniser = SourceApprovals.Tokeniser(cache);
                var target = ParatextPassages.Tokenize(passage, tokeniser).FirstOrDefault(word => word.Anchor == request.Anchor);
                if (target is null) return new List<string>();
                var wanted = SetOccurrenceApprovalDefinition.Normal(target.Surface);
                return scope.AllPassages(entry).SelectMany(each => ParatextPassages.Tokenize(each, tokeniser))
                    .Where(word => word.Anchor != request.Anchor && SetOccurrenceApprovalDefinition.Normal(word.Surface) == wanted)
                    .Select(word => word.Anchor).ToList();
            });
            anchors.AddRange(others.Select(other => (other, Intent(other, true))));
            return anchors;
        }, $"Approve {request.Anchor} in Source {request.Source}" + (request.Bulk ? " and the same wordform elsewhere" : ""));
    }

    private static CommandOutcome<SourceApprovalStageResponse> StageApprovals(
        string projectPath, string source, string anchor, bool approving,
        Func<ComposeProjectScope, string, IReadOnlyList<(string Anchor, object Intent)>> plan, string summary) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var draftName = PendingChanges.DraftName;
            try
            {
                draftName = PendingChanges.EnsurePendingDraft(database, new ProposalRepository(database));
                using var scopeHandle = ComposeProjectScope.Ensure(database, project);
                var scope = ComposeProjectScope.Current!;
                var id = source.Trim();
                var steps = plan(scope, id).Select(item => ComposeStep.From(new SetOccurrenceApprovalDefinition(),
                    JsonSerializer.SerializeToElement(item.Intent)) with
                {
                    PendingRow = new PendingRowSpec(SourceApprovalFit.Domain, approving ? "source-approve" : "source-clear",
                        id, item.Anchor == anchor ? summary : $"Approve {item.Anchor} in Source {id}"),
                }).ToArray();
                var composed = ComposeStepsIntoDraft(database, project, draftName, steps);
                if (!composed.Succeeded) return CommandOutcome<SourceApprovalStageResponse>.Refused(composed.Refusal!);
                var operations = composed.Value!.SelectMany(step => step.Operations).ToArray();
                var chapters = scope.Approved.Concat(scope.Cleared)
                    .Select(SIL.Motif.Runner.Operations.ScriptureApprovalDocument.PassageOf).Distinct().Count();
                int Skipped(OccurrenceSkip reason) => scope.Skipped.Count(pair => pair.Value == reason);
                return CommandOutcome<SourceApprovalStageResponse>.Success(new SourceApprovalStageResponse(id, draftName,
                    scope.Approved.Count, scope.Cleared.Count, chapters, Skipped(OccurrenceSkip.AlreadyApproved),
                    Skipped(OccurrenceSkip.ApprovedOtherwise), Skipped(OccurrenceSkip.Stale), scope.Conflicts.ToArray(), operations)
                {
                    AddsApprovalsField = scope.AddsApprovalsField,
                });
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<SourceApprovalStageResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(projectPath));
            }
            catch (SIL.Motif.Runner.Execution.ProposalRefusalException refusal)
            {
                return CommandOutcome<SourceApprovalStageResponse>.Refused(ProposalRefusals.ToRefusal(refusal));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<SourceApprovalStageResponse>.Refused(DraftInvalid(exception.Message, ("draftName", draftName)));
            }
        });
}
