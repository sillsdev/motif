using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Application;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Resolution;
using SIL.TextEditing.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the generic edit of a StText-valued field's paragraphs (ADR 0006).</summary>
public static class TextParagraphsOperationKinds
{
    public const string Set = "text/setParagraphs";

    /// <summary>The prefix of the slot and effect field: <c>text/</c> plus the field's <c>Class.Field</c> name.</summary>
    public const string FieldPrefix = "text/";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, TextParagraphsHandler.Instance, TextParagraphFields.Names
            .SelectMany(field => new[]
            {
                FieldWriteCapability.ForAddress(field, RecordWriteVerb.SetRich, FieldWritePayload.Bespoke),
                FieldWriteCapability.ForAddress(field, RecordWriteVerb.ClearRich, FieldWritePayload.Bespoke),
            }).Concat(new[]
            {
                new FieldWriteCapability(CustomFieldOperationKinds.DeclaringClass, TextParagraphFields.CustomType, RecordWriteVerb.SetRich) { Payload = FieldWritePayload.Bespoke },
                new FieldWriteCapability(CustomFieldOperationKinds.DeclaringClass, TextParagraphFields.CustomType, RecordWriteVerb.ClearRich) { Payload = FieldWritePayload.Bespoke },
            }).ToArray());
        OperationSlotRegistry.Register(Set, operation => new OperationSlot(OperationSlotRegistry.Target(operation),
            FieldPrefix + TextParagraphsPayload.FieldOf(OperationSlotRegistry.After(operation))));
    }
#pragma warning restore CA2255
}

/// <summary>
/// The StText-valued fields a person may edit as paragraphs: the inventory's in-scope owning atomic StText fields,
/// less <c>Text.Contents</c>, whose paragraphs carry a text's segments and analyses and are authored whole by
/// <c>texts/langProject/createTexts</c>.
/// </summary>
public static class TextParagraphFields
{
    private sealed record FieldAccess(Func<ICmObject, bool> Owns, Func<ICmObject, IStText?> Get, Action<ICmObject, IStText> Set);

