using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Parsing;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Where the Motif Sources list keeps each part of a Source in the project's own data. The one place these names are
/// written: <c>SourceListFormat</c> in the Scripture library reads them from here.
/// </summary>
public static class ScriptureSourceFields
{
    /// <summary>The name of the possibility list that holds the Sources.</summary>
    public const string ListName = "Motif Sources";

    /// <summary>The internal name of the custom field that holds the kind.</summary>
    public const string KindField = "MotifSourceKind";

    /// <summary>The internal name of the custom field that holds the versification name.</summary>
    public const string VersificationField = "MotifSourceVersification";

    /// <summary>The internal name of the custom field that holds the status.</summary>
    public const string StatusField = "MotifSourceStatus";

    /// <summary>The class the custom fields belong to.</summary>
    public const string FieldOwnerClass = "CmPossibility";

    /// <summary>The stored text of an Active Source.</summary>
    public const string Active = "Active";

    /// <summary>The stored text of a removed Source.</summary>
    public const string Removed = "Removed";

    /// <summary>The stored text of the only kind that is built.</summary>
    public const string Paratext = "Paratext";
}

/// <summary>
/// The operations that write the "Motif Sources" list. The list is found by its name and created by the first
/// <see cref="Create"/>; an item is a CmPossibility whose Abbreviation is the Source id, Name its display name,
/// Description its Context text, and whose three reserved custom String fields hold kind, versification and status.
/// The item's text goes in the project's default analysis writing system; the other alternatives of a field are
/// cleared so a Source has one name and one Context.
/// </summary>
public static class ScriptureSourceOperationKinds
{
    /// <summary>Adds a Source: {id, name, versification, context, kind}; the target is the language project.</summary>
    public const string Create = "scripture/source/create";

    /// <summary>Sets a Source's name: {name}; the target is the item.</summary>
    public const string SetName = "scripture/source/setName";

    /// <summary>Sets a Source's Context text: {context}; the target is the item.</summary>
    public const string SetContext = "scripture/source/setContext";

    /// <summary>Sets a Source's versification: {versification}; the target is the item.</summary>
    public const string SetVersification = "scripture/source/setVersification";

    /// <summary>Sets a Source's status: {status} Active or Removed; the target is the item.</summary>
    public const string SetStatus = "scripture/source/setStatus";

    /// <summary>
    /// Throws when one of the reserved custom fields is ambiguous or incompatible. Read-only: it never writes, so
    /// Preflight can run the check Apply runs before it touches the project. A missing field is fine: Apply adds it.
    /// </summary>
    public static void RequireCompatibleFields(LcmCache cache) => ScriptureSourceSupport.RequireCompatible(cache);

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        foreach (var kind in new[] { Create, SetName, SetContext, SetVersification, SetStatus })
            OperationKindRegistry.Register(kind);
        OperationHandlerRegistry.Register(Create, ScriptureSourceCreateHandler.Instance);
        OperationHandlerRegistry.Register(SetName, new ScriptureSourceSetHandler(SetName, "name",
            SnapshotFields.CmPossibilityName, ScriptureSourceSupport.ReadName, ScriptureSourceSupport.WriteName));
        OperationHandlerRegistry.Register(SetContext, new ScriptureSourceSetHandler(SetContext, "context",
            SnapshotFields.CmPossibilityDescription, ScriptureSourceSupport.ReadContext, ScriptureSourceSupport.WriteContext));
        OperationHandlerRegistry.Register(SetVersification, new ScriptureSourceSetHandler(SetVersification, "versification",
            SnapshotFields.CmPossibilityMotifSourceVersification,
            (cache, item) => ScriptureSourceSupport.ReadCustom(cache, item, ScriptureSourceFields.VersificationField),
            (cache, item, value) => ScriptureSourceSupport.WriteCustom(cache, item, ScriptureSourceFields.VersificationField, value)));
        OperationHandlerRegistry.Register(SetStatus, new ScriptureSourceSetHandler(SetStatus, "status",
            SnapshotFields.CmPossibilityMotifSourceStatus,
            (cache, item) => ScriptureSourceSupport.ReadCustom(cache, item, ScriptureSourceFields.StatusField),
            (cache, item, value) => ScriptureSourceSupport.WriteCustom(cache, item, ScriptureSourceFields.StatusField, value),
            ScriptureSourceSupport.RequireStatus));
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.ScriptureSourceItems, false);
        OperationSlotRegistry.RegisterScalar(SetName, SnapshotFields.CmPossibilityName);
        OperationSlotRegistry.RegisterScalar(SetContext, SnapshotFields.CmPossibilityDescription);
        OperationSlotRegistry.RegisterScalar(SetVersification, SnapshotFields.CmPossibilityMotifSourceVersification);
        OperationSlotRegistry.RegisterScalar(SetStatus, SnapshotFields.CmPossibilityMotifSourceStatus);
    }
