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

/// <summary>One run of a changed paragraph: its text, writing system tag and optional character style.</summary>
public sealed record TextRun(string Text, string Ws, string? CharacterStyle = null);

/// <summary>One changed or new paragraph: where it sits in <c>order</c>, its key (null when new), style and runs.</summary>
public sealed record TextParagraphChange(int Index, string? Key, string? Style, IReadOnlyList<TextRun> Runs);

/// <summary>
/// The closed <c>after</c> of a <c>text/setParagraphs</c> operation. <c>order</c> names every paragraph after the
/// edit (null for a new one); <c>expected</c> holds the starting text of every changed or removed paragraph.
/// </summary>
public sealed record TextParagraphsPayload(
    string Field,
    IReadOnlyList<string?> Order,
    IReadOnlyList<TextParagraphChange> Changed,
    IReadOnlyList<string> Removed,
    IReadOnlyDictionary<string, string> Expected)
{
    private const string Kind = TextParagraphsOperationKinds.Set;
    private static readonly string[] Properties = ["field", "order", "changed", "removed", "expected"];
    private static readonly string[] ChangeProperties = ["index", "key", "style", "runs"];
    private static readonly string[] RunProperties = ["text", "ws", "characterStyle"];

    /// <summary>The <c>after</c> JSON this payload is written as.</summary>
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(new
    {
        field = Field,
        order = Order,
        changed = Changed.Select(change => new
        {
            index = change.Index, key = change.Key, style = change.Style,
            runs = change.Runs.Select(run => run.CharacterStyle is null
                ? (object)new { text = run.Text, ws = run.Ws }
                : new { text = run.Text, ws = run.Ws, characterStyle = run.CharacterStyle }),
        }),
        removed = Removed,
        expected = Expected,
    });

    /// <summary>Reads only the field name, for the slot policy.</summary>
    internal static string FieldOf(JsonElement after)
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
            throw Invalid("'expected' must give the starting text of exactly the changed and removed paragraphs, by key");
        return new(field, order, changed, removed, expected);
    }

    private static TextParagraphChange Change(JsonElement item)
    {
        ClosedPayloadParsing.RequireObject(item, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(item, ChangeProperties, Kind);
        var index = ClosedPayloadParsing.GetRequiredInteger(item, "index", Kind);
        var key = item.TryGetProperty("key", out var keyValue) && keyValue.ValueKind != JsonValueKind.Null ? Key(keyValue, "changed[].key") : null;
        var style = OptionalString(item, "style");
        var runs = Array(item, "runs").Select(run =>
        {
            ClosedPayloadParsing.RequireObject(run, Kind);
            ClosedPayloadParsing.RejectUnknownProperties(run, RunProperties, Kind);
            // A run carries no object data yet, so an object replacement character would be written as an empty object.
            if (ClosedPayloadParsing.GetRequiredString(run, "text", Kind).Contains('￼'))
                throw Invalid("a run holds an embedded object (U+FFFC), which a run cannot carry yet");
            return new TextRun(ClosedPayloadParsing.GetRequiredString(run, "text", Kind),
                ClosedPayloadParsing.GetRequiredString(run, "ws", Kind), OptionalString(run, "characterStyle"));
        }).ToArray();
        return new(index, key, style, runs);
    }

    private static Dictionary<string, string> Expectations(JsonElement after)
    {
        if (!after.TryGetProperty("expected", out var value) || value.ValueKind != JsonValueKind.Object)
            throw Invalid("'expected' is required and must be an object of starting text by key");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw Invalid($"'expected.{property.Name}' must be a string");
            expected[Normalize(property.Name)] = property.Value.GetString()!;
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
        for (var index = 0; index < payload.Order.Count; index++)
        {
            IStTxtPara paragraph;
            if (payload.Order[index] is { } key)
                paragraph = existing[key];
            else
            {
                paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create(Minted(operation, $"paragraph/{index}"));
                text.ParagraphsOS.Insert(index, paragraph);
            }
            if (changes.TryGetValue(index, out var change))
                Write(paragraph, change, handles);
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

    // Stale: a paragraph the edit began from changed, appeared, went or moved since.
    private static void CheckUnchanged(OperationEnvelope operation, CanonicalId owner, TextParagraphsPayload payload, IStText? text)
    {
        var live = text?.ParagraphsOS.Cast<IStTxtPara>().ToArray() ?? [];
        var liveKeys = live.Select(Key).ToArray();
        var startKeys = payload.Order.OfType<string>().Concat(payload.Removed).ToHashSet(StringComparer.Ordinal);
        var kept = payload.Order.OfType<string>().ToArray();
        var drifted = liveKeys.Length != startKeys.Count || !liveKeys.All(startKeys.Contains) ||
            !liveKeys.Where(kept.Contains).SequenceEqual(kept) ||
            payload.Expected.Any(expected => !SameText(live.Single(paragraph => Key(paragraph) == expected.Key), expected.Value));
        if (drifted)
            throw new TextFieldStaleException(operation, owner, payload.Field);
    }

    private static bool SameText(IStTxtPara paragraph, string expected) =>
        string.Equals((paragraph.Contents?.Text ?? "").Normalize(NormalizationForm.FormC), expected.Normalize(NormalizationForm.FormC),
            StringComparison.Ordinal);

    private static void Write(IStTxtPara paragraph, TextParagraphChange change, IReadOnlyDictionary<string, int> handles)
    {
        var style = TsStringUtils.MakePropsBldr();
        if (change.Style is { } name)
            style.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, name);
        paragraph.StyleRules = style.GetTextProps();
        var builder = TsStringUtils.MakeIncStrBldr();
        foreach (var run in change.Runs)
        {
            builder.SetIntPropValues((int)FwTextPropType.ktptWs, (int)FwTextPropVar.ktpvDefault, handles[run.Ws]);
            builder.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, run.CharacterStyle);
            builder.Append(run.Text);
        }
        paragraph.Contents = change.Runs.Count > 0
            ? builder.GetString()
            : TsStringUtils.EmptyString(handles.Count > 0 ? handles.Values.First() : paragraph.Cache.DefaultAnalWs);
    }

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

    /// <summary>Each paragraph's text and style by key, and the paragraphs' order.</summary>
    private static IReadOnlyDictionary<string, string> State(IStText? text)
    {
        var state = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (text is null) return state;
        var paragraphs = text.ParagraphsOS.Cast<IStTxtPara>().ToArray();
        foreach (var paragraph in paragraphs)
        {
            state[Key(paragraph)] = paragraph.Contents?.Text ?? "";
            state[Key(paragraph) + "/style"] = paragraph.StyleName ?? "";
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
