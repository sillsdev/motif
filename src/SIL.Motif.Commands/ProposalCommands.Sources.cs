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
                draftName = PendingChanges.EnsurePendingDraft(new ProposalRepository(database));
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
}