    private static readonly IReadOnlyDictionary<string, FieldAccess> Accessors = new Dictionary<string, FieldAccess>(StringComparer.Ordinal)
    {
        ["CmPossibility.Discussion"] = new(owner => owner is ICmPossibility,
            owner => ((ICmPossibility)owner).DiscussionOA, (owner, text) => ((ICmPossibility)owner).DiscussionOA = text),
        ["LexAppendix.Contents"] = new(owner => owner is ILexAppendix,
            owner => ((ILexAppendix)owner).ContentsOA, (owner, text) => ((ILexAppendix)owner).ContentsOA = text),
        ["LexDb.Introduction"] = new(owner => owner is ILexDb,
            owner => ((ILexDb)owner).IntroductionOA, (owner, text) => ((ILexDb)owner).IntroductionOA = text),
        ["PhPhonContext.Description"] = new(owner => owner is IPhPhonContext,
            owner => ((IPhPhonContext)owner).DescriptionOA, (owner, text) => ((IPhPhonContext)owner).DescriptionOA = text),
        ["RnGenericRec.Conclusions"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).ConclusionsOA, (owner, text) => ((IRnGenericRec)owner).ConclusionsOA = text),
        ["RnGenericRec.Description"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).DescriptionOA, (owner, text) => ((IRnGenericRec)owner).DescriptionOA = text),
        ["RnGenericRec.Discussion"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).DiscussionOA, (owner, text) => ((IRnGenericRec)owner).DiscussionOA = text),
        ["RnGenericRec.ExternalMaterials"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).ExternalMaterialsOA, (owner, text) => ((IRnGenericRec)owner).ExternalMaterialsOA = text),
        ["RnGenericRec.FurtherQuestions"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).FurtherQuestionsOA, (owner, text) => ((IRnGenericRec)owner).FurtherQuestionsOA = text),
        ["RnGenericRec.Hypothesis"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).HypothesisOA, (owner, text) => ((IRnGenericRec)owner).HypothesisOA = text),
        ["RnGenericRec.PersonalNotes"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).PersonalNotesOA, (owner, text) => ((IRnGenericRec)owner).PersonalNotesOA = text),
        ["RnGenericRec.ResearchPlan"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).ResearchPlanOA, (owner, text) => ((IRnGenericRec)owner).ResearchPlanOA = text),
    };

    /// <summary>Every stock editable field, as <c>Class.Field</c>.</summary>
    public static IReadOnlyCollection<string> Names => Accessors.Keys.ToArray();

    /// <summary>The type name a custom owned-StText field is declared under in the catalog.</summary>
    public const string CustomType = "StText";

    /// <summary>The prefix that names a project's custom field: <c>custom:Class.Name</c>, by its LibLCM metadata name.</summary>
    public const string CustomPrefix = "custom:";

    internal static bool IsAllowed(string field) => Accessors.ContainsKey(field) || IsCustom(field);

    private static bool IsCustom(string field) => field.StartsWith(CustomPrefix, StringComparison.Ordinal) &&
        field.IndexOf('.', CustomPrefix.Length) is var dot && dot > CustomPrefix.Length && dot < field.Length - 1;

    internal static IStText? Read(ICmObject owner, string field)
    {
        if (!IsCustom(field))
            return Accessor(owner, field).Get(owner);
        var hvo = CustomText(owner, field, out var flid).get_ObjectProp(owner.Hvo, flid);
        return hvo == 0 ? null : (IStText)owner.Cache.ServiceLocator.GetObject(hvo);
    }

    /// <summary>A new, empty text owned by the field; a custom field's takes a fresh identity, as FieldWorks' own StTextSlice.cs:217 makes it.</summary>
    internal static IStText Create(ICmObject owner, string field, Guid minted)
    {
        if (IsCustom(field))
        {
            var data = CustomText(owner, field, out var flid);
            return (IStText)owner.Cache.ServiceLocator.GetObject(data.MakeNewObject(StTextTags.kClassId, owner.Hvo, flid, -2));
        }
        var text = owner.Cache.ServiceLocator.GetInstance<IStTextFactory>().Create(minted);
        Accessor(owner, field).Set(owner, text);
        return text;
    }

    // AddCustomFieldDlg.cs:395 makes the field; ISilDataAccess edits it by field id.
    private static ISilDataAccessManaged CustomText(ICmObject owner, string field, out int flid)
    {
        var name = field[CustomPrefix.Length..];
        var dot = name.IndexOf('.');
        var (owning, member) = (name[..dot], name[(dot + 1)..]);
        var metadata = (IFwMetaDataCacheManaged)owner.Cache.MetaDataCacheAccessor;
        flid = 0;
        if (!metadata.FieldExists(owner.ClassID, member, true) ||
            metadata.GetOwnClsName(metadata.GetFieldId2(owner.ClassID, member, true)) != owning)
            throw new CustomFieldRefusal(RefusalCodes.CustomFieldMissing, TextParagraphsOperationKinds.Set, member,
                $"{owner.ClassName} has no field '{member}' declared on {owning}.");
        flid = metadata.GetFieldId2(owner.ClassID, member, true);
        if (!metadata.IsCustom(flid))
            throw new CustomFieldRefusal(RefusalCodes.CustomFieldNotCustom, TextParagraphsOperationKinds.Set, member,
                $"'{member}' on {owner.ClassName} is a stock field, which its own kinds write.");
        if ((CellarPropertyType)metadata.GetFieldType(flid) != CellarPropertyType.OwningAtomic || metadata.GetDstClsName(flid) != "StText")
            throw new CustomFieldRefusal(RefusalCodes.CustomFieldWrongType, TextParagraphsOperationKinds.Set, member,
                $"Custom field '{member}' is not an owned StText, which this kind writes as paragraphs.");
        return (ISilDataAccessManaged)owner.Cache.DomainDataByFlid;
    }

    private static FieldAccess Accessor(ICmObject owner, string field)
    {
        var accessor = Accessors[field];
        if (!accessor.Owns(owner))
            throw new ContractParseException(
                $"'{TextParagraphsOperationKinds.Set}' target '{CanonicalId.FromGuid(owner.Guid).Value}' has no field '{field}'.");
        return accessor;
    }
}

