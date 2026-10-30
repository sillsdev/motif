using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the semantic replacement of a variant reference's target and type.</summary>
public static class VariantLinkOperationKinds
{
    public const string Set = "lexical/lexEntryRef/setVariantLink";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, VariantLinkHandler.Instance);
        OperationSlotRegistry.RegisterMany(Set, operation =>
        [
            new(OperationSlotRegistry.Target(operation), SnapshotFields.LexEntryRefComponentLexemes, WholeField: true),
            new(OperationSlotRegistry.Target(operation), SnapshotFields.LexEntryRefVariantEntryTypes, WholeField: true),
        ]);
    }
#pragma warning restore CA2255
}

/// <summary>Checks a variant link against the current project, including the reachable variant graph.</summary>
public static class VariantLinkSafety
{
    public static (ILexEntryRef Reference, ICmObject Component, ILexEntryType Type) Resolve(
        LcmCache cache, CanonicalId referenceId, CanonicalId componentId, CanonicalId typeId)
    {
        var reference = ResolveObject<ILexEntryRef>(cache, referenceId, RefusalCodes.VariantInvalidReference,
            "The target must be a variant reference owned by an entry.");
        if (reference.RefType != LexEntryRefTags.krtVariant || reference.Owner is not ILexEntry source)
            throw Refuse(RefusalCodes.VariantInvalidReference, "The target must be a variant reference owned by an entry.");
        var (component, type) = CheckLink(cache, source, componentId, typeId);
        return (reference, component, type);
    }


    /// <summary>Checks a proposed component and variant type for a variant entry: the component must be an entry or
    /// sense, the type must be in the variant types list, and the link must be neither a self-link nor a cycle.</summary>
    public static (ICmObject Component, ILexEntryType Type) CheckLink(
        LcmCache cache, ILexEntry source, CanonicalId componentId, CanonicalId typeId)
    {
        var component = ResolveObject<ICmObject>(cache, componentId, RefusalCodes.VariantInvalidComponent,
            "The component must be an entry or sense.");
        var destination = OwningEntry(component) ?? throw Refuse(RefusalCodes.VariantInvalidComponent, "The component must be an entry or sense.");
        var type = ResolveObject<ILexEntryType>(cache, typeId, RefusalCodes.VariantInvalidType,
            "The type must belong to the variant types list.");
        ICmObject? owner = type;
        var list = cache.LangProject.LexDbOA.VariantEntryTypesOA;
        while (owner is not null && owner.Guid != list?.Guid) owner = owner.Owner;
        if (owner is null) throw Refuse(RefusalCodes.VariantInvalidType, "The type must belong to the variant types list.");
        if (destination.Guid == source.Guid)
            throw Refuse(RefusalCodes.VariantSelfLink, "A variant cannot link to its own entry or sense.");
        var pending = new Stack<ILexEntry>();
        var seen = new HashSet<Guid>();
        pending.Push(destination);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.Guid == source.Guid)
                throw Refuse(RefusalCodes.VariantCycle, "The variant link would form a cycle.");
            if (!seen.Add(current.Guid)) continue;
            foreach (var next in current.EntryRefsOS.Where(r => r.RefType == LexEntryRefTags.krtVariant)
                         .SelectMany(r => r.ComponentLexemesRS).Select(OwningEntry).OfType<ILexEntry>())
                pending.Push(next);
        }
        return (component, type);
    }

    /// <summary>Throws the already-linked error when the source entry is a variant of the component.</summary>
    public static void RequireNotLinked(ILexEntry source, ICmObject component)
    {
        if (source.EntryRefsOS.Any(r => r.RefType == LexEntryRefTags.krtVariant &&
                                        r.ComponentLexemesRS.Any(c => c.Guid == component.Guid)))
            throw Refuse(RefusalCodes.VariantAlreadyLinked, "The entry is already a variant of that entry or sense.");
    }

    internal static VariantLinkRefusalException Refuse(string code, string message) => new(code, message);

    internal static T ResolveObject<T>(LcmCache cache, CanonicalId id, string code, string message) where T : ICmObject
    {
        try { return ReferenceFieldLowering.Resolve<T>(cache, id, VariantLinkOperationKinds.Set); }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw Refuse(code, message); }
    }

    private static ILexEntry? OwningEntry(ICmObject item)
    {
        if (item is ILexEntry entry) return entry;
        if (item is not ILexSense) return null;
        ICmObject? current = item.Owner;
        while (current is ILexSense) current = current.Owner;
        return current as ILexEntry;
    }
}

