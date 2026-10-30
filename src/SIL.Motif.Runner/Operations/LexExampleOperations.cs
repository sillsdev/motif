using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation kinds that add an example sentence to a sense and move one to another sense.</summary>
public static class LexExampleOperationKinds
{
    /// <summary>Creates one example sentence, with its translations, in the targeted sense.</summary>
    public const string Create = "lexical/lexSense/createExamples";

    /// <summary>Moves the targeted <c>LexExampleSentence</c> to another sense, keeping its identity and translations.</summary>
    public const string Move = "lexical/lexExampleSentence/moveToSense";

    /// <summary>The slot field that says which sense owns an example.</summary>
    internal const string OwnerField = "lexical/lexExampleSentence/owner";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, LexExampleCreateHandler.Instance);
        OperationSlotRegistry.RegisterMany(Create, operation =>
        {
            var example = operation.EntityId ?? throw new ContractParseException($"'{Create}' requires 'entityId'.");
            var payload = LexExampleCreatePayload.Parse(OperationSlotRegistry.After(operation));
            var slots = new List<OperationSlot>
            {
                new(OperationSlotRegistry.Target(operation), SnapshotFields.LexSenseExamples,
                    OperationSlotRegistry.MemberKey(example)),
                new(example, "$identity"),
            };
            slots.AddRange(payload.Translations.Select(translation => new OperationSlot(translation.Id, "$identity")));
            return slots;
        });

        OperationKindRegistry.Register(Move);
        OperationHandlerRegistry.Register(Move, LexExampleMoveHandler.Instance);
        OperationSlotRegistry.RegisterMany(Move, operation =>
        {
            var example = OperationSlotRegistry.Target(operation);
            var payload = LexExampleMovePayload.Parse(OperationSlotRegistry.After(operation));
            var member = OperationSlotRegistry.MemberKey(example);
            return
            [
                new(example, OwnerField),
                new(payload.FromSense, SnapshotFields.LexSenseExamples, member, ReleasesMember: true),
                new(payload.ToSense, SnapshotFields.LexSenseExamples, member),
            ];
        });
    }
#pragma warning restore CA2255
}

/// <summary>One writing-system alternative of an example or translation.</summary>
/// <param name="Ws">The writing-system tag.</param>
/// <param name="Text">The text in that writing system; never empty.</param>
public sealed record ExampleForm(string Ws, string Text);

/// <summary>One translation of an example: its identity, its type, and its text in one or more writing systems.</summary>
/// <param name="Id">The new translation's identity, authored so a replay creates the same object.</param>
/// <param name="Type"><c>free</c>, <c>literal</c> or <c>back</c>.</param>
/// <param name="Text">The translation text, one entry per writing system.</param>
public sealed record ExampleTranslation(CanonicalId Id, string Type, IReadOnlyList<ExampleForm> Text);

/// <summary>
/// The closed payload of an example creation: <c>{ "text": [{ws,text}], "reference": "...", "translations":
/// [{ "id", "type", "text": [{ws,text}] }] }</c>. Only <c>text</c> is required.
/// </summary>
/// <param name="Text">The example sentence, one entry per writing system.</param>
/// <param name="Reference">The example's reference string, such as a text citation, or null.</param>
/// <param name="Translations">The translations, in order.</param>
public sealed record LexExampleCreatePayload(
    IReadOnlyList<ExampleForm> Text, string? Reference, IReadOnlyList<ExampleTranslation> Translations)
{
    private const string Kind = LexExampleOperationKinds.Create;

    /// <summary>The translation types an example accepts.</summary>
    public static readonly IReadOnlyList<string> TranslationTypes = ["free", "literal", "back"];

    /// <summary>Parses the closed payload, refusing any other property, an empty text and a repeated writing system.</summary>
    public static LexExampleCreatePayload Parse(JsonElement after)
    {
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["text", "reference", "translations"], Kind);
        var text = ExampleForms.Parse(after, "text", Kind);
        string? reference = null;
        if (after.TryGetProperty("reference", out var value) && value.ValueKind != JsonValueKind.Null)
            reference = ClosedPayloadParsing.GetRequiredString(after, "reference", Kind);
        var translations = new List<ExampleTranslation>();
        if (after.TryGetProperty("translations", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array)
                throw new ContractParseException($"'{Kind}' operation 'after.translations' must be an array.");
            foreach (var item in list.EnumerateArray())
            {
                ClosedPayloadParsing.RequireObject(item, Kind);
                ClosedPayloadParsing.RejectUnknownProperties(item, ["id", "type", "text"], Kind);
                var type = ClosedPayloadParsing.GetRequiredString(item, "type", Kind);
                if (!TranslationTypes.Contains(type))
                    throw new ContractParseException(
                        $"'{Kind}' operation translation type '{type}' must be {string.Join(", ", TranslationTypes)}.");
                translations.Add(new(
                    CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(item, "id", Kind).ToGuid()),
                    type, ExampleForms.Parse(item, "text", Kind)));
            }
        }
        if (translations.Select(item => item.Id).Distinct().Count() != translations.Count)
            throw new ContractParseException($"'{Kind}' operation 'after.translations' names an identity twice.");
        return new(text, reference, translations);
    }
}

