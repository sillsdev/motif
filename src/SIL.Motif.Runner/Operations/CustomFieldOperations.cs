using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// The operation kinds that set and clear the value of a project's custom field on any object that carries it. The
/// field is named in the payload by its LibLCM metadata name (the record file's <c>custom-&lt;Class&gt;-&lt;name&gt;</c>
/// key maps to it), because a custom field exists only in the project, so no kind can be generated for it.
/// </summary>
/// <remarks>
/// <para>
/// FieldWorks writes a custom field's value through <c>ISilDataAccess</c> by field id, which is what each handler does:
/// an integer is <c>SetInt</c> (DetailControls/BasicTypeSlices.cs, <c>IntegerSlice.m_tb_LostFocus</c>); an atomic
/// reference is <c>SetObjProp</c> (DetailControls/AtomicReferenceLauncher.cs and ReferenceComboBoxSlice.cs); a reference
/// collection is <c>Replace</c> on the vector (DetailControls/VectorReferenceLauncher.cs, <c>AddNewObjectsToProperty</c>
/// and <c>RemoveFromPropertyAt</c>); a string is <c>SetString</c> (Common/Framework/FwEditingHelper.cs) and a
/// multilingual string is <c>SetMultiStringAlt</c> (Common/Controls/Widgets/FwMultilingualPropView.cs). The types
/// FieldWorks lets a user create are in xWorks/AddCustomFieldDlg.cs, <c>SetFieldType</c>: String, MultiUnicode, an
/// owned StText, Integer, GenDate, ReferenceAtomic and ReferenceCollection.
/// </para>
/// <para>
/// Not written here, and refused when a record file asks: Boolean, Guid, Unicode, Binary, Time, Float, reference
/// sequences and owning fields other than StText (FieldWorks has no editor that creates them); an owned StText (its
/// paragraphs need a composer); and GenDate (the record schema omits it, so no record file can carry a value).
/// </para>
/// </remarks>
public static class CustomFieldOperationKinds
{
    public const string SetAlternative = "customField/setAlternative";
    public const string ClearAlternative = "customField/clearAlternative";
    public const string SetString = "customField/setString";
    public const string ClearString = "customField/clearString";
    public const string SetInteger = "customField/setInteger";
    public const string ClearInteger = "customField/clearInteger";
    public const string SetReference = "customField/setReference";
    public const string ClearReference = "customField/clearReference";
    public const string AddReference = "customField/addReference";
    public const string RemoveReference = "customField/removeReference";

    /// <summary>
    /// The declaring class every custom-field declaration states. A custom field has no model class, so the declaration
    /// names the LibLCM property type in <see cref="FieldWriteCapability.Field"/> instead, and a catalog lookup for a
    /// custom field resolves by that type.
    /// </summary>
    public const string DeclaringClass = "CustomField";

    /// <summary>The slot and effect field of one custom field.</summary>
    public static string SlotField(string name) => "customField/" + name;

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        RegisterKind(SetAlternative, new CustomFieldHandler(SetAlternative, ["field", "ws", "text"],
            [CellarPropertyType.MultiUnicode, CellarPropertyType.MultiString], CustomFieldHandler.WriteAlternative),
            "MultiUnicode", RecordWriteVerb.SetAlternative, alternative: true);
        RegisterKind(ClearAlternative, new CustomFieldHandler(ClearAlternative, ["field", "ws"],
            [CellarPropertyType.MultiUnicode, CellarPropertyType.MultiString], CustomFieldHandler.WriteAlternative),
            "MultiUnicode", RecordWriteVerb.ClearAlternative, alternative: true);
        OperationHandlerRegistry.Register(SetString, new CustomFieldHandler(SetString, ["field", "ws", "text"],
            [CellarPropertyType.String], CustomFieldHandler.WriteString), Declare("String", RecordWriteVerb.SetPlainRich));
        OperationHandlerRegistry.Register(ClearString, new CustomFieldHandler(ClearString, ["field", "ws"],
            [CellarPropertyType.String], CustomFieldHandler.WriteString), Declare("String", RecordWriteVerb.ClearRich));
        OperationHandlerRegistry.Register(SetInteger, new CustomFieldHandler(SetInteger, ["field", "value"],
            [CellarPropertyType.Integer], CustomFieldHandler.WriteInteger), Declare("Integer", RecordWriteVerb.Set));
        OperationHandlerRegistry.Register(ClearInteger, new CustomFieldHandler(ClearInteger, ["field"],
            [CellarPropertyType.Integer], CustomFieldHandler.WriteInteger), Declare("Integer", RecordWriteVerb.Clear));
        OperationHandlerRegistry.Register(SetReference, new CustomFieldHandler(SetReference, ["field", "ref"],
            [CellarPropertyType.ReferenceAtomic], CustomFieldHandler.WriteReference), Declare("ReferenceAtomic", RecordWriteVerb.Set));
        OperationHandlerRegistry.Register(ClearReference, new CustomFieldHandler(ClearReference, ["field"],
            [CellarPropertyType.ReferenceAtomic], CustomFieldHandler.WriteReference), Declare("ReferenceAtomic", RecordWriteVerb.Clear));
        OperationHandlerRegistry.Register(AddReference, new CustomFieldHandler(AddReference, ["field", "member"],
            [CellarPropertyType.ReferenceCollection], CustomFieldHandler.WriteMember), Declare("ReferenceCollection", RecordWriteVerb.AddReference));
        OperationHandlerRegistry.Register(RemoveReference, new CustomFieldHandler(RemoveReference, ["field", "member"],
            [CellarPropertyType.ReferenceCollection], CustomFieldHandler.WriteMember), Declare("ReferenceCollection", RecordWriteVerb.RemoveReference));

