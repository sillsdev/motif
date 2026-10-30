using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation kind that removes one alternate form (allomorph) of an entry that nothing refers to.</summary>
public static class AlternateFormRemoveOperationKinds
{
    /// <summary>Deletes the named alternate form from the targeted <c>LexEntry</c>.</summary>
    public const string Remove = "lexical/lexEntry/removeAlternateForm";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Remove);
        OperationHandlerRegistry.Register(Remove, AlternateFormRemoveHandler.Instance);
        OperationSlotRegistry.RegisterMany(Remove, operation =>
        {
            var form = AlternateFormRemovePayload.Parse(OperationSlotRegistry.After(operation)).Form;
            return
            [
                new OperationSlot(OperationSlotRegistry.Target(operation), SnapshotFields.LexEntryAlternateForms,
                    OperationSlotRegistry.MemberKey(form)),
                new OperationSlot(form, null),
            ];
        });
    }
#pragma warning restore CA2255
}

/// <summary>The closed payload of an alternate-form removal.</summary>
/// <param name="Form">The alternate form to delete.</param>
public sealed record AlternateFormRemovePayload(CanonicalId Form)
{
    /// <summary>Parses the closed payload, refusing any other property.</summary>
    public static AlternateFormRemovePayload Parse(JsonElement after)
    {
        const string kind = AlternateFormRemoveOperationKinds.Remove;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["form"], kind);
        return new AlternateFormRemovePayload(
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "form", kind).ToGuid()));
    }
}

/// <summary>Decides whether an alternate form may be removed, shared by the composer and the handler.</summary>
internal static class AlternateFormRemoval
{
    private const string Kind = AlternateFormRemoveOperationKinds.Remove;

    /// <summary>Finds the form among the entry's alternate forms and throws when something refers to it.</summary>
    internal static IMoForm Inspect(ILexEntry entry, Guid formId)
    {
        var form = entry.AlternateFormsOS.FirstOrDefault(item => item.Guid == formId) ??
            throw new InvalidOperationException(
                $"'{Kind}' removes only an alternate form owned by the entry '{entry.HeadWord?.Text}'; " +
                "the lexeme form cannot be removed this way.");
        foreach (var referrer in form.ReferringObjects)
            throw new InvalidOperationException(
                $"'{Kind}' refuses the alternate form {Describe(form)} because {SenseRemoval.Describe(referrer)} " +
                "still refers to it; retarget or remove that reference first.");
        return form;
    }

    internal static string Describe(IMoForm form) =>
        $"'{form.Form.BestVernacularAlternative?.Text}' ({form.ClassName} {CanonicalId.FromGuid(form.Guid).Value})";
}

/// <summary>Removes one alternate form, the way FieldWorks deletes an allomorph, refusing one that anything uses.</summary>
internal sealed class AlternateFormRemoveHandler : IOperationHandler
{
    internal static readonly AlternateFormRemoveHandler Instance = new();
    private const string Kind = AlternateFormRemoveOperationKinds.Remove;

    private AlternateFormRemoveHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, entry, formId) = Resolve(operation, cache);
        var form = AlternateFormRemoval.Inspect(entry, formId.ToGuid());
        var before = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        var preview = Preview(entry, form);
        touchedTargets.Add(id);
        touchedTargets.Add(formId);
        form.Delete();
        return new ExpectedEffect(id, SnapshotFields.LexEntryAlternateForms, before,
            ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS), preview);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, entry, formId) = Resolve(operation, cache);
        var current = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        var form = entry.AlternateFormsOS.FirstOrDefault(item => item.Guid == formId.ToGuid());
        return new ExpectedEffect(id, SnapshotFields.LexEntryAlternateForms, current, current,
            form is null ? null : Preview(entry, form));
    }

    private static (CanonicalId Id, ILexEntry Entry, CanonicalId Form) Resolve(OperationEnvelope operation, LcmCache cache)
    {
        var payload = AlternateFormRemovePayload.Parse(operation.After ??
            throw new ContractParseException($"'{Kind}' operation requires 'after'."));
        var (id, entry) = TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
        return (id, entry, payload.Form);
    }

    /// <summary>The recorded before-state: what the form was, so a reviewer sees exactly what goes.</summary>
    private static JsonElement Preview(ILexEntry entry, IMoForm form)
    {
        var index = entry.AlternateFormsOS.IndexOf(form);
        return JsonSerializer.SerializeToElement(new
        {
            removedForm = new
            {
                id = CanonicalId.FromGuid(form.Guid).Value,
                entry = entry.HeadWord?.Text,
                @class = form.ClassName,
                text = form.Form.BestVernacularAlternative?.Text,
                forms = RemovalPreview.AllWritingSystems(form.Cache, form.Form),
                morphType = form.MorphTypeRA?.Name.BestAnalysisAlternative?.Text,
                index,
                environments = (form as IMoStemAllomorph)?.PhoneEnvRC.Count ?? (form as IMoAffixAllomorph)?.PhoneEnvRC.Count ?? 0,
            },
            reversal = "createAlternateForms with the same id, class, form text, morph type and placement",
        });
    }
}
