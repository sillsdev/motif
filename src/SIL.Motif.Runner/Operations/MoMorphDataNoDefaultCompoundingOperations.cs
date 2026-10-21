using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that sets the <c>NoDefaultCompounding</c> flag inside the <c>&lt;HC&gt;</c> element of the
/// project's <c>ParserParameters</c> XML. The parser's built-in default compounding is on unless that flag is true.
/// </summary>
public static class MoMorphDataNoDefaultCompoundingOperationKinds
{
    public const string Set = "grammar/moMorphData/setNoDefaultCompounding";

    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, MoMorphDataNoDefaultCompoundingHandler.Instance);
        OperationSlotRegistry.RegisterScalar(Set, SnapshotFields.MoMorphDataNoDefaultCompounding);
    }
}

/// <summary>The closed <c>after</c> payload <c>{ "value": true|false }</c> of the set operation.</summary>
public static class MoMorphDataNoDefaultCompoundingPayload
{
    public static bool Parse(JsonElement after)
    {
        var kind = MoMorphDataNoDefaultCompoundingOperationKinds.Set;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["value"], kind);
        return ClosedPayloadParsing.GetRequiredBoolean(after, "value", kind);
    }
}

/// <summary>
/// Reads and edits the one flag in ParserParameters. Every other element, attribute and text in the XML is kept; a
/// missing <c>&lt;HC&gt;</c> element is created and a missing flag is appended to the end of it.
/// </summary>
public static class NoDefaultCompoundingParserParameters
{
    private const string Flag = "NoDefaultCompounding";

    /// <summary>The flag as FieldWorks's loader reads it: absent or unparsable means default compounding stays on.</summary>
    public static bool Read(string? parserParameters)
    {
        if (string.IsNullOrWhiteSpace(parserParameters)) return false;
        var flag = XElement.Parse(parserParameters).Element("HC")?.Element(Flag);
        return flag is not null && bool.TryParse(flag.Value.Trim(), out var value) && value;
    }

    /// <summary>The XML with the flag set to <paramref name="value"/>, unchanged byte for byte when it already is.</summary>
    public static string Write(string? parserParameters, bool value)
    {
        if (string.IsNullOrWhiteSpace(parserParameters))
            return new XElement("ParserParameters", new XElement("HC", new XElement(Flag, Text(value)))).ToString(SaveOptions.DisableFormatting);
        if (Read(parserParameters) == value && XElement.Parse(parserParameters).Element("HC")?.Element(Flag) is not null)
            return parserParameters;

        var root = XElement.Parse(parserParameters, LoadOptions.PreserveWhitespace);
        var hc = root.Element("HC");
        if (hc is null)
        {
            hc = new XElement("HC");
            root.Add(hc);
        }
        var flag = hc.Element(Flag);
        if (flag is null) hc.Add(new XElement(Flag, Text(value)));
        else flag.Value = Text(value);
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static string Text(bool value) => value ? "true" : "false";
}

internal sealed class MoMorphDataNoDefaultCompoundingHandler : IOperationHandler
{
    private const string Kind = MoMorphDataNoDefaultCompoundingOperationKinds.Set;
    internal static readonly MoMorphDataNoDefaultCompoundingHandler Instance = new();
    private MoMorphDataNoDefaultCompoundingHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var after = operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        var desired = MoMorphDataNoDefaultCompoundingPayload.Parse(after);
        var (id, morphData) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        touchedTargets.Add(id);

        var before = BooleanFieldAlternatives.ToAlternatives(NoDefaultCompoundingParserParameters.Read(morphData.ParserParameters));
        morphData.ParserParameters = NoDefaultCompoundingParserParameters.Write(morphData.ParserParameters, desired);
        var afterValue = BooleanFieldAlternatives.ToAlternatives(NoDefaultCompoundingParserParameters.Read(morphData.ParserParameters));
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataNoDefaultCompounding, before, afterValue);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, morphData) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        var current = BooleanFieldAlternatives.ToAlternatives(NoDefaultCompoundingParserParameters.Read(morphData.ParserParameters));
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataNoDefaultCompounding, current, current);
    }
}
