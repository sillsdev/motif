using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

public static class HumanJudgmentCustomFieldOperationKinds
{
    public const string Set = "system/rnGenericRec/setMotifHumanJudgment";

    /// <summary>
    /// Throws when the reserved judgment field is missing, ambiguous or incompatible. Read-only: it never writes
    /// or saves, so Preflight can run the same check Apply runs before it touches the project.
    /// </summary>
    public static void RequireCompatibleField(LcmCache cache) => HumanJudgmentCustomFieldHandler.RequireCompatible(cache);

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, HumanJudgmentCustomFieldHandler.Instance);
    }
#pragma warning restore CA2255
}

internal sealed class HumanJudgmentCustomFieldHandler : IOperationHandler
{
    public static readonly HumanJudgmentCustomFieldHandler Instance = new();
    private HumanJudgmentCustomFieldHandler() { }

    internal static void RequireCompatible(LcmCache cache) => _ = ResolveField(cache);

    /// <summary>
    /// Returns null when no field uses the reserved name, so a snapshot emits nothing for it. Otherwise
    /// validates the field exactly as Apply does, throwing for an ambiguous or incompatible schema.
    /// </summary>
    internal static int? TryResolveField(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var named = metadata.GetFieldIds()
            .Any(flid => string.Equals(metadata.GetFieldName(flid), "MotifHumanJudgment", StringComparison.Ordinal));
        return named ? ResolveField(cache) : null;
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var after = operation.After ?? throw new InvalidOperationException($"'{HumanJudgmentCustomFieldOperationKinds.Set}' requires after.");
        ClosedPayloadParsing.RequireObject(after, HumanJudgmentCustomFieldOperationKinds.Set);
        ClosedPayloadParsing.RejectUnknownProperties(after, new[] { "text" }, HumanJudgmentCustomFieldOperationKinds.Set);
        var value = ClosedPayloadParsing.GetRequiredString(after, "text", HumanJudgmentCustomFieldOperationKinds.Set);
        var (id, record) = TargetResolution.Resolve<IRnGenericRec>(cache, operation, HumanJudgmentCustomFieldOperationKinds.Set);
        var fieldId = ResolveField(cache);
        var before = Read(cache, record, fieldId);
        if (before.Count != 0)
            throw new InvalidOperationException("A judgment revision already has a reserved value; create a new Notebook record.");

        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var recordId = CanonicalId.FromGuid(record.Guid).Value;
        var judgment = HumanJudgmentCodec.Parse(value, projectId, recordId);
        var agentDisposition = judgment.Actor?.Kind == JudgmentActorKind.Agent &&
                               judgment.Body is DispositionJudgment;
        var agentRetraction = judgment.Actor?.Kind == JudgmentActorKind.Agent &&
                              judgment.Body is RetractionJudgment;
        var humanReviewedJudgment = judgment.Actor?.Kind == JudgmentActorKind.Human &&
                                    judgment.Body is ReviewedNegativeJudgment or RetractionJudgment;
        if (!agentDisposition && !agentRetraction && !humanReviewedJudgment)
            throw new InvalidOperationException(
                "This field operation accepts agent dispositions and retractions or human-reviewed judgments.");

        cache.DomainDataByFlid.SetString(record.Hvo, fieldId,
            TsStringUtils.MakeString(value, cache.DefaultAnalWs));
        touchedTargets.Add(id);
        return new ExpectedEffect(id, SnapshotFields.RnGenericRecMotifHumanJudgment,
            before, Read(cache, record, fieldId));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, record) = TargetResolution.Resolve<IRnGenericRec>(cache, operation, HumanJudgmentCustomFieldOperationKinds.Set);
        var fieldId = ResolveField(cache);
        var current = Read(cache, record, fieldId);
        return new ExpectedEffect(id, SnapshotFields.RnGenericRecMotifHumanJudgment, current, current);
    }

    private static int ResolveField(LcmCache cache)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var fields = metadata.GetFieldIds()
            .Where(flid => string.Equals(metadata.GetFieldName(flid), "MotifHumanJudgment", StringComparison.Ordinal))
            .ToArray();
        if (fields.Length == 0)
            throw new InvalidOperationException("The reserved judgment field is missing; run project initialize first.");
        if (fields.Length != 1)
            throw new InvalidOperationException("The reserved judgment field name is ambiguous; resolve the project schema before authoring.");

        var field = fields[0];
        if (!string.Equals(metadata.GetOwnClsName(field), "RnGenericRec", StringComparison.Ordinal) ||
            (CellarPropertyType)metadata.GetFieldType(field) != CellarPropertyType.String ||
            !metadata.IsCustom(field) || metadata.GetFieldWs(field) != WritingSystemServices.kwsAnal ||
            metadata.GetDstClsId(field) != 0 || metadata.GetFieldListRoot(field) != Guid.Empty)
            throw new InvalidOperationException("The reserved judgment field has an incompatible schema; resolve it before authoring.");
        return field;
    }

    private static Dictionary<string, string> Read(LcmCache cache, IRnGenericRec record, int fieldId)
    {
        var text = cache.DomainDataByFlid.get_StringProp(record.Hvo, fieldId)?.Text;
        return string.IsNullOrEmpty(text)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["text"] = text.Normalize(System.Text.NormalizationForm.FormD),
            };
    }
}
