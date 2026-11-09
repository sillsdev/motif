using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
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

/// <summary>Names the generic edit of a StText-valued field's paragraphs (ADR 0095).</summary>
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
        OperationHandlerRegistry.Register(Set, TextParagraphsHandler.Instance);
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
        ["RnGenericRec.Description"] = new(owner => owner is IRnGenericRec,
            owner => ((IRnGenericRec)owner).DescriptionOA, (owner, text) => ((IRnGenericRec)owner).DescriptionOA = text),
    };

    /// <summary>Every editable field, as <c>Class.Field</c>.</summary>
    public static IReadOnlyCollection<string> Names => Accessors.Keys.ToArray();

    internal static bool IsAllowed(string field) => Accessors.ContainsKey(field);

    internal static IStText? Read(ICmObject owner, string field) => Accessor(owner, field).Get(owner);

    internal static void Attach(ICmObject owner, string field, IStText text) => Accessor(owner, field).Set(owner, text);

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
    private static readonly string[] RunProperties = ["text", "ws", "characterStyle", "ints", "strings", "objData"];
    private static readonly string[] ExpectedProperties = ["text", "style", "runs"];

    /// <summary>The <c>after</c> JSON this payload is written as.</summary>
    public JsonElement ToJson()
    {
        var changed = Changed.Select(change =>
        {
            var item = new Dictionary<string, object?> { ["index"] = change.Index, ["key"] = change.Key, ["style"] = change.Style };
            item["runs"] = change.Runs.Select(run =>
            {
                var written = new Dictionary<string, object?> { ["text"] = run.Text, ["ws"] = run.Ws };
                if (run.CharacterStyle is not null) written["characterStyle"] = run.CharacterStyle;
                WriteProps(written, run.Props);
                if (run.ObjData is not null) written["objData"] = run.ObjData;
                return written;
            }).ToArray();
            WriteProps(item, change.Props);
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

    private static void WriteProps(Dictionary<string, object?> into, TextProps? props)
    {
        if (props is null) return;
        if (props.Ints.Count > 0)
            into["ints"] = props.Ints.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                pair => new[] { pair.Value.Variation, pair.Value.Value });
        if (props.Strings.Count > 0)
            into["strings"] = props.Strings.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value);
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
        var style = OptionalString(item, "style");
        var runs = Array(item, "runs").Select(Run).ToArray();
        return new(index, key, style, runs, Props(item, inStyleRules: true));
    }

    private static TextRun Run(JsonElement run)
    {
        ClosedPayloadParsing.RequireObject(run, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(run, RunProperties, Kind);
        var text = ClosedPayloadParsing.GetRequiredString(run, "text", Kind);
        var objData = OptionalString(run, "objData");
        if (text.Contains('￼') && (text.Length != 1 || objData is null))
            throw Invalid("an embedded object (U+FFFC) must be a run of its own that carries its 'objData'");
        if (objData is not null && TsStringConversion.WhyLiblcmRefuses((int)FwTextPropType.ktptObjData, objData, false) is { } why)
            throw Invalid($"'objData' cannot be stored: {why}");
        return new TextRun(text, ClosedPayloadParsing.GetRequiredString(run, "ws", Kind), OptionalString(run, "characterStyle"),
            Props(run, inStyleRules: false), objData);
    }

    private static TextProps? Props(JsonElement owner, bool inStyleRules)
    {
        var hasInts = owner.TryGetProperty("ints", out var ints);
        var hasStrings = owner.TryGetProperty("strings", out var strings);
        if (!hasInts && !hasStrings) return null;
        var intProps = new Dictionary<int, (int, int)>();
        if (hasInts)
        {
            if (ints.ValueKind != JsonValueKind.Object) throw Invalid("'ints' must be an object of [variation, value] by property number");
            foreach (var property in ints.EnumerateObject())
            {
                var number = PropertyNumber(property.Name, inStyleRules, isInt: true);
                if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() != 2 ||
                    !property.Value[0].TryGetInt32(out var variation) || !property.Value[1].TryGetInt32(out var value))
                    throw Invalid($"'ints.{property.Name}' must be [variation, value]");
                if (TsStringConversion.WhyLiblcmRefuses(number, variation, value, inStyleRules) is { } why)
                    throw Invalid($"'ints.{property.Name}' cannot be stored: {why}");
                intProps[number] = (variation, value);
            }
        }
        var stringProps = new Dictionary<int, string>();
        if (hasStrings)
        {
            if (strings.ValueKind != JsonValueKind.Object) throw Invalid("'strings' must be an object of text by property number");
            foreach (var property in strings.EnumerateObject())
            {
                var number = PropertyNumber(property.Name, inStyleRules, isInt: false);
                stringProps[number] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()!
                    : throw Invalid($"'strings.{property.Name}' must be a string");
                if (TsStringConversion.WhyLiblcmRefuses(number, stringProps[number], inStyleRules) is { } why)
                    throw Invalid($"'strings.{property.Name}' cannot be stored: {why}");
            }
        }
        return new(intProps, stringProps);
    }

    // Any FieldWorks text property, less those with a member of their own and those liblcm cannot load back.
    private static int PropertyNumber(string name, bool inStyleRules, bool isInt)
    {
        if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw Invalid($"property '{name}' is not a FieldWorks text property number");
        if (isInt && number == (int)FwTextPropType.ktptWs)
            throw Invalid("the writing system is a run's 'ws', not a property");
        if (!isInt && number == (int)FwTextPropType.ktptObjData)
            throw Invalid("object data is a run's 'objData', not a property");
        if (!isInt && number == (int)FwTextPropType.ktptNamedStyle)
            throw Invalid("the named style is 'style' or 'characterStyle', not a property");
        if (isInt && TsStringConversion.IsRefusedByLiblcm(number, inStyleRules))
            throw Invalid($"integer property {number} is one liblcm cannot load back");
        return number;
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

    private static IEnumerable<JsonElement> Array(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : throw Invalid($"'{name}' is required and must be an array");

    private static string Key(JsonElement item, string where) =>
        item.ValueKind == JsonValueKind.String && CanonicalId.TryParse(item.GetString(), out var id)
            ? OperationSlotRegistry.MemberKey(id)
            : throw Invalid($"'{where}' holds a value that is not a paragraph key");

    private static string Normalize(string key) =>
        CanonicalId.TryParse(key, out var id) ? OperationSlotRegistry.MemberKey(id) : throw Invalid($"'expected' key '{key}' is not a paragraph key");

    private static string? OptionalString(JsonElement owner, string name) =>
        !owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid($"'{name}' must be a string");

    private static ContractParseException Invalid(string message) => new($"'{Kind}' operation: {message}.");
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
        var handles = payload.Changed.SelectMany(change => change.Runs).Select(run => run.Ws).Distinct(StringComparer.Ordinal)
            .ToDictionary(tag => tag, tag => WritingSystem(cache, tag), StringComparer.Ordinal);
        touchedTargets.Add(id);

        if (text is null)
        {
            text = cache.ServiceLocator.GetInstance<IStTextFactory>().Create(Minted(operation, "text"));
            TextParagraphFields.Attach(owner, payload.Field, text);
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
    internal static string Digest(IStTxtPara paragraph)
    {
        var contents = paragraph.Contents;
        if (contents is null) return TextRunsDigest.Of([]);
        var factory = paragraph.Cache.WritingSystemFactory;
        return TextRunsDigest.Of(Enumerable.Range(0, contents.RunCount).Select(index =>
        {
            var props = contents.get_Properties(index);
            var ints = new List<KeyValuePair<int, (int, int)>>();
            for (var i = 0; i < props.IntPropCount; i++)
            {
                var value = props.GetIntProp(i, out var tpt, out var variation);
                if (tpt != (int)FwTextPropType.ktptWs) ints.Add(KeyValuePair.Create(tpt, (variation, value)));
            }
            var strings = new List<KeyValuePair<int, string>>();
            string? objData = null;
            for (var i = 0; i < props.StrPropCount; i++)
            {
                var value = props.GetStrProp(i, out var tpt) ?? "";
                if (tpt == (int)FwTextPropType.ktptObjData) objData = value;
                else strings.Add(KeyValuePair.Create(tpt, value));
            }
            var ws = props.GetIntPropValues((int)FwTextPropType.ktptWs, out _);
            return new TextRunsDigest.RunFacts(contents.get_RunText(index) ?? "", ws > 0 ? factory.GetStrFromWs(ws) : "", ints, strings, objData);
        }));
    }

    private static void Write(IStTxtPara paragraph, TextParagraphChange change, IReadOnlyDictionary<string, int> handles, int emptyWs)
    {
        // The paragraph keeps the properties it has (direction, alignment, indents); the edit sets its own over them.
        var rules = paragraph.StyleRules?.GetBldr() ?? TsStringUtils.MakePropsBldr();
        Set(rules, change.Props);
        rules.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, change.Style);
        paragraph.StyleRules = rules.GetTextProps();

        var builder = TsStringUtils.MakeStrBldr();
        foreach (var run in change.Runs.Where(run => run.Text.Length > 0))
        {
            var props = TsStringUtils.MakePropsBldr();
            Set(props, run.Props);
            props.SetIntPropValues((int)FwTextPropType.ktptWs, (int)FwTextPropVar.ktpvDefault, handles[run.Ws]);
            props.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, run.CharacterStyle);
            if (run.ObjData is not null) props.SetStrPropValue((int)FwTextPropType.ktptObjData, run.ObjData);
            builder.Replace(builder.Length, builder.Length, run.Text.Normalize(NormalizationForm.FormD), props.GetTextProps());
        }
        paragraph.Contents = builder.Length > 0
            ? builder.GetString()
            : TsStringUtils.EmptyString(change.Runs.Count > 0 ? handles[change.Runs[0].Ws] : emptyWs);
    }

    private static void Set(ITsPropsBldr builder, TextProps? props)
    {
        if (props is null) return;
        foreach (var (number, (variation, value)) in props.Ints) builder.SetIntPropValues(number, variation, value);
        foreach (var (number, value) in props.Strings) builder.SetStrPropValue(number, value);
    }

    private static int? FirstWs(IStTxtPara paragraph) =>
        paragraph.Contents is { } contents && contents.get_WritingSystemAt(0) is var ws && ws > 0 ? ws : null;

    private static int WritingSystem(LcmCache cache, string tag)
    {
        var handle = cache.WritingSystemFactory.GetWsFromStr(tag);
        return handle != 0 ? handle
            : throw new ContractParseException($"'{TextParagraphsOperationKinds.Set}' operation: writing system '{tag}' is not in the project.");
    }

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


/// <summary>The field no longer holds the text the edit began from (ADR 0095 decision 3).</summary>
internal sealed class TextFieldStaleException(OperationEnvelope operation, CanonicalId owner, string field)
    : ProposalRefusalException(RefusalCodes.TextFieldStale, FailureReason.Refused,
        $"The {field} of {owner.Value} changed since this edit began; nothing was applied. Edit it again from the current text.",
        new Dictionary<string, string>
        {
            ["operationId"] = operation.OperationId.Value, ["owner"] = owner.Value, ["field"] = field,
        });
