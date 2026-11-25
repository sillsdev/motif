using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Resolution;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that sets how many times one affix rule may apply to a word, stored as
/// <c>HC/AffixRules/AffixRule[@guid][@maxApps]</c> in the project's <c>ParserParameters</c> XML, keyed by the affix MSA guid.
/// The parser applies an affix rule once unless the project says otherwise.
/// </summary>
public static class MoMorphDataAffixMaxAppsOperationKinds
{
    public const string Set = "grammar/moMorphData/setAffixMaxApps";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, MoMorphDataAffixMaxAppsHandler.Instance,
            new FieldWriteCapability("MoMorphData", "AffixMaxApps", RecordWriteVerb.Set) { Payload = FieldWritePayload.Generic });
        OperationSlotRegistry.Register(Set, operation => new OperationSlot(
            OperationSlotRegistry.Target(operation), SnapshotFields.MoMorphDataAffixMaxApps,
            OperationSlotRegistry.MemberKey(ClosedPayloadParsing.GetRequiredCanonicalId(
                OperationSlotRegistry.After(operation), "msa", Set))));
    }
#pragma warning restore CA2255
}

/// <summary>The closed <c>after</c> payload <c>{ "msa": id, "maxApps": n }</c> of the set operation.</summary>
public sealed record MoMorphDataAffixMaxAppsPayload(CanonicalId Msa, int MaxApps)
{
    public static MoMorphDataAffixMaxAppsPayload Parse(JsonElement after)
    {
        var kind = MoMorphDataAffixMaxAppsOperationKinds.Set;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["msa", "maxApps"], kind);
        return new(ClosedPayloadParsing.GetRequiredCanonicalId(after, "msa", kind),
            ClosedPayloadParsing.GetRequiredInteger(after, "maxApps", kind));
    }
}

/// <summary>A refusal of an affix repeat count, carrying a registered code.</summary>
public sealed class AffixMaxAppsRefusal : ProposalRefusalException
{
    internal AffixMaxAppsRefusal(string code, string message)
        : base(code, FailureReason.Refused, message, new Dictionary<string, string>()) { }
}

/// <summary>Validates a repeat count and the MSA it is for.</summary>
public static class AffixMaxApps
{
    /// <summary>The largest repeat count Motif authors; each extra application multiplies the parser's work on that rule.</summary>
    public const int Cap = 3;

    /// <summary>The count of an affix rule the project does not mention: it applies once.</summary>
    public const int Default = 1;

    /// <summary>Throws for a count below 1 or above the cap.</summary>
    public static void RequireCount(int maxApps)
    {
        if (maxApps < Default || maxApps > Cap)
            throw new AffixMaxAppsRefusal(RefusalCodes.AffixMaxAppsInvalidCount,
                $"maxApps must be a whole number from 1 to {Cap}, not {maxApps.ToString(CultureInfo.InvariantCulture)}. 1 is the default and removes the setting.");
    }