#pragma warning restore CA2255
}

/// <summary>Finds the Sources list and its items in a project copy, for the code that composes and checks Source edits.</summary>
public static class ScriptureSourceLookup
{
    /// <summary>The "Motif Sources" list, or null when the project has none; throws when two lists carry the name.</summary>
    public static ICmPossibilityList? FindList(LcmCache cache) => ScriptureSourceSupport.FindList(cache);

    /// <summary>The Source id an item carries: its Abbreviation text.</summary>
    public static string IdOf(ICmPossibility item) => ScriptureSourceSupport.IdOf(item);

    /// <summary>The item whose id is <paramref name="id"/>, compared ignoring case, or null; throws when the list is ambiguous.</summary>
    public static ICmPossibility? FindItem(LcmCache cache, string id) =>
        FindList(cache)?.PossibilitiesOS.FirstOrDefault(item =>
            string.Equals(IdOf(item), id, StringComparison.OrdinalIgnoreCase));
}

internal static class ScriptureSourceSupport
{
    internal static readonly string[] CustomFields =
    [
        ScriptureSourceFields.KindField, ScriptureSourceFields.VersificationField, ScriptureSourceFields.StatusField,
    ];

    internal static void RequireCompatible(LcmCache cache)
    {
        foreach (var name in CustomFields) _ = FindField(cache, name, create: false);
    }

    internal static string[] TextsOf(IMultiAccessorBase text) =>
        text.AvailableWritingSystemIds.Select(ws => text.get_String(ws).Text ?? "").ToArray();

    internal static ICmPossibilityList[] FindLists(LcmCache cache) =>
        cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances()
            .Where(list => TextsOf(list.Name).Any(name => name == ScriptureSourceFields.ListName)).ToArray();

    /// <summary>The one list, or null when there is none; throws when two lists carry the name.</summary>
    internal static ICmPossibilityList? FindList(LcmCache cache)
    {
        var lists = FindLists(cache);
        if (lists.Length > 1)
            throw new InvalidOperationException(
                $"{lists.Length} possibility lists are named '{ScriptureSourceFields.ListName}'. " +
                "Rename or remove all but one in FieldWorks.");
        return lists.Length == 0 ? null : lists[0];
    }

    internal static string IdOf(ICmPossibility item) =>
        (item.Abbreviation.AvailableWritingSystemIds.Select(ws => item.Abbreviation.get_String(ws).Text ?? "")
            .FirstOrDefault(text => text.Length > 0) ?? "").Trim();

    /// <summary>Resolves the reserved custom field, adding it when <paramref name="create"/> and the project has none.</summary>
    internal static int FindField(LcmCache cache, string name, bool create)
    {
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var fields = metadata.GetFieldIds()
            .Where(flid => string.Equals(metadata.GetFieldName(flid), name, StringComparison.Ordinal)
                && string.Equals(metadata.GetOwnClsName(flid), ScriptureSourceFields.FieldOwnerClass, StringComparison.Ordinal))
            .ToArray();
        if (fields.Length == 0)
        {
            if (!create) return 0;
            metadata.AddCustomField(ScriptureSourceFields.FieldOwnerClass, name, CellarPropertyType.String, 0, name,
                WritingSystemServices.kwsAnal, Guid.Empty);
            return FindField(cache, name, create: false);
        }
        if (fields.Length != 1)
            throw new InvalidOperationException(
                $"The reserved Source field {name} is ambiguous; resolve the project schema before authoring.");
        var field = fields[0];
        if ((CellarPropertyType)metadata.GetFieldType(field) != CellarPropertyType.String || !metadata.IsCustom(field) ||
            metadata.GetFieldWs(field) != WritingSystemServices.kwsAnal || metadata.GetDstClsId(field) != 0 ||
            metadata.GetFieldListRoot(field) != Guid.Empty)
            throw new InvalidOperationException(
                $"The reserved Source field {name} has an incompatible schema; resolve it before authoring.");
        return field;
    }

