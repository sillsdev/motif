using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the generic edit of one String field or one writing system of a MultiString field (ADR 0095).</summary>
public static class TextRichStringOperationKinds
{
    public const string Set = "text/setRichString";

    /// <summary>
    /// The field an edit's slot names: the generated kinds' snapshot field when the field has them, so this kind and
    /// <c>lexical/lexSense/setDefinition</c> cannot both edit one alternative in a Draft; otherwise <c>text/</c> and the field.
    /// </summary>
    public static string SlotField(string field) => SnapshotFieldNames.TryGetValue(field, out var name) ? name : TextParagraphsOperationKinds.FieldPrefix + field;

    /// <summary>The field an edit's effect names: <c>text/</c>, the field and, for a MultiString field, the writing system.</summary>
    public static string EffectField(string field, string? ws) => TextParagraphsOperationKinds.FieldPrefix + field + (ws is null ? "" : "/" + ws);

    private static readonly IReadOnlyDictionary<string, string> SnapshotFieldNames = TextRichStringFields.Names
        .Select(name => (name, value: typeof(SnapshotFields).GetField(name.Replace(".", ""), BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string))
        .Where(pair => pair.value is not null)
        .ToDictionary(pair => pair.name, pair => pair.value!, StringComparer.Ordinal);

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, TextRichStringHandler.Instance);
        OperationSlotRegistry.Register(Set, operation =>
        {
            var (field, ws) = TextRichStringPayload.AddressOf(OperationSlotRegistry.After(operation));
            return new OperationSlot(OperationSlotRegistry.Target(operation), SlotField(field), ws);
        });
    }
#pragma warning restore CA2255
}

/// <summary>
/// The String and MultiString fields a person may edit as rich text: the inventory's in-scope settable ones that the
/// parser neither references nor reaches (an unconfirmed reach is not enough), less the fields other kinds own (a
/// paragraph's contents, a morph bundle's form and a judgment record's title). Each is read and written through the
/// project's metadata, so subclasses share a field.
/// </summary>
public static class TextRichStringFields
{
    // Whether each field is a MultiString (one alternative per writing system) rather than one String.
    private static readonly IReadOnlyDictionary<string, bool> Multi = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["CmMedia.Label"] = true,
        ["CmPicture.Caption"] = true,
        ["CmPicture.Description"] = true,
        ["CmPossibility.Description"] = true,
        ["CmTranslation.Translation"] = true,
        ["FsFeatDefn.Description"] = true,
        ["FsFeatStrucType.Description"] = true,
        ["FsSymFeatVal.Description"] = true,
        ["LexEntry.Bibliography"] = true,
        ["LexEntry.Comment"] = true,
        ["LexEntry.LiteralMeaning"] = true,
        ["LexEntry.Restrictions"] = true,
        ["LexEntry.SummaryDefinition"] = true,
        ["LexEntryRef.Summary"] = true,
        ["LexEtymology.Bibliography"] = true,
        ["LexEtymology.Comment"] = true,
        ["LexEtymology.LanguageNotes"] = true,
        ["LexEtymology.Note"] = true,
        ["LexEtymology.PrecComment"] = true,
        ["LexExampleSentence.Example"] = true,
        ["LexExampleSentence.Reference"] = false,
        ["LexExtendedNote.Discussion"] = true,
        ["LexPronunciation.CVPattern"] = false,
        ["LexPronunciation.Tone"] = false,
        ["LexReference.Comment"] = true,
        ["LexSense.AnthroNote"] = true,
        ["LexSense.Bibliography"] = true,
        ["LexSense.Definition"] = true,
        ["LexSense.DiscourseNote"] = true,
        ["LexSense.EncyclopedicInfo"] = true,
        ["LexSense.Exemplar"] = true,
        ["LexSense.GeneralNote"] = true,
        ["LexSense.GrammarNote"] = true,
        ["LexSense.PhonologyNote"] = true,
        ["LexSense.Restrictions"] = true,
        ["LexSense.ScientificName"] = false,
        ["LexSense.SemanticsNote"] = true,
        ["LexSense.SocioLinguisticsNote"] = true,
        ["LexSense.Source"] = false,
        ["LexSense.UsageNote"] = true,
        ["MoCompoundRule.Description"] = true,
        ["MoInflAffixSlot.Description"] = true,
        ["MoInflAffixTemplate.Description"] = true,
        ["MoInflClass.Description"] = true,
        ["MoReferralRule.Description"] = true,
        ["MoStemName.Description"] = true,
        ["MoStratum.Description"] = true,
        ["PhEnvironment.Description"] = true,
        ["PhNaturalClass.Description"] = true,
        ["PhPhoneme.BasicIPASymbol"] = false,
        ["PhPhonemeSet.Description"] = true,
        ["PhSegmentRule.Description"] = true,
        ["PhTerminalUnit.Description"] = true,
    };

    /// <summary>Every editable field, as <c>Class.Field</c>.</summary>
    public static IReadOnlyCollection<string> Names => Multi.Keys.ToArray();

    /// <summary>Whether <paramref name="field"/> is editable and holds one alternative per writing system.</summary>
    public static bool IsMultiString(string field) => Multi[field];

    internal static bool IsAllowed(string field) => Multi.ContainsKey(field);

    /// <summary>The field's id on <paramref name="owner"/>; an owner without the field is refused.</summary>
    internal static int Flid(ICmObject owner, string field)
    {
        var metadata = owner.Cache.ServiceLocator.GetInstance<IFwMetaDataCacheManaged>();
        var parts = field.Split('.');
        var declared = metadata.GetFieldId(parts[0], parts[1], true);
        var expected = Multi[field] ? CellarPropertyType.MultiString : CellarPropertyType.String;
        int onOwner;
        try
        {
            onOwner = metadata.GetFieldId2(owner.ClassID, parts[1], true);
        }
        catch (LcmInvalidFieldException)
        {
            onOwner = 0;
        }
        if (onOwner != declared || (CellarPropertyType)metadata.GetFieldType(declared) != expected)
            throw new ContractParseException(
                $"'{TextRichStringOperationKinds.Set}' target '{CanonicalId.FromGuid(owner.Guid).Value}' has no field '{field}'.");
        return declared;
    }

    internal static ITsString? Read(ICmObject owner, int flid, int ws) => ws == 0
        ? owner.Cache.DomainDataByFlid.get_StringProp(owner.Hvo, flid)
        : owner.Cache.DomainDataByFlid.get_MultiStringAlt(owner.Hvo, flid, ws);

    internal static void Write(ICmObject owner, int flid, int ws, ITsString value)
    {
        if (ws == 0) owner.Cache.DomainDataByFlid.SetString(owner.Hvo, flid, value);
        else owner.Cache.DomainDataByFlid.SetMultiStringAlt(owner.Hvo, flid, ws, value);
    }
}

