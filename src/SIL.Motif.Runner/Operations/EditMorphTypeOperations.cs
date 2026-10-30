using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names a guarded in-place morph-type edit.</summary>
public static class EditMorphTypeOperationKinds
{
    public const string Set = "grammar/moForm/editMorphType";
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, EditMorphTypeHandler.Instance);
        OperationSlotRegistry.RegisterScalar(Set, SnapshotFields.MoFormMorphType);
    }
#pragma warning restore CA2255
}

/// <summary>Checks the FieldWorks stem-family classification before an in-place edit.</summary>
public static class MorphTypeEditSafety
{
    public static void Check(IMoForm form, IMoMorphType desired)
    {
        if (form is not IMoStemAllomorph || form.MorphTypeRA is not { } current ||
            !IsStemFamily(current) || !IsStemFamily(desired))
            throw new MorphTypeFamilyException();
    }

    private static bool IsStemFamily(IMoMorphType type)
    {
        try { return MoFormConcreteClassSelection.ClassFor(type, EditMorphTypeOperationKinds.Set) == MoFormConcreteClass.StemAllomorph; }
        catch (InvalidOperationException) { return false; }
    }
}

internal sealed class MorphTypeFamilyException : ProposalRefusalException
{
    internal MorphTypeFamilyException() : base(RefusalCodes.MorphTypeIncompatibleFamily, FailureReason.Refused,
        "An in-place morph-type edit requires a stem form and two stem-family types; changing families requires a different analysis.",
        new Dictionary<string, string>()) { }
}

internal sealed class EditMorphTypeHandler : IOperationHandler
{
    internal static readonly EditMorphTypeHandler Instance = new();

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, form, type) = Resolve(cache, operation);
        var before = ReferenceFieldSnapshotting.ReadAlternatives(form.MorphTypeRA);
        var beforeName = form.MorphTypeRA.Name.BestAnalysisAlternative?.Text;
        var afterName = type.Name.BestAnalysisAlternative?.Text;
        touchedTargets.Add(id);
        form.MorphTypeRA = type;
        var preview = JsonSerializer.SerializeToElement(new { beforeTypeName = beforeName, afterTypeName = afterName });
        return new(id, SnapshotFields.MoFormMorphType, before,
            ReferenceFieldSnapshotting.ReadAlternatives(form.MorphTypeRA), preview);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, form) = TargetResolution.Resolve<IMoForm>(cache, operation, EditMorphTypeOperationKinds.Set);
        var current = ReferenceFieldSnapshotting.ReadAlternatives(form.MorphTypeRA);
        return new(id, SnapshotFields.MoFormMorphType, current, current);
    }

    private static (CanonicalId, IMoForm, IMoMorphType) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var after = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(after, EditMorphTypeOperationKinds.Set);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["morphType", "expectedMorphType"], EditMorphTypeOperationKinds.Set);
        var typeId = ClosedPayloadParsing.GetRequiredCanonicalId(after, "morphType", EditMorphTypeOperationKinds.Set);
        var expected = ClosedPayloadParsing.GetRequiredCanonicalId(after, "expectedMorphType", EditMorphTypeOperationKinds.Set);
        var (id, form) = TargetResolution.Resolve<IMoForm>(cache, operation, EditMorphTypeOperationKinds.Set);
        var type = ReferenceFieldLowering.Resolve<IMoMorphType>(cache, typeId, EditMorphTypeOperationKinds.Set);
        MorphTypeEditSafety.Check(form, type);
        if (form.MorphTypeRA.Guid != expected.ToGuid())
            throw new MorphTypeStaleSourceException(id, expected, CanonicalId.FromGuid(form.MorphTypeRA.Guid));
        return (id, form, type);
    }
}

internal sealed class MorphTypeStaleSourceException : ProposalRefusalException
{
    internal MorphTypeStaleSourceException(CanonicalId form, CanonicalId expected, CanonicalId actual)
        : base(RefusalCodes.MorphTypeStaleSource, FailureReason.Refused,
            "The form's morph type changed; compose the edit again.",
            new Dictionary<string, string> { ["form"] = form.Value, ["expectedMorphType"] = expected.Value,
                ["actualMorphType"] = actual.Value }) { }
}