        foreach (var kind in new[] { SetString, ClearString, SetInteger, ClearInteger, SetReference, ClearReference, AddReference, RemoveReference })
            OperationKindRegistry.Register(kind);
        foreach (var kind in new[] { SetString, ClearString, SetInteger, ClearInteger, SetReference, ClearReference })
            OperationSlotRegistry.Register(kind, operation => new OperationSlot(OperationSlotRegistry.Target(operation), FieldOf(operation)));
        foreach (var kind in new[] { AddReference, RemoveReference })
            OperationSlotRegistry.Register(kind, operation => new OperationSlot(OperationSlotRegistry.Target(operation), FieldOf(operation),
                OperationSlotRegistry.MemberKey(ClosedPayloadParsing.GetRequiredCanonicalId(OperationSlotRegistry.After(operation), "member", kind))));
    }
#pragma warning restore CA2255

    private static void RegisterKind(string kind, IOperationHandler handler, string type, RecordWriteVerb verb, bool alternative)
    {
        OperationKindRegistry.Register(kind);
        OperationHandlerRegistry.Register(kind, handler, Declare(type, verb), Declare("MultiString", verb == RecordWriteVerb.SetAlternative
            ? RecordWriteVerb.SetPlainRich : RecordWriteVerb.ClearRich));
        if (alternative)
            OperationSlotRegistry.Register(kind, operation => new OperationSlot(OperationSlotRegistry.Target(operation), FieldOf(operation),
                ClosedPayloadParsing.GetRequiredString(OperationSlotRegistry.After(operation), "ws", kind)));
    }

    private static FieldWriteCapability Declare(string type, RecordWriteVerb verb) =>
        new(DeclaringClass, type, verb) { Payload = FieldWritePayload.Generic };

    private static string FieldOf(OperationEnvelope operation) =>
        SlotField(ClosedPayloadParsing.GetRequiredString(OperationSlotRegistry.After(operation), "field", operation.Kind));
}

