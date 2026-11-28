using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
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
    /// <summary>An edit under an operation's own id is no longer the import's work, so the import stops listing it.</summary>
    internal static void DropRecordImportMark(DraftDocument draft, string operationId)
    {
        for (var index = 0; index < draft.ComposerProvenance.Count; index++)
        {
            if (RecordImportEntryIds(draft.ComposerProvenance[index]) is not { } ids || !ids.Contains(operationId)) continue;
            var entry = JsonNode.Parse(draft.ComposerProvenance[index].GetRawText())!.AsObject();
            entry["operationIds"] = new JsonArray(ids.Where(id => id != operationId).Select(id => (JsonNode)id).ToArray());
            draft.ComposerProvenance[index] = JsonSerializer.SerializeToElement(entry);
        }
    }

    /// <summary>The import entries of a Proposal narrowed to the operations a split group keeps; an entry left with none is dropped.</summary>
    internal static List<JsonElement> CarryRecordImportMarks(JsonElement? extensions, IReadOnlySet<string> kept)
    {
        var carried = new List<JsonElement>();
        foreach (var element in ExtractComposerProvenance(extensions))
        {
            if (RecordImportEntryIds(element) is not { } ids) continue;
            var narrowed = ids.Where(kept.Contains).ToArray();
            if (narrowed.Length == 0) continue;
            var entry = JsonNode.Parse(element.GetRawText())!.AsObject();
            entry["operationIds"] = new JsonArray(narrowed.Select(id => (JsonNode)id).ToArray());
            carried.Add(JsonSerializer.SerializeToElement(entry));
        }
        return carried;
    }

    private static string[]? RecordImportEntryIds(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object &&
        entry.TryGetProperty("composer", out var name) && name.ValueKind == JsonValueKind.String &&
        name.GetString() == RecordImportProvenance.ComposerName &&
        entry.TryGetProperty("operationIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String).Select(id => id.GetString()!).ToArray()
            : null;

    /// <summary>
    /// Adds the operations a record-file plan lowers to into an existing Draft, through the same compose path every
    /// other verb uses, so the operations are validated against a scratch copy and nothing is applied. A change the
    /// lowering refused is reported, not staged.
    /// </summary>
    public static CommandOutcome<RecordImportStaged> StageRecordImport(string projectPath, string draftName,
        ProjectRecordSchemaResult schema, ProjectRecordPlan plan) =>
        StageRecordImport(projectPath, draftName, plan.Export?.Base ?? "",
            cache => new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan),
            plan.Kind == RecordImportProvenance.LexiconKind);

    /// <summary>
    /// Stages what <paramref name="lower"/> returns for the scratch project the compose reads. The caller's delegate is where a
    /// preview is compared with the project as the stage sees it, so it can refuse by throwing <see cref="RecordImportChangedException"/>.
    /// </summary>
    internal static CommandOutcome<RecordImportStaged> StageRecordImport(string projectPath, string draftName,
        string recordFile, Func<LcmCache, RecordLowering> lower, bool lexicon) =>
        StageRecordImport(projectPath, draftName, JsonSerializer.SerializeToElement(new { recordFile }), (cache, _) => lower(cache),
            // The count travels with the Draft so a partial stage never reads as a plain success.
            lowering => JsonSerializer.SerializeToElement(new
            {
                recordFile, refusedChanges = lowering.Refusals.Count + lowering.PlanRefusals.Count, lexicon,
            }));

    /// <summary>Stages with the Draft's recorded input and authored form chosen by the caller, as a folder import records its manifest.</summary>
    internal static CommandOutcome<RecordImportStaged> StageRecordImport(string projectPath, string draftName,
        JsonElement authored, Func<LcmCache, bool, RecordLowering> lower, Func<RecordLowering, JsonElement> input) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            RecordLowering? lowering = null;
            try
            {
                var untouched = TryLoadDraft(new SIL.Motif.Worker.Store.ProposalRepository(database), draftName, out var existing, out _) && existing.Operations.Count == 0;
                var steps = new List<ComposeStep>
                {
                    new(null, RecordImportProvenance.ComposerName, (cache, _) =>
                    {
                        lowering = lower(cache, untouched);
                        return (input(lowering), lowering.Operations);
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