    internal static Dictionary<string, string> ReadCustom(LcmCache cache, ICmPossibility item, string name)
    {
        var flid = FindField(cache, name, create: false);
        var text = flid == 0 ? null : cache.DomainDataByFlid.get_StringProp(item.Hvo, flid)?.Text;
        return string.IsNullOrEmpty(text)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = text.Normalize(System.Text.NormalizationForm.FormD) };
    }

    internal static void WriteCustom(LcmCache cache, ICmPossibility item, string name, string value)
    {
        var flid = FindField(cache, name, create: true);
        cache.DomainDataByFlid.SetString(item.Hvo, flid, TsStringUtils.MakeString(value, cache.DefaultAnalWs));
    }

    internal static Dictionary<string, string> ReadName(LcmCache cache, ICmPossibility item) =>
        MultiAlternativesFieldSnapshotting.ReadAlternatives(cache, item.Name);

    internal static Dictionary<string, string> ReadContext(LcmCache cache, ICmPossibility item) =>
        MultiAlternativesFieldSnapshotting.ReadAlternatives(cache, item.Description);

    internal static void WriteName(LcmCache cache, ICmPossibility item, string value)
    {
        var ws = cache.DefaultAnalWs;
        item.Name.set_String(ws, TsStringUtils.MakeString(value, ws));
        foreach (var other in item.Name.AvailableWritingSystemIds.Where(other => other != ws).ToArray())
            item.Name.set_String(other, TsStringUtils.MakeString("", other));
    }

    internal static void WriteContext(LcmCache cache, ICmPossibility item, string value)
    {
        var ws = cache.DefaultAnalWs;
        item.Description.set_String(ws, TsStringUtils.MakeString(value, ws));
        foreach (var other in item.Description.AvailableWritingSystemIds.Where(other => other != ws).ToArray())
            item.Description.set_String(other, TsStringUtils.MakeString("", other));
    }

    internal static void RequireStatus(string value, string kind)
    {
        if (value is not (ScriptureSourceFields.Active or ScriptureSourceFields.Removed))
            throw new InvalidOperationException($"'{kind}': status must be {ScriptureSourceFields.Active} or {ScriptureSourceFields.Removed}.");
    }

    internal static void RequireId(string id, string kind)
    {
        if (id.Length == 0 || id != id.Trim() || id.Any(char.IsWhiteSpace))
            throw new InvalidOperationException($"'{kind}': a Source id is not empty and has no spaces.");
    }
}

/// <summary>Sets one text part of an existing Source item.</summary>
internal sealed class ScriptureSourceSetHandler(
    string kind, string property, string field,
    Func<LcmCache, ICmPossibility, Dictionary<string, string>> read,
    Action<LcmCache, ICmPossibility, string> write,
    Action<string, string>? validate = null) : IOperationHandler
{
    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var after = operation.After ?? throw new InvalidOperationException($"'{kind}' requires after.");
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, [property], kind);
        var value = ClosedPayloadParsing.GetRequiredString(after, property, kind);
        validate?.Invoke(value, kind);
        var (id, item) = TargetResolution.Resolve<ICmPossibility>(cache, operation, kind);
        var before = read(cache, item);
        write(cache, item, value);
        touchedTargets.Add(id);
        return new ExpectedEffect(id, field, before, read(cache, item));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, item) = TargetResolution.Resolve<ICmPossibility>(cache, operation, kind);
        var current = read(cache, item);
        return new ExpectedEffect(id, field, current, current);
    }
}

