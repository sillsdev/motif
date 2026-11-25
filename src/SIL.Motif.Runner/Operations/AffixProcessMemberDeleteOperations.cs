using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the operations that take one input context or one output mapping out of an affix process.</summary>
/// <remarks>
/// FieldWorks removes an input column with <c>Rule.InputOS.Remove</c> and an output mapping with <c>Rule.OutputOS.Remove</c>
/// (AffixRuleFormulaControl.RemoveItems and RemoveFromOutput); a column that was a sequence also drops the member contexts only it used
/// (RuleFormulaControl.ProcessIndicesSeqContext removes them from PhPhonData.ContextsOS).
/// </remarks>
public static class AffixProcessMemberDeleteOperationKinds
{
    /// <summary>Deletes one input context or variable of an affix process.</summary>
    public const string DeleteInput = "grammar/moAffixProcess/deleteInput";

    /// <summary>Deletes one output mapping of an affix process.</summary>
    public const string DeleteOutput = "grammar/moAffixProcess/deleteOutput";

    /// <summary>Deletes one simple or iteration context that a sequence or iteration no longer holds and nothing else uses.</summary>
    public const string DeleteContext = "grammar/phPhonData/deleteContext";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeleteInput);
        OperationKindRegistry.Register(DeleteOutput);
        OperationKindRegistry.Register(DeleteContext);
        OperationHandlerRegistry.Register(DeleteInput, new AffixProcessMemberDeleteHandler(DeleteInput, SnapshotFields.MoAffixProcessInput),
            new FieldWriteCapability("MoAffixProcess", "Input", RecordWriteVerb.RemoveObject)
            { Payload = FieldWritePayload.Composer, ChildClasses = ["PhVariable", "PhSimpleContextSeg", "PhSimpleContextNC", "PhSimpleContextBdry", "PhSequenceContext"] });
        OperationHandlerRegistry.Register(DeleteOutput, new AffixProcessMemberDeleteHandler(DeleteOutput, SnapshotFields.MoAffixProcessOutput),
            new FieldWriteCapability("MoAffixProcess", "Output", RecordWriteVerb.RemoveObject)
            { Payload = FieldWritePayload.Composer, ChildClasses = ["MoCopyFromInput", "MoInsertPhones", "MoInsertNC", "MoModifyFromInput"] });
        OperationHandlerRegistry.Register(DeleteContext, new AffixProcessMemberDeleteHandler(DeleteContext, SnapshotFields.PhPhonDataContexts),
            new FieldWriteCapability("PhPhonData", "Contexts", RecordWriteVerb.RemoveObject)
            { Payload = FieldWritePayload.Composer, ChildClasses = ["PhSimpleContextSeg", "PhSimpleContextNC", "PhSimpleContextBdry", "PhIterationContext"] });
        OperationSlotRegistry.RegisterEntityDeletion(DeleteInput);
        OperationSlotRegistry.RegisterEntityDeletion(DeleteOutput);
        OperationSlotRegistry.RegisterEntityDeletion(DeleteContext);
    }
#pragma warning restore CA2255
}

/// <summary>Deletes one member of an affix process's Input or Output sequence, or a context a sequence has dropped.</summary>
internal sealed class AffixProcessMemberDeleteHandler(string kind, string field) : IOperationHandler
{
    private bool IsInput => kind == AffixProcessMemberDeleteOperationKinds.DeleteInput;
    private bool IsContext => kind == AffixProcessMemberDeleteOperationKinds.DeleteContext;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, member) = Resolve(cache, operation, requireUnreferenced: true);
        touchedTargets.Add(id);
        var before = ReferenceFieldAlternatives.ToAlternatives(id);
        if (member is IPhSequenceContext sequence)
        {
            var data = cache.LangProject.PhonologicalDataOA;
            foreach (var part in sequence.MembersRS.ToArray())
                if (part.Owner == data && part.ReferringObjects.All(user => user == sequence))
                    part.Delete();
        }
        member.Delete();
        return new ExpectedEffect(id, field, before, ReferenceFieldAlternatives.ToAlternatives(null));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, _) = Resolve(cache, operation, requireUnreferenced: false);
        var current = ReferenceFieldAlternatives.ToAlternatives(id);
        return new ExpectedEffect(id, field, current, current);
    }

    private (CanonicalId Id, ICmObject Member) Resolve(LcmCache cache, OperationEnvelope operation, bool requireUnreferenced)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'after'.");
        FsFeatStrucFeatureSpecsPayload.Parse(after, kind);
        var (id, member) = TargetResolution.Resolve<ICmObject>(cache, operation, kind);
        var ok = IsContext ? member is IPhSimpleContext or IPhIterationContext && member.Owner is IPhPhonData : IsInput ? member is IPhVariable or IPhSimpleContext or IPhSequenceContext && member.Owner is IMoAffixProcess process && process.InputOS.Contains(member)
            : member is IMoRuleMapping && member.Owner is IMoAffixProcess owner && owner.OutputOS.Contains(member);
        if (!ok)
            throw new InvalidOperationException($"'{kind}': '{id.Value}' is not {(IsContext ? "a simple or iteration context of the phonological data" : IsInput ? "a member of an affix process's Input" : "a member of an affix process's Output")}.");
        if (requireUnreferenced && IsContext)
        {
            var user = member.ReferringObjects.FirstOrDefault();
            if (user is not null)
                throw new InvalidOperationException($"'{kind}': '{id.Value}' is still used by a {user.ClassName} ({user.Guid:D}).");
        }
        if (requireUnreferenced && IsInput)
        {
            var user = member.ReferringObjects.FirstOrDefault(r => r is IMoCopyFromInput or IMoModifyFromInput);
            if (user is not null)
                throw new InvalidOperationException($"'{kind}': '{id.Value}' is still the source of a {user.ClassName} ({user.Guid:D}); change that output first.");
        }
        return (id, member);
    }
}