/// <summary>
/// Text properties by FieldWorks property number, as a run's or paragraph's property bag holds them. The writing
/// system, object data and named style are not here: each has its own member.
/// </summary>
public sealed record TextProps(
    IReadOnlyDictionary<int, (int Variation, int Value)> Ints, IReadOnlyDictionary<int, string> Strings)
{
    /// <summary>No properties.</summary>
    public static readonly TextProps None = new(new Dictionary<int, (int, int)>(), new Dictionary<int, string>());
}

/// <summary>
/// One run of a changed paragraph: its text, writing system tag, optional character style, its other properties and
/// its object data (a link's target or an embedded object), so an edited paragraph keeps every run property.
/// </summary>
public sealed record TextRun(string Text, string Ws, string? CharacterStyle = null, TextProps? Props = null, string? ObjData = null);

/// <summary>
/// One changed or new paragraph: where it sits in <c>order</c>, its key (null when new), its paragraph style, its runs
/// and the paragraph properties to set over the ones it already has.
/// </summary>
public sealed record TextParagraphChange(int Index, string? Key, string? Style, IReadOnlyList<TextRun> Runs, TextProps? Props = null);

/// <summary>What a changed or removed paragraph held when the edit began: its text, paragraph style and <see cref="TextRunsDigest"/>.</summary>
public sealed record TextParagraphExpected(string Text, string Style, string Runs);

