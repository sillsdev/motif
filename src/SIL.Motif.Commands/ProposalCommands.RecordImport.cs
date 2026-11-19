using System.Text.Json;
using SIL.Motif.Commands.Records;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands;

/// <summary>A record-file import staged in a Draft: the operations added and every change that was refused.</summary>
/// <param name="Composed">What the Draft now holds from this import.</param>
/// <param name="Lowering">The lowering, whose refusals name each change that has no operation.</param>
public sealed record RecordImportStaged(ComposedOperationsResponse Composed, RecordLowering Lowering);

public static partial class ProposalCommands
{
    /// <summary>
    /// Adds the operations a record-file plan lowers to into an existing Draft, through the same compose path every
    /// other verb uses, so the operations are validated against a scratch copy and nothing is applied. A change the
    /// lowering refused is reported, not staged.
    /// </summary>
    public static CommandOutcome<RecordImportStaged> StageRecordImport(string projectPath, string draftName,
        ProjectRecordSchemaResult schema, ProjectRecordPlan plan) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            RecordLowering? lowering = null;
            try
            {
                var lowerer = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema));
                var authored = JsonSerializer.SerializeToElement(new { recordFile = plan.Export?.Base ?? "" });
                var steps = new List<ComposeStep>
                {
                    new(null, "RecordImport", (cache, _) =>
                    {
                        lowering = lowerer.Lower(cache, plan);
                        return (authored, lowering.Operations);
                    }, Authored: authored),
                    // The empty step makes the batch validate the last operation against the scratch project.
                    new(null, "Validate", (_, _) => (JsonSerializer.SerializeToElement(new { }), Array.Empty<OperationEnvelope>())),
                };
                var outcome = ComposeStepsIntoDraft(database, project, draftName, steps);
                return outcome.Succeeded
                    ? CommandOutcome<RecordImportStaged>.Success(new(outcome.Value![0], lowering!))
                    : CommandOutcome<RecordImportStaged>.Refused(outcome.Refusal!);
            }
            catch (Exception exception) when (SqliteConnections.IsBusy(exception))
            {
                return CommandOutcome<RecordImportStaged>.Refused(ProjectStoreCommand.BusyBeforeSave(projectPath));
            }
        });
}
