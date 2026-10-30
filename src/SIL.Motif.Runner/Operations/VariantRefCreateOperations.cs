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

/// <summary>Names the creation of a variant reference on an existing or earlier-staged entry.</summary>
public static class VariantRefCreateOperationKinds
{
    public const string Create = "lexical/lexEntry/createVariantRef";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, VariantRefCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.LexEntryEntryRefs, false);
    }
#pragma warning restore CA2255
}

/// <summary>Builds the variant reference the way FieldWorks' Add Variant does for an existing entry: type, one component
/// lexeme, and no primary lexemes (those are for complex forms); HideMinorEntry keeps its default of 0, so the minor
/// entry shows.</summary>
internal sealed class VariantRefCreateHandler : IOperationHandler
{
    internal static readonly VariantRefCreateHandler Instance = new();
    private const string Kind = VariantRefCreateOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var after = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["component", "variantType"], Kind);
        var componentId = ClosedPayloadParsing.GetRequiredCanonicalId(after, "component", Kind);
        var typeId = ClosedPayloadParsing.GetRequiredCanonicalId(after, "variantType", Kind);
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "LexEntryRef");
        var (component, type) = VariantLinkSafety.CheckLink(cache, entry, componentId, typeId);
        VariantLinkSafety.RequireNotLinked(entry, component);
        var before = Read(entry, entityId);
        touchedTargets.Add(id);
        var reference = cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create(entityId.ToGuid());
        entry.EntryRefsOS.Add(reference);
        reference.RefType = LexEntryRefTags.krtVariant;
        reference.VariantEntryTypesRS.Add(type);
        reference.ComponentLexemesRS.Add(component);
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

    private static Dictionary<string, string> Read(ILexEntry entry, CanonicalId entityId) =>
        entry.EntryRefsOS.Any(item => item.Guid == entityId.ToGuid())
            ? new(StringComparer.Ordinal) { ["ref"] = entityId.Value } : new(StringComparer.Ordinal);
}