/// <summary>Adds a Source: the list when the project has none, the item at the end of it, and the reserved fields.</summary>
internal sealed class ScriptureSourceCreateHandler : IOperationHandler
{
    internal static readonly ScriptureSourceCreateHandler Instance = new();
    private ScriptureSourceCreateHandler() { }

    private const string Kind = ScriptureSourceOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var after = operation.After ?? throw new InvalidOperationException($"'{Kind}' requires after.");
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["id", "name", "versification", "context", "kind"], Kind);
        var sourceId = ClosedPayloadParsing.GetRequiredString(after, "id", Kind);
        var name = ClosedPayloadParsing.GetRequiredString(after, "name", Kind);
        var versification = ClosedPayloadParsing.GetRequiredString(after, "versification", Kind);
        var context = ClosedPayloadParsing.GetRequiredString(after, "context", Kind);
        var kind = ClosedPayloadParsing.GetRequiredString(after, "kind", Kind);
        ScriptureSourceSupport.RequireId(sourceId, Kind);
        if (kind != ScriptureSourceFields.Paratext)
            throw new InvalidOperationException($"'{Kind}': only a {ScriptureSourceFields.Paratext} Source can be added.");
        if (versification.Length == 0)
            throw new InvalidOperationException($"'{Kind}': a Source needs a versification.");
        var (id, project) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "CmPossibility");
        var list = ScriptureSourceSupport.FindList(cache);
        if (list is not null && ItemWithId(list, sourceId, entityId) is not null)
            throw new InvalidOperationException($"'{Kind}': the Source id '{sourceId}' is already in use.");

        var before = Read(cache, entityId, sourceId);
        // The fields are checked first so a project with an unusable field changes nothing.
        ScriptureSourceSupport.RequireCompatible(cache);
        var ws = cache.DefaultAnalWs;
        if (list is null)
        {
            list = cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>()
                .CreateUnowned(ScriptureSourceFields.ListName, ws);
        }
        var item = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create(entityId.ToGuid(), list);
        item.Abbreviation.set_String(ws, TsStringUtils.MakeString(sourceId, ws));
        ScriptureSourceSupport.WriteName(cache, item, name);
        ScriptureSourceSupport.WriteContext(cache, item, context);
        ScriptureSourceSupport.WriteCustom(cache, item, ScriptureSourceFields.KindField, kind);
        ScriptureSourceSupport.WriteCustom(cache, item, ScriptureSourceFields.VersificationField, versification);
        ScriptureSourceSupport.WriteCustom(cache, item, ScriptureSourceFields.StatusField, ScriptureSourceFields.Active);
        touchedTargets.Add(id);
        return new ExpectedEffect(id, SnapshotFields.ScriptureSourceItems, before, Read(cache, entityId, sourceId));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var (id, _) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        var sourceId = operation.After is { ValueKind: System.Text.Json.JsonValueKind.Object } after &&
            after.TryGetProperty("id", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? "" : "";
        var current = Read(cache, entityId, sourceId);
        return new ExpectedEffect(id, SnapshotFields.ScriptureSourceItems, current, current);
    }

    private static ICmPossibility? ItemWithId(ICmPossibilityList list, string sourceId, CanonicalId except) =>
        list.PossibilitiesOS.FirstOrDefault(item => item.Guid != except.ToGuid() &&
            string.Equals(ScriptureSourceSupport.IdOf(item), sourceId, StringComparison.OrdinalIgnoreCase));

    // The footprint says whether the item exists, whether the identity names something else, and who holds the id.
    private static Dictionary<string, string> Read(LcmCache cache, CanonicalId entityId, string sourceId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = ScriptureSourceSupport.FindList(cache);
        var inList = list?.PossibilitiesOS.Any(item => item.Guid == entityId.ToGuid()) == true;
        if (inList) result["ref"] = entityId.Value;
        else if (cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(entityId.ToGuid()))
            result["collision"] = entityId.Value;
        if (list is not null && sourceId.Length > 0 && ItemWithId(list, sourceId, entityId) is { } holder)
            result["idHeldBy"] = CanonicalId.FromGuid(holder.Guid).Value;
        return result;
    }
}