/// <summary>What the field held when the edit began: its text and the <see cref="Contract.Responses.TextRunsDigest"/> of its runs.</summary>
public sealed record TextRichStringExpected(string Text, string Runs);

/// <summary>
/// The closed <c>after</c> of a <c>text/setRichString</c> operation: the field, the writing system of the alternative
/// (a MultiString field only), the new runs and what the field held when the edit began.
/// </summary>
public sealed record TextRichStringPayload(string Field, string? Ws, IReadOnlyList<TextRun> Runs, TextRichStringExpected Expected)
{
    private const string Kind = TextRichStringOperationKinds.Set;
    private static readonly string[] Properties = ["field", "ws", "runs", "expected"];
    private static readonly string[] ExpectedProperties = ["text", "runs"];

    /// <summary>The runs' text.</summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));

    /// <summary>The <c>after</c> JSON this payload is written as.</summary>
    public JsonElement ToJson()
    {
        var written = new Dictionary<string, object?> { ["field"] = Field };
        if (Ws is not null) written["ws"] = Ws;
        written["runs"] = Runs.Select(RichTextRuns.Write).ToArray();
        written["expected"] = new Dictionary<string, string> { ["text"] = Expected.Text, ["runs"] = Expected.Runs };
        return JsonSerializer.SerializeToElement(written);
    }

    /// <summary>Reads only the field and writing system, for the slot policy and for matching a Draft's edit.</summary>
    public static (string Field, string? Ws) AddressOf(JsonElement after)
    {
        ClosedPayloadParsing.RequireObject(after, Kind);
        return (ClosedPayloadParsing.GetRequiredString(after, "field", Kind), RichTextRuns.OptionalString(after, "ws", Kind));
    }