/// <summary>
/// The closed <c>after</c> of a <c>text/setParagraphs</c> operation. <c>order</c> names every paragraph after the
/// edit (null for a new one); <c>expected</c> holds what every changed or removed paragraph held when the edit began.
/// </summary>
public sealed record TextParagraphsPayload(
    string Field,
    IReadOnlyList<string?> Order,
    IReadOnlyList<TextParagraphChange> Changed,
    IReadOnlyList<string> Removed,
    IReadOnlyDictionary<string, TextParagraphExpected> Expected)
{
    private const string Kind = TextParagraphsOperationKinds.Set;
    private static readonly string[] Properties = ["field", "order", "changed", "removed", "expected"];
    private static readonly string[] ChangeProperties = ["index", "key", "style", "runs", "ints", "strings"];
    private static readonly string[] ExpectedProperties = ["text", "style", "runs"];

    /// <summary>The <c>after</c> JSON this payload is written as.</summary>
    public JsonElement ToJson()
    {
        var changed = Changed.Select(change =>
        {
            var item = new Dictionary<string, object?> { ["index"] = change.Index, ["key"] = change.Key, ["style"] = change.Style };
            item["runs"] = change.Runs.Select(RichTextRuns.Write).ToArray();
            RichTextRuns.WriteProps(item, change.Props);
            return item;
        }).ToArray();
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["field"] = Field,
            ["order"] = Order,
            ["changed"] = changed,
            ["removed"] = Removed,
            ["expected"] = Expected.ToDictionary(pair => pair.Key,
                pair => new Dictionary<string, string> { ["text"] = pair.Value.Text, ["style"] = pair.Value.Style, ["runs"] = pair.Value.Runs }),
        });
    }

    /// <summary>Reads only the field name, for the slot policy.</summary>
    public static string FieldOf(JsonElement after)
    {
        ClosedPayloadParsing.RequireObject(after, Kind);
        return ClosedPayloadParsing.GetRequiredString(after, "field", Kind);
    }

    /// <summary>Reads and checks a closed payload; a key may appear once, and every new paragraph is a change.</summary>
    public static TextParagraphsPayload Parse(JsonElement after)
    {
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, Properties, Kind);
        var field = ClosedPayloadParsing.GetRequiredString(after, "field", Kind);
        if (!TextParagraphFields.IsAllowed(field))
            throw Invalid($"field '{field}' is not an editable StText field; use one of {string.Join(", ", TextParagraphFields.Names)}");
        var order = Array(after, "order").Select(item => item.ValueKind == JsonValueKind.Null ? null : Key(item, "order")).ToArray();
        var removed = Array(after, "removed").Select(item => Key(item, "removed")).ToArray();
        var changed = Array(after, "changed").Select(Change).ToArray();
        var expected = Expectations(after);

        // A field the edit leaves with no paragraph would be an StText with none, which FieldWorks does not make.
        if (order.Length == 0)
            throw Invalid("'order' must name at least one paragraph; to empty a field, keep one empty paragraph");
        var keys = order.OfType<string>().Concat(removed).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw Invalid("a paragraph key appears more than once in 'order' and 'removed'");
        if (changed.Select(change => change.Index).Distinct().Count() != changed.Length)
            throw Invalid("two 'changed' entries name the same index");
        foreach (var change in changed)
            if (change.Index < 0 || change.Index >= order.Length || order[change.Index] != change.Key)
                throw Invalid($"'changed' index {change.Index} does not name key '{change.Key ?? "null"}' in 'order'");
        if (order.Select((key, index) => (key, index)).Any(item => item.key is null && changed.All(change => change.Index != item.index)))
            throw Invalid("every new paragraph in 'order' needs a 'changed' entry");
        var mustExpect = changed.Select(change => change.Key).OfType<string>().Concat(removed);
        if (mustExpect.Any(key => !expected.ContainsKey(key)) || expected.Keys.Any(key => !keys.Contains(key, StringComparer.Ordinal)))
            throw Invalid("'expected' must give what exactly the changed and removed paragraphs held, by key");
        return new(field, order, changed, removed, expected);
    }

    private static TextParagraphChange Change(JsonElement item)
    {
        ClosedPayloadParsing.RequireObject(item, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(item, ChangeProperties, Kind);
        var index = ClosedPayloadParsing.GetRequiredInteger(item, "index", Kind);
        var key = item.TryGetProperty("key", out var keyValue) && keyValue.ValueKind != JsonValueKind.Null ? Key(keyValue, "changed[].key") : null;
        var style = RichTextRuns.OptionalString(item, "style", Kind);
        var runs = RichTextRuns.ReadRuns(item, "runs", Kind);
        return new(index, key, style, runs, RichTextRuns.ReadProps(item, inStyleRules: true, Kind));
    }

    private static Dictionary<string, TextParagraphExpected> Expectations(JsonElement after)
    {
        if (!after.TryGetProperty("expected", out var value) || value.ValueKind != JsonValueKind.Object)
            throw Invalid("'expected' is required and must be an object of starting paragraphs by key");
        var expected = new Dictionary<string, TextParagraphExpected>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            ClosedPayloadParsing.RequireObject(property.Value, Kind);
            ClosedPayloadParsing.RejectUnknownProperties(property.Value, ExpectedProperties, Kind);
            expected[Normalize(property.Name)] = new(
                ClosedPayloadParsing.GetRequiredString(property.Value, "text", Kind),
                ClosedPayloadParsing.GetRequiredString(property.Value, "style", Kind),
                ClosedPayloadParsing.GetRequiredString(property.Value, "runs", Kind));
        }
        return expected;
    }

    private static IEnumerable<JsonElement> Array(JsonElement owner, string name) => RichTextRuns.Array(owner, name, Kind);

    private static string Key(JsonElement item, string where) =>
        item.ValueKind == JsonValueKind.String && CanonicalId.TryParse(item.GetString(), out var id)
            ? OperationSlotRegistry.MemberKey(id)
            : throw Invalid($"'{where}' holds a value that is not a paragraph key");

    private static string Normalize(string key) =>
        CanonicalId.TryParse(key, out var id) ? OperationSlotRegistry.MemberKey(id) : throw Invalid($"'expected' key '{key}' is not a paragraph key");

    private static ContractParseException Invalid(string message) => RichTextRuns.Invalid(Kind, message);
}

