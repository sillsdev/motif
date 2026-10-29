using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that records decision evidence for a wordform or an analysis and changes nothing in
/// the project.
/// </summary>
/// <remarks>
/// It exists so a word-form tier such as a typo or an ill-formed word can be recorded without touching spelling
/// status, which stays a person's. The closed <c>after</c> payload is empty and the operation must carry a
/// <c>decisionEvidence</c> provenance entry; the evidence lives in Motif's store with the Applied Proposal.
/// </remarks>
public static class DecisionEvidenceOperationKinds
{
    public const string Record = "analysis/decisionEvidence/record";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Record);
        OperationHandlerRegistry.Register(Record, new RecordDecisionEvidenceHandler());
        OperationSlotRegistry.Register(Record, operation => new OperationSlot(
            OperationSlotRegistry.Target(operation), SnapshotFields.DecisionEvidenceRecorded));
    }
#pragma warning restore CA2255
}

/// <summary>The closed, empty payload of <see cref="DecisionEvidenceOperationKinds.Record"/>.</summary>
public static class DecisionEvidenceRecordPayload
{
    public static void Parse(JsonElement after)
    {
        const string kind = DecisionEvidenceOperationKinds.Record;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, [], kind);
    }
}

internal sealed class RecordDecisionEvidenceHandler : IOperationHandler
{
    private const string Kind = DecisionEvidenceOperationKinds.Record;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var effect = Read(cache, operation);
        touchedTargets.Add(operation.Target!.Value);
        return effect;
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) => Read(cache, operation);

    private static ExpectedEffect Read(LcmCache cache, OperationEnvelope operation)
    {
        DecisionEvidenceRecordPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{Kind}' operation requires 'after'."));
        if (DecisionEvidence.FromProvenance(operation.Provenance) is null)
            throw new InvalidOperationException($"'{Kind}' needs a decisionEvidence provenance entry; without one it records nothing.");
        var id = operation.Target ?? throw new InvalidOperationException($"'{Kind}' requires a target.");
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.TryGetObject(id.ToGuid(), out var target) || target is not (IWfiWordform or IWfiAnalysis))
            throw new InvalidOperationException($"'{Kind}': target '{id.Value}' must be an existing wordform or analysis.");
        var state = new Dictionary<string, string> { ["target"] = target.ClassName };
        return new ExpectedEffect(id, SnapshotFields.DecisionEvidenceRecorded, state, state);
    }
}
