using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the creation of a complex-form reference on an existing or earlier-staged entry.</summary>
public static class ComplexFormRefCreateOperationKinds
{
    public const string Create = "lexical/lexEntry/createComplexFormRef";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, ComplexFormRefCreateHandler.Instance,
            new FieldWriteCapability("LexEntry", "EntryRefs", RecordWriteVerb.AddObject, Shape: FieldWriteShape.ComplexFormReference)
            { Payload = FieldWritePayload.Generic, ChildClasses = ["LexEntryRef"] });
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.LexEntryEntryRefs, false);
    }
#pragma warning restore CA2255
}

/// <summary>
/// Builds the complex-form reference the way FieldWorks does. InsertEntryDlg.CreateNewEntryInternal (its m_fComplexForm branch)
/// makes a LexEntryRef with RefType krtComplexForm and ComplexEntryTypes; the lexicon editor then adds each component to
/// ComponentLexemes (EntrySequenceReferenceLauncher.AddNewObjectsToProperty, mirrored by DetailComposer.ApplyComponentLexemesAddCoupling),
/// which also lists the component in ShowComplexFormsIn unless the type is a derivation. This payload names the components, the
/// optional primary lexemes (a subset of the components) and the optional ShowComplexFormsIn, which defaults to the components.
/// </summary>
internal sealed class ComplexFormRefCreateHandler : IOperationHandler
{
    internal static readonly ComplexFormRefCreateHandler Instance = new();
    private const string Kind = ComplexFormRefCreateOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var after = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after,
            ["complexTypes", "components", "primaryLexemes", "showComplexFormsIn", "hideMinorEntry"], Kind);
        var typeIds = RetargetPayloadParsing.Ids(after, "complexTypes", Kind, allowEmpty: true);
        var componentIds = RetargetPayloadParsing.Ids(after, "components", Kind);
        var primaryIds = after.TryGetProperty("primaryLexemes", out _) ? RetargetPayloadParsing.Ids(after, "primaryLexemes", Kind, allowEmpty: true) : [];
        var showIds = after.TryGetProperty("showComplexFormsIn", out _)
            ? RetargetPayloadParsing.Ids(after, "showComplexFormsIn", Kind, allowEmpty: true) : null;
        var hideMinorEntry = false;
        if (after.TryGetProperty("hideMinorEntry", out var hide))
            hideMinorEntry = hide.ValueKind is JsonValueKind.True or JsonValueKind.False ? hide.GetBoolean()
                : throw new ContractParseException($"'{Kind}' operation 'after.hideMinorEntry' must be true or false.");
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "LexEntryRef");

        var types = typeIds.Select(type => CheckType(cache, type)).ToList();
        var components = componentIds.Select(component => CheckComponent(cache, entry, component)).ToList();
        var primaries = primaryIds.Select(item => Subset(cache, components, item, "primary lexemes")).ToList();
        var shows = showIds?.Select(item => Subset(cache, components, item, "ShowComplexFormsIn")).ToList();
        if (shows is null)
        {
            var derivation = cache.ServiceLocator.GetInstance<ILexEntryTypeRepository>()
                .TryGetObject(LexEntryTypeTags.kguidLexTypDerivation, out var found) ? found : null;
            shows = derivation is not null && types.Any(type => type.Guid == derivation.Guid) ? [] : components.ToList();
        }

        var before = Read(entry, entityId);
        touchedTargets.Add(id);
        var reference = cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create(entityId.ToGuid());
        entry.EntryRefsOS.Add(reference);
        reference.RefType = LexEntryRefTags.krtComplexForm;
        reference.HideMinorEntry = hideMinorEntry ? 1 : 0;
        foreach (var type in types) reference.ComplexEntryTypesRS.Add(type);
        try
        {
            foreach (var component in components) reference.ComponentLexemesRS.Add(component);
            foreach (var primary in primaries) reference.PrimaryLexemesRS.Add(primary);
            reference.ShowComplexFormsInRS.Clear();
            foreach (var show in shows) reference.ShowComplexFormsInRS.Add(show);
        }
        catch (ArgumentException exception)
        {
            entry.EntryRefsOS.Remove(reference);
            throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidComponent,
                "FieldWorks does not take these components for a complex form: " + exception.Message);
        }
        return new ExpectedEffect(id, SnapshotFields.LexEntryEntryRefs, before, Read(entry, entityId));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
        var current = Read(entry, entityId);
        if (!entry.EntryRefsOS.Any(item => item.Guid == entityId.ToGuid()))
            CreateIdentity.AddCollision(cache, entityId, null, current);
        return new ExpectedEffect(id, SnapshotFields.LexEntryEntryRefs, current, current);
    }

    private static ILexEntryType CheckType(LcmCache cache, CanonicalId id)
    {
        var type = VariantLinkSafety.ResolveObject<ILexEntryType>(cache, id, RefusalCodes.VariantInvalidType,
            "The type must belong to the complex form types list.");
        ICmObject? owner = type;
        var list = cache.LangProject.LexDbOA.ComplexEntryTypesOA;
        while (owner is not null && owner.Guid != list?.Guid) owner = owner.Owner;
        if (owner is null)
            throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidType, "The type must belong to the complex form types list.");
        return type;
    }

    // An entry or sense other than the form itself or its senses, not already leading back to it.
    private static ICmObject CheckComponent(LcmCache cache, ILexEntry source, CanonicalId id)
    {
        var component = VariantLinkSafety.ResolveObject<ICmObject>(cache, id, RefusalCodes.VariantInvalidComponent,
            "The component must be an entry or sense.");
        var owning = OwningEntry(component) ?? throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidComponent,
            "The component must be an entry or sense.");
        if (owning.Guid == source.Guid)
            throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidComponent, "A complex form cannot have its own entry or sense as a component.");
        var pending = new Stack<ILexEntry>([owning]);
        var seen = new HashSet<Guid>();
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.Guid == source.Guid)
                throw VariantLinkSafety.Refuse(RefusalCodes.VariantCycle,
                    "The complex-form link would form a cycle: the component is already, through its own components, a complex form of the new entry.");
            if (!seen.Add(current.Guid)) continue;
            foreach (var next in current.EntryRefsOS.Where(item => item.RefType == LexEntryRefTags.krtComplexForm)
                         .SelectMany(item => item.ComponentLexemesRS).Select(OwningEntry).OfType<ILexEntry>())
                pending.Push(next);
        }
        return component;
    }

    private static ICmObject Subset(LcmCache cache, List<ICmObject> components, CanonicalId id, string what)
    {
        var item = VariantLinkSafety.ResolveObject<ICmObject>(cache, id, RefusalCodes.VariantInvalidComponent,
            "The " + what + " must be components of the complex form.");
        return components.FirstOrDefault(component => component.Guid == item.Guid) ??
            throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidComponent, "The " + what + " must be components of the complex form.");
    }

    private static ILexEntry? OwningEntry(ICmObject item)
    {
        if (item is ILexEntry entry) return entry;
        if (item is not ILexSense) return null;
        ICmObject? current = item.Owner;
        while (current is ILexSense) current = current.Owner;
        return current as ILexEntry;
    }

    private static Dictionary<string, string> Read(ILexEntry entry, CanonicalId entityId) =>
        entry.EntryRefsOS.Any(item => item.Guid == entityId.ToGuid())
            ? new(StringComparer.Ordinal) { ["ref"] = entityId.Value } : new(StringComparer.Ordinal);
}
