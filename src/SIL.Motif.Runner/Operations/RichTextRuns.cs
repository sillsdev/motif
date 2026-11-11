using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// What the two rich-text kinds (ADR 0095) share: reading and writing runs and their properties in a payload, with
/// liblcm's own load-back rules, and building, writing-system lookup and fingerprinting of a stored string's runs.
/// Every refusal names the kind whose payload is being read.
/// </summary>
internal static class RichTextRuns
{
    private static readonly string[] RunProperties = ["text", "ws", "characterStyle", "ints", "strings", "objData"];

    /// <summary>
    /// Checks that every paragraph and character style an edit names is one of the project's styles of that kind; a
    /// text edit never creates a style.
    /// </summary>
    /// <exception cref="TextStyleUnknownException">A named style is not one of the project's styles of that kind.</exception>
    public static void RequireStyles(LcmCache cache, IEnumerable<string?> paragraphStyles, IEnumerable<TextRun> runs)
    {
        var styles = cache.LangProject.StylesOC;
        bool Has(string name, StyleType type) => styles.Any(style => style.Name == name && style.Type == type);
        foreach (var name in paragraphStyles.OfType<string>().Where(name => name.Length > 0).Distinct(StringComparer.Ordinal))
            if (!Has(name, StyleType.kstParagraph)) throw new TextStyleUnknownException(name, paragraph: true);
        foreach (var name in runs.Select(run => run.CharacterStyle).OfType<string>().Where(name => name.Length > 0)
                     .Distinct(StringComparer.Ordinal))
            if (!Has(name, StyleType.kstCharacter)) throw new TextStyleUnknownException(name, paragraph: false);
    }

    /// <summary>The <paramref name="name"/> array of runs of <paramref name="owner"/>.</summary>
    public static TextRun[] ReadRuns(JsonElement owner, string name, string kind) =>
        Array(owner, name, kind).Select(run => ReadRun(run, kind)).ToArray();

    public static TextRun ReadRun(JsonElement run, string kind)
    {
        ClosedPayloadParsing.RequireObject(run, kind);
        ClosedPayloadParsing.RejectUnknownProperties(run, RunProperties, kind);
        var text = ClosedPayloadParsing.GetRequiredString(run, "text", kind);
        var objData = OptionalString(run, "objData", kind);
        if (text.Contains('￼') && (text.Length != 1 || objData is null))
            throw Invalid(kind, "an embedded object (U+FFFC) must be a run of its own that carries its 'objData'");
        if (objData is not null && TsStringConversion.WhyLiblcmRefuses((int)FwTextPropType.ktptObjData, objData, false) is { } why)
            throw Invalid(kind, $"'objData' cannot be stored: {why}");
        return new TextRun(text, ClosedPayloadParsing.GetRequiredString(run, "ws", kind), OptionalString(run, "characterStyle", kind),
            ReadProps(run, inStyleRules: false, kind), objData);
    }

