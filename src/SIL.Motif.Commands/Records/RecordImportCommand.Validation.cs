using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

/// <summary>The project or file changed after a preview, so the operations to stage are not the ones that were previewed.</summary>
internal sealed class RecordImportChangedException() : ProposalRefusalException(RefusalCodes.RecordImportChangedSincePreview,
    FailureReason.Refused,
    "The project changed after the import was previewed, so the Draft was not staged. Run the import again.",
    new Dictionary<string, string>());

public static partial class RecordImportCommand
{
    /// <summary>Tests change the project between the preview and the stage by setting this.</summary>
    internal static Action? BeforeStage { get; set; }

    /// <summary>Identifies the operations a lowering produced, so a stage can check it would stage what was previewed.</summary>
    private static string Digest(IReadOnlyList<OperationEnvelope> operations)
    {
        var text = new StringBuilder();
        foreach (var operation in operations)
            text.Append(operation.OperationId.Value).Append('|').Append(operation.Kind).Append('|')
                .Append(operation.Target?.Value).Append('|').Append(operation.After?.GetRawText())
                .Append('|').Append(string.Join(",", operation.DependsOn.Select(item => item.OperationId.Value))).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// Tries the operations on the scratch copy in <paramref name="cache"/>, in the order Apply would run them, as the
    /// stage does. A change whose operation the Runner refuses is refused here with its record, field and reason, together
    /// with the changes that wait on it, so the stage never discovers it.
    /// </summary>
    private static RecordLowering ValidateOnScratch(LcmCache cache, ProjectRecordPlan plan, RecordLowering full)
    {
        if (full.Operations.Count == 0 || full.OperationChanges is not { } owners) return full;
        var changes = plan.ChangeSet.Changes;
        var changeOf = new Dictionary<CanonicalId, int>();
        for (var index = 0; index < full.Operations.Count; index++)
            changeOf[full.Operations[index].OperationId] = owners[index];
        var failed = new Dictionary<int, string>();
        var failedOperations = new HashSet<CanonicalId>();
        var own = changeOf.Keys.ToHashSet();
        var contained = full.Operations.Select(operation => new OperationEnvelope(operation.OperationId, operation.Kind,
            operation.EntityId, operation.Target, operation.After, operation.Placement,
            operation.DependsOn.Where(dependency => own.Contains(dependency.OperationId)).ToArray(),
            rationale: operation.Rationale)).ToArray();
        var byId = full.Operations.ToDictionary(operation => operation.OperationId);
        var handler = cache.ServiceLocator.GetInstance<IActionHandler>();
        using var unit = new NonUndoableUnitOfWorkHelper(handler);
        unit.RollBack = false;
        using var writes = DeferredWrites.Begin(cache);
        foreach (var ordered in OperationExecutionOrder.Sort(contained))
        {
            var id = ordered.OperationId;
            var change = changeOf[id];
            if (failed.ContainsKey(change) || ordered.DependsOn.Any(dependency => failedOperations.Contains(dependency.OperationId)))
            {
                failedOperations.Add(id);
                failed.TryAdd(change, "It depends on a change the Runner refused.");
                continue;
            }
            try
            {
                OperationHandlerRegistry.Resolve(byId[id].Kind, "Draft composer preparation")
                    .ApplyAndCaptureEffect(cache, byId[id], new List<CanonicalId>());
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                failedOperations.Add(id);
                failed[change] = exception is ProposalRefusalException refusal
                    ? refusal.Code + ": " + refusal.Message
                    : exception.Message;
            }
        }
        try
        {
            writes.Complete();
        }
        catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
        {
            foreach (var change in changeOf.Values.Distinct().Where(change => !failed.ContainsKey(change)))
                failed[change] = "The operations together could not be written: " + exception.Message;
        }
        if (failed.Count == 0) return full;
        var kept = new List<OperationEnvelope>();
        var keptChanges = new List<int>();
        for (var index = 0; index < full.Operations.Count; index++)
        {
            if (failed.ContainsKey(owners[index])) continue;
            kept.Add(full.Operations[index]);
            keptChanges.Add(owners[index]);
        }
        var refusals = full.Refusals.ToList();
        foreach (var (index, reason) in failed.OrderBy(pair => pair.Key))
        {
            var change = changes[index];
            refusals.Add(new(RefusalCodes.RecordImportChangeRefused, change.ObjectType ?? "", change.Field,
                change.ObjectGuid, reason));
        }
        return new(kept, refusals, full.PlanRefusals, keptChanges);
    }
}