    /// <summary>The affix MSA an id names; throws when it names anything else.</summary>
    public static ICmObject ResolveAffixMsa(LcmCache cache, CanonicalId id)
    {
        ICmObject? resolved;
        try { resolved = CanonicalIdResolver.Resolve(cache, id); }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new AffixMaxAppsRefusal(RefusalCodes.AffixMaxAppsNotAffixMsa,
                $"'{id.Value}' is not an object in this project; give the id of an affix's morphosyntactic analysis.");
        }
        return resolved switch
        {
            IMoInflAffMsa or IMoDerivAffMsa or IMoUnclassifiedAffixMsa => resolved,
            _ => throw new AffixMaxAppsRefusal(RefusalCodes.AffixMaxAppsNotAffixMsa,
                $"'{id.Value}' is a {resolved?.ClassName ?? "missing object"}, not an affix's morphosyntactic analysis (inflectional, derivational or unclassified affix)."),
        };
    }

    /// <summary>
    /// The analysis FieldWorks builds the affix rule from when <paramref name="msa"/> is one of several equal analyses on
    /// its entry, and every other analysis equal to it. The choice is HCLoader's <c>RepresentativeMSA</c>: the first analysis of
    /// the entry with the same class that <c>EqualsMsa</c> and that a sense uses, else the analysis itself; with <c>HC/MergeMSAs</c> false it is the analysis itself with no equals. A count keyed on
    /// any other analysis is dropped by the loaders.
    /// </summary>
    public static (ICmObject Representative, IReadOnlyList<ICmObject> Equal) Representative(ICmObject msa)
    {
        if (msa is not IMoMorphSynAnalysis analysis || analysis.OwnerOfClass<ILexEntry>() is not { } entry
            || !MergesAnalyses(msa.Cache))
            return (msa, []);
        IMoMorphSynAnalysis representative = analysis;
        var equal = new List<IMoMorphSynAnalysis>();
        foreach (var other in entry.MorphoSyntaxAnalysesOC)
        {
            if (other.ClassID != analysis.ClassID || !other.EqualsMsa(analysis)) continue;
            equal.Add(other);
        }
        foreach (var other in entry.MorphoSyntaxAnalysesOC)
            if (other.ClassID == analysis.ClassID && other.EqualsMsa(analysis) && entry.SenseWithMsa(other) is not null)
            {
                representative = other;
                break;
            }
        return (representative, equal.Where(other => other.Guid != representative.Guid).ToArray());
    }

    /// <summary>
    /// Whether the parser merges equal analyses: <c>HC/MergeMSAs</c> in ParserParameters, true when absent (HCParser's default).
    /// Unreadable settings count as the default.
    /// </summary>
    public static bool MergesAnalyses(LcmCache cache)
    {
        var text = cache.LangProject.MorphologicalDataOA?.ParserParameters;
        if (string.IsNullOrWhiteSpace(text)) return true;
        try
        {
            var value = XElement.Parse(text).Elements("HC").Elements("MergeMSAs").FirstOrDefault();
            return value is null || (bool)value;
        }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException)
        {
            return true;
        }
    }

    /// <summary>The count the project holds for the affix MSA <paramref name="msa"/>, or <see cref="Default"/> when none is set.</summary>
    public static int Read(string? parserParameters, Guid msa) =>
        AffixMaxAppsParserParameters.Read(parserParameters, msa);

    /// <summary>The count on an affix MSA of the project, <see cref="Default"/> when none is set; for the read model.</summary>
    public static int ReadFor(ICmObject msa) =>
        msa is IMoInflAffMsa or IMoDerivAffMsa or IMoUnclassifiedAffixMsa
            && msa.Cache.LangProject.MorphologicalDataOA is { } data
            ? TryRead(data.ParserParameters, Representative(msa).Representative.Guid) : Default;

    private static int TryRead(string? parserParameters, Guid msa)
    {
        try { return AffixMaxAppsParserParameters.Read(parserParameters, msa); }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException or OverflowException)
        {
            return Default;
        }
    }
}

/// <summary>
/// Reads and edits one affix count in ParserParameters. Every other element, attribute and text in the XML is kept
/// as XML (the text is re-serialised, so empty elements may gain a space and a declaration is dropped); a count of 1 removes its element and an empty <c>AffixRules</c> element is removed with it.
/// </summary>
public static class AffixMaxAppsParserParameters
{
    private const string Group = "AffixRules";
    private const string Item = "AffixRule";