/// <summary>One custom-field handler: it resolves the field on the target, reads it, writes it, and reads it again.</summary>
internal sealed class CustomFieldHandler(string kind, string[] allowed, CellarPropertyType[] types,
    Action<CustomFieldHandler.Write> write) : IOperationHandler
{
    /// <summary>What a write needs: the cache, the resolved target and field, and the payload.</summary>
    internal sealed record Write(LcmCache Cache, string Kind, ICmObject Target, int Flid, CellarPropertyType Type, JsonElement After);

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, context) = Resolve(cache, operation);
        touchedTargets.Add(id);
        var before = Read(context);
        write(context);
        return new ExpectedEffect(id, CustomFieldOperationKinds.SlotField(Name(operation)), before, Read(context));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, context) = Resolve(cache, operation);
        var current = Read(context);
        return new ExpectedEffect(id, CustomFieldOperationKinds.SlotField(Name(operation)), current, current);
    }

    private static string Name(OperationEnvelope operation) =>
        ClosedPayloadParsing.GetRequiredString(operation.After!.Value, "field", operation.Kind);

    private (CanonicalId Id, Write Context) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var target = operation.Target ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'target'.");
        var after = operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'after'.");
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, allowed, kind);
        var name = ClosedPayloadParsing.GetRequiredString(after, "field", kind);

        var instance = CanonicalIdResolver.Resolve(cache, target);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        if (!metadata.FieldExists(instance.ClassID, name, true))
            throw new InvalidOperationException($"'{kind}': {instance.ClassName} has no field '{name}'.");
        var flid = metadata.GetFieldId2(instance.ClassID, name, true);
        if (!metadata.IsCustom(flid))
            throw new InvalidOperationException(
                $"'{kind}': '{name}' on {instance.ClassName} is a stock field, which its own kinds write, not a custom field.");
        var type = (CellarPropertyType)metadata.GetFieldType(flid);
        if (!types.Contains(type))
            throw new InvalidOperationException(
                $"'{kind}': custom field '{name}' is {type}, which this kind does not write.");
        return (target, new Write(cache, kind, instance, flid, type, after));
    }

    private static IReadOnlyDictionary<string, string> Read(Write context)
    {
        var data = context.Cache.DomainDataByFlid;
        var hvo = context.Target.Hvo;
        switch (context.Type)
        {
            case CellarPropertyType.MultiUnicode or CellarPropertyType.MultiString:
                var alternatives = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var system in context.Cache.ServiceLocator.WritingSystems.AllWritingSystems)
                {
                    var text = data.get_MultiStringAlt(hvo, context.Flid, system.Handle);
                    if (text is { Length: > 0 })
                        alternatives[system.Id] = text.Text;
                }
                return alternatives;
            case CellarPropertyType.String:
                var held = data.get_StringProp(hvo, context.Flid);
                return held is { Length: > 0 }
                    ? new Dictionary<string, string> { [context.Cache.WritingSystemFactory.GetStrFromWs(
                        held.get_WritingSystem(0))] = held.Text }
                    : new Dictionary<string, string>();
            case CellarPropertyType.Integer:
                return IntegerFieldAlternatives.ToAlternatives(data.get_IntProp(hvo, context.Flid));
            case CellarPropertyType.ReferenceAtomic:
                var one = data.get_ObjectProp(hvo, context.Flid);
                return ReferenceFieldAlternatives.ToAlternatives(one == 0 ? null
                    : CanonicalId.FromGuid(context.Cache.ServiceLocator.GetObject(one).Guid));
            default:
                var members = new List<CanonicalId>();
                for (var index = 0; index < data.get_VecSize(hvo, context.Flid); index++)
                    members.Add(CanonicalId.FromGuid(context.Cache.ServiceLocator.GetObject(data.get_VecItem(hvo, context.Flid, index)).Guid));
                return ReferenceCollectionAlternatives.ToAlternatives(members);
        }
    }

    private static int Ws(Write context)
    {
        var tag = ClosedPayloadParsing.GetRequiredString(context.After, "ws", context.Kind);
        var handle = context.Cache.WritingSystemFactory.GetWsFromStr(tag);
        return handle != 0 ? handle : throw new InvalidOperationException(
            $"'{context.Kind}' operation: writing system tag '{tag}' is not known to this project.");
    }

    private static bool Clearing(Write context) => context.Kind.Contains("/clear", StringComparison.Ordinal);

    internal static void WriteAlternative(Write context)
    {
        var handle = Ws(context);
        var text = Clearing(context) ? "" : ClosedPayloadParsing.GetRequiredString(context.After, "text", context.Kind);
        context.Cache.DomainDataByFlid.SetMultiStringAlt(context.Target.Hvo, context.Flid, handle, TsStringUtils.MakeString(text, handle));
    }

    internal static void WriteString(Write context)
    {
        var handle = Ws(context);
        var text = Clearing(context) ? "" : ClosedPayloadParsing.GetRequiredString(context.After, "text", context.Kind);
        context.Cache.DomainDataByFlid.SetString(context.Target.Hvo, context.Flid, TsStringUtils.MakeString(text, handle));
    }

    internal static void WriteInteger(Write context) => context.Cache.DomainDataByFlid.SetInt(context.Target.Hvo, context.Flid,
        Clearing(context) ? 0 : ClosedPayloadParsing.GetRequiredInteger(context.After, "value", context.Kind));

    internal static void WriteReference(Write context)
    {
        var hvo = Clearing(context) ? 0 : Member(context, "ref").Hvo;
        context.Cache.DomainDataByFlid.SetObjProp(context.Target.Hvo, context.Flid, hvo);
    }

    internal static void WriteMember(Write context)
    {
        var member = Member(context, "member");
        var data = context.Cache.DomainDataByFlid;
        var size = data.get_VecSize(context.Target.Hvo, context.Flid);
        var at = Enumerable.Range(0, size).FirstOrDefault(index => data.get_VecItem(context.Target.Hvo, context.Flid, index) == member.Hvo, -1);
        if (context.Kind == CustomFieldOperationKinds.AddReference)
        {
            if (at < 0)
                data.Replace(context.Target.Hvo, context.Flid, size, size, [member.Hvo], 1);
        }
        else if (at >= 0)
            data.Replace(context.Target.Hvo, context.Flid, at, at + 1, [], 0);
    }

    /// <summary>Resolves the referenced object, checked against the field's class and list.</summary>
    private static ICmObject Member(Write context, string property)
    {
        var id = ClosedPayloadParsing.GetRequiredCanonicalId(context.After, property, context.Kind);
        var member = CanonicalIdResolver.Resolve(context.Cache, id);
        var metadata = (IFwMetaDataCacheManaged)context.Cache.MetaDataCacheAccessor;
        var destination = metadata.GetDstClsId(context.Flid);
        if (member.ClassID != destination && !metadata.GetAllSubclasses(destination).Contains(member.ClassID))
            throw new InvalidOperationException(
                $"'{context.Kind}' operation: referenced object '{id.Value}' is a {member.ClassName}, not a {metadata.GetClassName(destination)}.");
        var root = metadata.GetFieldListRoot(context.Flid);
        if (root != Guid.Empty && (member as ICmPossibility)?.OwningList.Guid != root)
            throw new InvalidOperationException(
                $"'{context.Kind}' operation: referenced object '{id.Value}' is not in the list this field was created over.");
        return member;
    }
}