internal sealed class VariantLinkHandler : ICompoundOperationHandler
{
    internal static readonly VariantLinkHandler Instance = new();
    private const string Kind = VariantLinkOperationKinds.Set;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(LcmCache cache, OperationEnvelope operation,
        List<CanonicalId> touchedTargets)
    {
        var (id, reference, component, type, beforeComponents, beforeTypes) = Resolve(cache, operation);
        touchedTargets.Add(id);
        reference.ComponentLexemesRS.Clear();
        reference.ComponentLexemesRS.Add(component);
        reference.VariantEntryTypesRS.Clear();
        reference.VariantEntryTypesRS.Add(type);
        return Effects(id, reference, beforeComponents, beforeTypes);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (id, reference) = TargetResolution.Resolve<ILexEntryRef>(cache, operation, Kind);
        var beforeComponents = ReferenceSequenceFieldSnapshotting.ReadAlternatives(reference.ComponentLexemesRS);
        var beforeTypes = ReferenceSequenceFieldSnapshotting.ReadAlternatives(reference.VariantEntryTypesRS);
        return Effects(id, reference, beforeComponents, beforeTypes);
    }

    private static (CanonicalId, ILexEntryRef, ICmObject, ILexEntryType,
        IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var after = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["component", "variantType", "expectedComponents", "expectedTypes"], Kind);
        var component = ClosedPayloadParsing.GetRequiredCanonicalId(after, "component", Kind);
        var type = ClosedPayloadParsing.GetRequiredCanonicalId(after, "variantType", Kind);
        var expectedComponents = RetargetPayloadParsing.Ids(after, "expectedComponents", Kind, allowEmpty: true);
        var expectedTypes = RetargetPayloadParsing.Ids(after, "expectedTypes", Kind, allowEmpty: true);
        var id = OperationSlotRegistry.Target(operation);
        var (reference, target, variantType) = VariantLinkSafety.Resolve(cache, id, component, type);
        if (!reference.ComponentLexemesRS.Select(x => x.Guid).SequenceEqual(expectedComponents.Select(x => x.ToGuid())) ||
            !reference.VariantEntryTypesRS.Select(x => x.Guid).SequenceEqual(expectedTypes.Select(x => x.ToGuid())))
            throw VariantLinkSafety.Refuse(RefusalCodes.VariantStaleReference,
                "The variant reference has changed; compose the link again.");
        return (id, reference, target, variantType,
            ReferenceSequenceFieldSnapshotting.ReadAlternatives(reference.ComponentLexemesRS),
            ReferenceSequenceFieldSnapshotting.ReadAlternatives(reference.VariantEntryTypesRS));
    }

    private static IReadOnlyList<ExpectedEffect> Effects(CanonicalId id, ILexEntryRef reference,
        IReadOnlyDictionary<string, string> components, IReadOnlyDictionary<string, string> types) =>
    [
        new(id, SnapshotFields.LexEntryRefComponentLexemes, components,
            ReferenceSequenceFieldSnapshotting.ReadAlternatives(reference.ComponentLexemesRS)),
        new(id, SnapshotFields.LexEntryRefVariantEntryTypes, types,
            ReferenceSequenceFieldSnapshotting.ReadAlternatives(reference.VariantEntryTypesRS)),
    ];
}

internal sealed class VariantLinkRefusalException : ProposalRefusalException
{
    internal VariantLinkRefusalException(string code, string message)
        : base(code, FailureReason.Refused, message, new Dictionary<string, string>()) { }
}