    /// <summary>Reads and checks a closed payload: a MultiString field names its writing system, a String field none.</summary>
    public static TextRichStringPayload Parse(JsonElement after)
    {
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, Properties, Kind);
        var field = ClosedPayloadParsing.GetRequiredString(after, "field", Kind);
        if (!TextRichStringFields.IsAllowed(field))
            throw Invalid($"field '{field}' is not an editable String or MultiString field; use one of {string.Join(", ", TextRichStringFields.Names)}");
        var ws = RichTextRuns.OptionalString(after, "ws", Kind);
        if (TextRichStringFields.IsMultiString(field) && string.IsNullOrEmpty(ws))
            throw Invalid($"'{field}' holds one alternative per writing system, so 'ws' names the one edited");
        if (!TextRichStringFields.IsMultiString(field) && ws is not null)
            throw Invalid($"'{field}' is one String, not an alternative, so it takes no 'ws'");
        var runs = RichTextRuns.ReadRuns(after, "runs", Kind);
        if (!after.TryGetProperty("expected", out var expected) || expected.ValueKind != JsonValueKind.Object)
            throw Invalid("'expected' is required and must be the field's starting text and runs");
        ClosedPayloadParsing.RejectUnknownProperties(expected, ExpectedProperties, Kind);
        return new(field, ws, runs, new(ClosedPayloadParsing.GetRequiredString(expected, "text", Kind),
            ClosedPayloadParsing.GetRequiredString(expected, "runs", Kind)));
    }

    private static Contract.Parsing.ContractParseException Invalid(string message) => RichTextRuns.Invalid(Kind, message);
}

internal sealed class TextRichStringHandler : IOperationHandler
{
    internal static readonly TextRichStringHandler Instance = new();

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = TextRichStringPayload.Parse(OperationSlotRegistry.After(operation));
        var (id, owner, flid, ws) = Resolve(cache, operation, payload.Field, payload.Ws);
        var current = TextRichStringFields.Read(owner, flid, ws);
        var before = State(current, cache);
        if (!Holds(current, payload.Expected, cache))
            throw new TextFieldStaleException(operation, id, payload.Field, payload.Ws);
        var handles = RichTextRuns.Handles(cache, payload.Runs, TextRichStringOperationKinds.Set);
        touchedTargets.Add(id);
        TextRichStringFields.Write(owner, flid, ws, RichTextRuns.Build(payload.Runs, handles, ws != 0 ? ws : cache.DefaultAnalWs));
        return new(id, TextRichStringOperationKinds.EffectField(payload.Field, payload.Ws), before,
            State(TextRichStringFields.Read(owner, flid, ws), cache));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (field, wsTag) = TextRichStringPayload.AddressOf(OperationSlotRegistry.After(operation));
        var (id, owner, flid, ws) = Resolve(cache, operation, field, wsTag);
        var current = State(TextRichStringFields.Read(owner, flid, ws), cache);
        return new(id, TextRichStringOperationKinds.EffectField(field, wsTag), current, current);
    }

    private static (CanonicalId Id, ICmObject Owner, int Flid, int Ws) Resolve(LcmCache cache, OperationEnvelope operation, string field, string? ws)
    {
        if (!TextRichStringFields.IsAllowed(field))
            throw new ContractParseException($"'{TextRichStringOperationKinds.Set}' operation: field '{field}' is not editable.");
        var (id, owner) = TargetResolution.Resolve<ICmObject>(cache, operation, TextRichStringOperationKinds.Set);
        var flid = TextRichStringFields.Flid(owner, field);
        return (id, owner, flid, ws is null ? 0 : RichTextRuns.WritingSystem(cache, ws, TextRichStringOperationKinds.Set));
    }

    private static bool Holds(ITsString? current, TextRichStringExpected expected, LcmCache cache) =>
        string.Equals((current?.Text ?? "").Normalize(NormalizationForm.FormC), expected.Text.Normalize(NormalizationForm.FormC),
            StringComparison.Ordinal) && Digest(current, cache) == expected.Runs;

    /// <summary>The runs digest of a stored string; an empty one has no runs, whatever writing system it carries.</summary>
    internal static string Digest(ITsString? value, LcmCache cache) =>
        RichTextRuns.Digest(value is null || value.Length == 0 ? null : value, cache.WritingSystemFactory);

    private static IReadOnlyDictionary<string, string> State(ITsString? value, LcmCache cache) =>
        new SortedDictionary<string, string>(StringComparer.Ordinal) { ["text"] = value?.Text ?? "", ["runs"] = Digest(value, cache) };
}
