using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
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

/// <summary>The scratch pass failed in a way no single change explains, so the import cannot be previewed.</summary>
internal sealed class RecordImportScratchException(Exception inner)
    : Exception("The operations could not be tried on a scratch copy: " + inner.GetType().Name + ".", inner);

public static partial class RecordImportCommand
{
    // Failures a handler gives for a value it cannot take.
    private static bool IsRefusalOfOneChange(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException
            or InvalidCastException or OverflowException or System.Text.Json.JsonException or ContractParseException;

    /// <summary>Tests change the project between the preview and the stage by setting this.</summary>
    internal static Action? BeforeStage { get; set; }

    /// <summary>Identifies the lowering's operations, so a stage can check what was previewed.</summary>
    private static string Digest(IReadOnlyList<OperationEnvelope> operations)
    {
        var text = new StringBuilder();
        foreach (var operation in operations)
            text.Append(operation.OperationId.Value).Append('|').Append(operation.Kind).Append('|')
                .Append(operation.Target?.Value).Append('|').Append(operation.After?.GetRawText())
                .Append('|').Append(string.Join(",", operation.DependsOn.Select(item => item.OperationId.Value))).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Runs the operations on a scratch copy in Apply's order, so refusals surface early.</summary>
    private static RecordLowering ValidateOnScratch(LcmCache cache, ProjectRecordPlan plan, RecordLowering full) =>
        ValidateOnScratch(cache, [(plan, full)])[0];

    /// <summary>One scratch pass over every file's operations, each failure charged to its file.</summary>
    private static RecordLowering[] ValidateOnScratch(LcmCache cache, IReadOnlyList<(ProjectRecordPlan Plan, RecordLowering Full)> units)
    {
        var results = units.Select(unit => unit.Full).ToArray();
        var owner = new Dictionary<CanonicalId, (int Unit, int Change)>();
        var byId = new Dictionary<CanonicalId, OperationEnvelope>();
        for (var unit = 0; unit < units.Count; unit++)
        {
            var full = units[unit].Full;
            if (full.Operations.Count == 0 || full.OperationChanges is not { } owners) continue;
            for (var index = 0; index < full.Operations.Count; index++)
            {
                owner[full.Operations[index].OperationId] = (unit, owners[index]);
                byId[full.Operations[index].OperationId] = full.Operations[index];
            }
        }
        if (byId.Count == 0) return results;
        var failed = new Dictionary<(int Unit, int Change), string>();
        var failedOperations = new HashSet<CanonicalId>();
        var contained = byId.Values.Select(operation => new OperationEnvelope(operation.OperationId, operation.Kind,
            operation.EntityId, operation.Target, operation.After, operation.Placement,
            operation.DependsOn.Where(dependency => owner.TryGetValue(dependency.OperationId, out var own) &&
                own.Unit == owner[operation.OperationId].Unit).ToArray(),
            rationale: operation.Rationale)).ToArray();
        var handler = cache.ServiceLocator.GetInstance<IActionHandler>();
        using var unitOfWork = new NonUndoableUnitOfWorkHelper(handler);
        unitOfWork.RollBack = false;
        using var writes = DeferredWrites.Begin(cache);
        IReadOnlyList<OperationEnvelope> order;
        try
        {
            order = OperationExecutionOrder.Sort(contained);
        }
        catch (Exception exception) when (IsRefusalOfOneChange(exception))
        {
            throw new RecordImportScratchException(exception);
        }
        foreach (var ordered in order)
        {
            var id = ordered.OperationId;
            var change = owner[id];
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
            catch (Exception exception) when (IsRefusalOfOneChange(exception))
            {
                failedOperations.Add(id);
                failed[change] = exception is ProposalRefusalException refusal
                    ? refusal.Code + ": " + refusal.Message
                    : exception.Message;
            }
            catch (Exception exception) when (exception is not (ProjectWritingSystemsUnavailableException or OutOfMemoryException))
            {
                throw new RecordImportScratchException(exception);
            }
        }
        try
        {
            writes.Complete();
        }
        catch (Exception exception) when (IsRefusalOfOneChange(exception))
        {
            foreach (var change in owner.Values.Distinct().Where(change => !failed.ContainsKey(change)))
                failed[change] = "The operations together could not be written: " + exception.Message;
        }
        for (var unit = 0; unit < units.Count; unit++)
        {
            var mine = failed.Where(pair => pair.Key.Unit == unit).ToDictionary(pair => pair.Key.Change, pair => pair.Value);
            if (mine.Count > 0) results[unit] = WithoutFailed(units[unit].Plan, units[unit].Full, mine);
        }
        return results;
    }

    private static RecordLowering WithoutFailed(ProjectRecordPlan plan, RecordLowering full, Dictionary<int, string> failed)
    {
        var changes = plan.ChangeSet.Changes;
        var owners = full.OperationChanges!;
        if (full.RequiredChanges is { } required && failed.Keys.Where(required.Contains).Order().FirstOrDefault(-1) is var fatal and >= 0)
            return new([], full.Refusals, full.PlanRefusals, [], null, new RecordLoweringRefusal(RefusalCodes.RecordImportCreateShape,
                changes[fatal].ObjectType ?? "", changes[fatal].Field, plan.RecordGuid,
                "A new record needs the parts FieldWorks makes with it, and the Runner refused one: " + failed[fatal]));
        var refusedCodes = (full.RefusedChanges ?? new Dictionary<int, string>()).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var index in failed.Keys) refusedCodes[index] = RefusalCodes.RecordImportChangeRefused;
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
        return new(kept, refusals, full.PlanRefusals, keptChanges, refusedCodes);
    }
}