    /// <summary>The count for <paramref name="msa"/>: <c>1</c> when absent. A count that is not a whole number throws <see cref="FormatException"/>.</summary>
    public static int Read(string? parserParameters, Guid msa)
    {
        if (string.IsNullOrWhiteSpace(parserParameters)) return AffixMaxApps.Default;
        var element = Find(XElement.Parse(parserParameters).Element("HC"), msa);
        if (element is null) return AffixMaxApps.Default;
        var text = element.Attribute("maxApps")?.Value;
        if (text is null || !int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            throw new FormatException($"AffixRule {msa} has maxApps '{text}', which is not a whole number.");
        return count;
    }

    /// <summary>Throws when the XML's root is not <c>ParserParameters</c>, which FieldWorks never writes.</summary>
    public static void RequireRoot(string? parserParameters)
    {
        if (string.IsNullOrWhiteSpace(parserParameters)) return;
        var name = XElement.Parse(parserParameters).Name.LocalName;
        if (name != "ParserParameters")
            throw new InvalidOperationException($"The project's parser settings start with <{name}>, not <ParserParameters>; fix it in FieldWorks first.");
    }

    /// <summary>The XML with the count for <paramref name="msa"/> set, returned as given when it already is.</summary>
    public static string Write(string? parserParameters, Guid msa, int maxApps)
    {
        RequireRoot(parserParameters);
        if (Read(parserParameters, msa) == maxApps && (maxApps == AffixMaxApps.Default ||
            Find(XElement.Parse(parserParameters!).Element("HC"), msa) is not null))
            return parserParameters ?? "";

        var root = string.IsNullOrWhiteSpace(parserParameters)
            ? new XElement("ParserParameters")
            : XElement.Parse(parserParameters, LoadOptions.PreserveWhitespace);
        var hc = root.Element("HC");
        var existing = Find(hc, msa);
        if (maxApps == AffixMaxApps.Default)
        {
            if (existing is null) return parserParameters ?? "";
            var group = existing.Parent!;
            Remove(existing);
            if (!group.Elements().Any()) Remove(group);
        }
        else if (existing is not null)
            existing.SetAttributeValue("maxApps", maxApps.ToString(CultureInfo.InvariantCulture));
        else
        {
            if (hc is null) root.Add(hc = new XElement("HC"));
            var group = hc.Element(Group);
            if (group is null) hc.Add(group = new XElement(Group));
            group.Add(new XElement(Item,
                new XAttribute("guid", msa.ToString()),
                new XAttribute("maxApps", maxApps.ToString(CultureInfo.InvariantCulture))));
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement? Find(XElement? hc, Guid msa) =>
        hc?.Element(Group)?.Elements(Item).FirstOrDefault(item =>
            Guid.TryParse(item.Attribute("guid")?.Value, out var guid) && guid == msa);

    private static void Remove(XElement element)
    {
        if (element.PreviousNode is XText { Value: var text } previous && string.IsNullOrWhiteSpace(text)) previous.Remove();
        element.Remove();
    }
}

internal sealed class MoMorphDataAffixMaxAppsHandler : IOperationHandler
{
    private const string Kind = MoMorphDataAffixMaxAppsOperationKinds.Set;
    internal static readonly MoMorphDataAffixMaxAppsHandler Instance = new();
    private MoMorphDataAffixMaxAppsHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var after = operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        var payload = MoMorphDataAffixMaxAppsPayload.Parse(after);
        AffixMaxApps.RequireCount(payload.MaxApps);
        var msa = AffixMaxApps.ResolveAffixMsa(cache, payload.Msa);
        var (id, morphData) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        touchedTargets.Add(id);

        var before = Alternatives(AffixMaxAppsParserParameters.Read(morphData.ParserParameters, msa.Guid));
        morphData.ParserParameters = AffixMaxAppsParserParameters.Write(morphData.ParserParameters, msa.Guid, payload.MaxApps);
        var afterValue = Alternatives(AffixMaxAppsParserParameters.Read(morphData.ParserParameters, msa.Guid));
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataAffixMaxApps, before, afterValue);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var payload = MoMorphDataAffixMaxAppsPayload.Parse(OperationSlotRegistry.After(operation));
        var msa = AffixMaxApps.ResolveAffixMsa(cache, payload.Msa);
        var (id, morphData) = TargetResolution.Resolve<IMoMorphData>(cache, operation, Kind);
        var current = Alternatives(AffixMaxAppsParserParameters.Read(morphData.ParserParameters, msa.Guid));
        return new ExpectedEffect(id, SnapshotFields.MoMorphDataAffixMaxApps, current, current);
    }

    private static IReadOnlyDictionary<string, string> Alternatives(int count) =>
        new Dictionary<string, string> { ["maxApps"] = count.ToString(CultureInfo.InvariantCulture) };
}