internal sealed class TextParagraphsHandler : IOperationHandler
{
    internal static readonly TextParagraphsHandler Instance = new();

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = TextParagraphsPayload.Parse(OperationSlotRegistry.After(operation));
        var (id, owner) = TargetResolution.Resolve<ICmObject>(cache, operation, TextParagraphsOperationKinds.Set);
        var text = TextParagraphFields.Read(owner, payload.Field);
        var before = State(text);
        CheckUnchanged(operation, id, payload, text);
        var handles = RichTextRuns.Handles(cache, payload.Changed.SelectMany(change => change.Runs), TextParagraphsOperationKinds.Set);
        touchedTargets.Add(id);

        if (text is null)
        {
            text = TextParagraphFields.Create(owner, payload.Field, Minted(operation, "text"));
        }
        var existing = text.ParagraphsOS.Cast<IStTxtPara>().ToDictionary(Key, StringComparer.Ordinal);
        foreach (var key in payload.Removed)
            text.ParagraphsOS.Remove(existing[key]);
        var changes = payload.Changed.ToDictionary(change => change.Index);
        var lastWs = cache.DefaultAnalWs;
        for (var index = 0; index < payload.Order.Count; index++)
        {
            IStTxtPara paragraph;
            var emptyWs = lastWs;
            if (payload.Order[index] is { } key)
            {
                paragraph = existing[key];
                emptyWs = FirstWs(paragraph) ?? lastWs;
            }
            else
            {
                paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create(Minted(operation, $"paragraph/{index}"));
                text.ParagraphsOS.Insert(index, paragraph);
            }
            if (changes.TryGetValue(index, out var change))
                Write(paragraph, change, handles, emptyWs);
            lastWs = FirstWs(paragraph) ?? lastWs;
        }