    /// <summary>The <c>ints</c> and <c>strings</c> of <paramref name="owner"/>, or null when it has neither.</summary>
    public static TextProps? ReadProps(JsonElement owner, bool inStyleRules, string kind)
    {
        var hasInts = owner.TryGetProperty("ints", out var ints);
        var hasStrings = owner.TryGetProperty("strings", out var strings);
        if (!hasInts && !hasStrings) return null;
        var intProps = new Dictionary<int, (int, int)>();
        if (hasInts)
        {
            if (ints.ValueKind != JsonValueKind.Object) throw Invalid(kind, "'ints' must be an object of [variation, value] by property number");
            foreach (var property in ints.EnumerateObject())
            {
                var number = PropertyNumber(property.Name, inStyleRules, isInt: true, kind);
                if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() != 2 ||
                    !property.Value[0].TryGetInt32(out var variation) || !property.Value[1].TryGetInt32(out var value))
                    throw Invalid(kind, $"'ints.{property.Name}' must be [variation, value]");
                if (TsStringConversion.WhyLiblcmRefuses(number, variation, value, inStyleRules) is { } why)
                    throw Invalid(kind, $"'ints.{property.Name}' cannot be stored: {why}");
                intProps[number] = (variation, value);
            }
        }
        var stringProps = new Dictionary<int, string>();
        if (hasStrings)
        {
            if (strings.ValueKind != JsonValueKind.Object) throw Invalid(kind, "'strings' must be an object of text by property number");
            foreach (var property in strings.EnumerateObject())
            {
                var number = PropertyNumber(property.Name, inStyleRules, isInt: false, kind);
                stringProps[number] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()!
                    : throw Invalid(kind, $"'strings.{property.Name}' must be a string");
                if (TsStringConversion.WhyLiblcmRefuses(number, stringProps[number], inStyleRules) is { } why)
                    throw Invalid(kind, $"'strings.{property.Name}' cannot be stored: {why}");
            }
        }
        return new(intProps, stringProps);
    }

    // The closed set of FwTextPropType, less those with a member of their own and those liblcm cannot load back.
    private static int PropertyNumber(string name, bool inStyleRules, bool isInt, string kind)
    {
        if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0 ||
            !Enum.IsDefined((FwTextPropType)number))
            throw Invalid(kind, $"property '{name}' is not a FieldWorks text property number");
        if (isInt && number == (int)FwTextPropType.ktptWs)
            throw Invalid(kind, "the writing system is a run's 'ws', not a property");
        if (!isInt && number == (int)FwTextPropType.ktptObjData)
            throw Invalid(kind, "object data is a run's 'objData', not a property");
        if (!isInt && number == (int)FwTextPropType.ktptNamedStyle)
            throw Invalid(kind, "the named style is 'style' or 'characterStyle', not a property");
        if (isInt && TsStringConversion.IsRefusedByLiblcm(number, inStyleRules))
            throw Invalid(kind, $"integer property {number} is one liblcm cannot load back");
        return number;
    }

    /// <summary>A run as its payload JSON object.</summary>
    public static Dictionary<string, object?> Write(TextRun run)
    {
        var written = new Dictionary<string, object?> { ["text"] = run.Text, ["ws"] = run.Ws };
        if (run.CharacterStyle is not null) written["characterStyle"] = run.CharacterStyle;
        WriteProps(written, run.Props);
        if (run.ObjData is not null) written["objData"] = run.ObjData;
        return written;
    }

    public static void WriteProps(Dictionary<string, object?> into, TextProps? props)
    {
        if (props is null) return;
        if (props.Ints.Count > 0)
            into["ints"] = props.Ints.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                pair => new[] { pair.Value.Variation, pair.Value.Value });
        if (props.Strings.Count > 0)
            into["strings"] = props.Strings.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value);
    }

    public static IEnumerable<JsonElement> Array(JsonElement owner, string name, string kind) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : throw Invalid(kind, $"'{name}' is required and must be an array");

    public static string? OptionalString(JsonElement owner, string name, string kind) =>
        !owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid(kind, $"'{name}' must be a string");

    public static ContractParseException Invalid(string kind, string message) => new($"'{kind}' operation: {message}.");

    /// <summary>The handles of every writing system <paramref name="runs"/> name; one the project lacks is refused.</summary>
    public static Dictionary<string, int> Handles(LcmCache cache, IEnumerable<TextRun> runs, string kind) =>
        runs.Select(run => run.Ws).Distinct(StringComparer.Ordinal)
            .ToDictionary(tag => tag, tag => WritingSystem(cache, tag, kind), StringComparer.Ordinal);

    public static int WritingSystem(LcmCache cache, string tag, string kind)
    {
        var handle = cache.WritingSystemFactory.GetWsFromStr(tag);
        return handle != 0 ? handle : throw new ContractParseException($"'{kind}' operation: writing system '{tag}' is not in the project.");
    }

    /// <summary>
    /// The stored string for <paramref name="runs"/>, text in NFD; with no text it is the empty string of the first
    /// run's writing system, or of <paramref name="emptyWs"/> when there is no run.
    /// </summary>
    public static ITsString Build(IReadOnlyList<TextRun> runs, IReadOnlyDictionary<string, int> handles, int emptyWs)
    {
        var builder = TsStringUtils.MakeStrBldr();
        foreach (var run in runs.Where(run => run.Text.Length > 0))
        {
            var props = TsStringUtils.MakePropsBldr();
            Set(props, run.Props);
            props.SetIntPropValues((int)FwTextPropType.ktptWs, (int)FwTextPropVar.ktpvDefault, handles[run.Ws]);
            props.SetStrPropValue((int)FwTextPropType.ktptNamedStyle, run.CharacterStyle);
            if (run.ObjData is not null) props.SetStrPropValue((int)FwTextPropType.ktptObjData, run.ObjData);
            builder.Replace(builder.Length, builder.Length, run.Text.Normalize(NormalizationForm.FormD), props.GetTextProps());
        }
        return builder.Length > 0 ? builder.GetString() : TsStringUtils.EmptyString(runs.Count > 0 ? handles[runs[0].Ws] : emptyWs);
    }

    public static void Set(ITsPropsBldr builder, TextProps? props)
    {
        if (props is null) return;
        foreach (var (number, (variation, value)) in props.Ints) builder.SetIntPropValues(number, variation, value);
        foreach (var (number, value) in props.Strings) builder.SetStrPropValue(number, value);
    }

    /// <summary>The <see cref="TextRunsDigest"/> of a stored string's runs; null is no runs.</summary>
    public static string Digest(ITsString? contents, ILgWritingSystemFactory factory)
    {
        if (contents is null) return TextRunsDigest.Of([]);
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
}