/// <summary>
/// The closed payload of an example move: the sense the example leaves, which a different owner makes stale, and the
/// sense it joins, which an earlier operation of the same Draft may create. The place in the target's examples is the
/// operation's placement.
/// </summary>
/// <param name="FromSense">The sense that owns the example when the Draft is composed.</param>
/// <param name="ToSense">The sense that owns it afterwards.</param>
public sealed record LexExampleMovePayload(CanonicalId FromSense, CanonicalId ToSense)
{
    /// <summary>Parses the closed payload, refusing any other property.</summary>
    public static LexExampleMovePayload Parse(JsonElement after)
    {
        const string kind = LexExampleOperationKinds.Move;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["fromSense", "toSense"], kind);
        return new(
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "fromSense", kind).ToGuid()),
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "toSense", kind).ToGuid()));
    }
}

/// <summary>Reads and writes the writing-system alternatives of an example or translation.</summary>
internal static class ExampleForms
{
    /// <summary>Reads a nonempty array of <c>{ws, text}</c> with each writing system once.</summary>
    internal static IReadOnlyList<ExampleForm> Parse(JsonElement owner, string name, string kind)
    {
        if (!owner.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{kind}' operation '{name}' is required and must be an array of {{ws, text}}.");
        var forms = new List<ExampleForm>();
        foreach (var item in list.EnumerateArray())
        {
            ClosedPayloadParsing.RequireObject(item, kind);
            ClosedPayloadParsing.RejectUnknownProperties(item, ["ws", "text"], kind);
            var text = ClosedPayloadParsing.GetRequiredString(item, "text", kind);
            if (text.Length == 0)
                throw new ContractParseException($"'{kind}' operation '{name}' holds an empty text.");
            forms.Add(new(ClosedPayloadParsing.GetRequiredString(item, "ws", kind), text));
        }
        if (forms.Count == 0)
            throw new ContractParseException($"'{kind}' operation '{name}' needs text in at least one writing system.");
        if (forms.Select(form => form.Ws).Distinct(StringComparer.Ordinal).Count() != forms.Count)
            throw new ContractParseException($"'{kind}' operation '{name}' names a writing system twice.");
        return forms;
    }

    /// <summary>Sets every alternative; a tag the project does not know is refused.</summary>
    internal static void Apply(LcmCache cache, IMultiString field, IEnumerable<ExampleForm> forms, string kind)
    {
        foreach (var form in forms)
            MultiAlternativesFieldLowering.ApplySet(cache, field, form.Ws, form.Text, kind);
    }

    /// <summary>The Dry Run view of authored text: writing-system tag to text.</summary>
    internal static Dictionary<string, string> View(IEnumerable<ExampleForm> forms) =>
        forms.ToDictionary(form => form.Ws, form => form.Text, StringComparer.Ordinal);

    /// <summary>The possibility a translation type names.</summary>
    internal static ICmPossibility TypeOf(LcmCache cache, string type, string kind)
    {
        var guid = type switch
        {
            "free" => CmPossibilityTags.kguidTranFreeTranslation,
            "literal" => CmPossibilityTags.kguidTranLiteralTranslation,
            "back" => CmPossibilityTags.kguidTranBackTranslation,
            _ => throw new InvalidOperationException($"'{kind}': translation type '{type}' is unknown."),
        };
        return cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().TryGetObject(guid, out var found)
            ? found : throw new InvalidOperationException(
                $"'{kind}': this project has no '{type}' translation type.");
    }

    /// <summary>The reference string, in the default vernacular writing system as FieldWorks stores it.</summary>
    internal static ITsString Reference(LcmCache cache, string text) => TsStringUtils.MakeString(text, cache.DefaultVernWs);
}