        return new(id, TextParagraphsOperationKinds.FieldPrefix + payload.Field, before, State(text));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var field = TextParagraphsPayload.FieldOf(OperationSlotRegistry.After(operation));
        var (id, owner) = TargetResolution.Resolve<ICmObject>(cache, operation, TextParagraphsOperationKinds.Set);
        var current = State(TextParagraphFields.Read(owner, field));
        return new(id, TextParagraphsOperationKinds.FieldPrefix + field, current, current);
    }

    // Stale: a starting paragraph changed its text, style or runs, or appeared, went or moved.
    private static void CheckUnchanged(OperationEnvelope operation, CanonicalId owner, TextParagraphsPayload payload, IStText? text)
    {
        var live = text?.ParagraphsOS.Cast<IStTxtPara>().ToArray() ?? [];
        var liveKeys = live.Select(Key).ToArray();
        var startKeys = payload.Order.OfType<string>().Concat(payload.Removed).ToHashSet(StringComparer.Ordinal);
        var kept = payload.Order.OfType<string>().ToArray();
        var drifted = liveKeys.Length != startKeys.Count || !liveKeys.All(startKeys.Contains) ||
            !liveKeys.Where(kept.Contains).SequenceEqual(kept) ||
            payload.Expected.Any(expected => !Holds(live.Single(paragraph => Key(paragraph) == expected.Key), expected.Value));
        if (drifted)
            throw new TextFieldStaleException(operation, owner, payload.Field);
    }

    private static bool Holds(IStTxtPara paragraph, TextParagraphExpected expected) =>
        string.Equals((paragraph.Contents?.Text ?? "").Normalize(NormalizationForm.FormC), expected.Text.Normalize(NormalizationForm.FormC),
            StringComparison.Ordinal) &&
        (paragraph.StyleName ?? "") == expected.Style &&
        Digest(paragraph) == expected.Runs;

    /// <summary>The <see cref="TextRunsDigest"/> of a stored paragraph's runs.</summary>
    internal static string Digest(IStTxtPara paragraph) => RichTextRuns.Digest(paragraph.Contents, paragraph.Cache.WritingSystemFactory);

    private static void Write(IStTxtPara paragraph, TextParagraphChange change, IReadOnlyDictionary<string, int> handles, int emptyWs)
    {
        // The paragraph keeps the properties it has (direction, alignment, indents); the edit sets its own over them.
        var rules = paragraph.StyleRules?.GetBldr() ?? TsStringUtils.MakePropsBldr();
        RichTextRuns.Set(rules, change.Props);
        rules.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, change.Style);
        paragraph.StyleRules = rules.GetTextProps();

        paragraph.Contents = RichTextRuns.Build(change.Runs, handles, emptyWs);
    }

    private static int? FirstWs(IStTxtPara paragraph) =>
        paragraph.Contents is { } contents && contents.get_WritingSystemAt(0) is var ws && ws > 0 ? ws : null;

    /// <summary>A new object's identity, the same on every run, so Dry Run and Apply capture one effect.</summary>
    private static Guid Minted(OperationEnvelope operation, string role)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{operation.OperationId.Value}/{role}")).AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string Key(IStTxtPara paragraph) => CanonicalId.FromGuid(paragraph.Guid).Value;

    /// <summary>Each paragraph's text, style and run fingerprint by key, and the paragraphs' order.</summary>
    private static IReadOnlyDictionary<string, string> State(IStText? text)
    {
        var state = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (text is null) return state;
        var paragraphs = text.ParagraphsOS.Cast<IStTxtPara>().ToArray();
        foreach (var paragraph in paragraphs)
        {
            state[Key(paragraph)] = paragraph.Contents?.Text ?? "";
            state[Key(paragraph) + "/style"] = paragraph.StyleName ?? "";
            state[Key(paragraph) + "/runs"] = Digest(paragraph);
        }
        state["$order"] = string.Join(" ", paragraphs.Select(Key));
        return state;
    }
}

/// <summary>A rich-text edit names a style the project lacks; creating styles is not part of a text edit.</summary>
internal sealed class TextStyleUnknownException(string style, bool paragraph)
    : ProposalRefusalException(RefusalCodes.TextStyleUnknown, FailureReason.InvalidArgument,
        $"The project has no {(paragraph ? "paragraph" : "character")} style named '{style}', so this edit was not added. " +
        "Choose a style the project has, or add this one in FieldWorks first.",
        new Dictionary<string, string> { ["style"] = style, ["styleKind"] = paragraph ? "paragraph" : "character" });

/// <summary>
/// The field, or its alternative in <paramref name="ws"/>, no longer holds the text the edit began from (ADR 0006
/// decision 3); the facts name the owner, the field and, for an alternative, its writing system.
/// </summary>

internal sealed class TextFieldStaleException(OperationEnvelope operation, CanonicalId owner, string field, string? ws = null)
    : ProposalRefusalException(RefusalCodes.TextFieldStale, FailureReason.Refused,
        $"The {field}{(ws is null ? "" : $" ({ws})")} of {owner.Value} changed since this edit began; nothing was applied. " +
        "Edit it again from the current text.",
        StaleFacts(operation, owner, field, ws))
{
    private static Dictionary<string, string> StaleFacts(OperationEnvelope operation, CanonicalId owner, string field, string? ws)
    {
        var facts = new Dictionary<string, string> { ["operationId"] = operation.OperationId.Value, ["owner"] = owner.Value, ["field"] = field };
        if (ws is not null) facts["ws"] = ws;
        return facts;
    }
}
