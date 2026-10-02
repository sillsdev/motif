using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using SIL.LCModel;
using SIL.LCModel.Core.Text;

namespace SIL.Motif.Host.PanGloss;

/// <summary>A stable FieldWorks link destination whose displayed URL can use the request's project name.</summary>
public sealed record FieldWorksLinkTarget(string Tool, Guid ObjectId);

/// <summary>
/// Builds <c>silfw:</c> links that open FieldWorks on one object of a named project, in the tool that
/// lists it — the same links FieldWorks writes for itself, so the installed FieldWorks follows them.
/// </summary>
public static class FieldWorksLinks
{
    /// <summary>
    /// A link opening <paramref name="found"/> at its nearest owner that a FieldWorks tool lists — a sense
    /// opens its entry, a slot its category — or <see langword="null"/> when no tool lists any of them.
    /// </summary>
    public static string? For(LcmCache cache, string projectName, ICmObject found)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(found);
        return ForTarget(projectName, TargetFor(cache, found));
    }

    /// <summary>
    /// A link selecting the wordform <paramref name="word"/> in FieldWorks' Word Analyses, where Parser ▸
    /// Try a Word opens with that word already entered; <see langword="null"/> when the project has no
    /// unambiguous wordform in any populated writing system.
    /// </summary>
    public static string? ForWordform(LcmCache cache, string projectName, string word)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(word);
        return ForTarget(projectName, WordformTargetFor(cache, word));
    }

    /// <summary>Finds the stable destination FieldWorks would open for one project object.</summary>
    public static FieldWorksLinkTarget? TargetFor(LcmCache cache, ICmObject found)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(found);
        return Target(cache, found) is { } target ? new FieldWorksLinkTarget(target.Tool, target.Guid) : null;
    }

    /// <summary>Finds a stable destination only when the exact form identifies one captured wordform.</summary>
    public static FieldWorksLinkTarget? WordformTargetFor(LcmCache cache, string word)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(word);
        var repository = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
        var form = word.Normalize(NormalizationForm.FormD);
        var matches = repository.AllInstances().Where(wordform => wordform.Form.AvailableWritingSystemIds.Any(ws =>
            StringComparer.Ordinal.Equals(wordform.Form.get_String(ws)?.Text?.Normalize(NormalizationForm.FormD), form)))
            .Take(2).ToArray();
        return matches.Length == 1 ? new FieldWorksLinkTarget("Analyses", matches[0].Guid) : null;
    }

    /// <summary>
    /// The environments <paramref name="allomorph"/> is written to occur in, each opening in FieldWorks'
    /// Environments: its phonological environments first, then an infix's position environments, each once.
    /// </summary>
    public static IReadOnlyList<FieldWorksLinkTarget> EnvironmentTargetsFor(IMoForm allomorph)
    {
        ArgumentNullException.ThrowIfNull(allomorph);
        IEnumerable<IPhEnvironment> environments = allomorph switch
        {
            IMoStemAllomorph stem => stem.PhoneEnvRC,
            IMoAffixAllomorph affix => affix.PhoneEnvRC.Concat(affix.PositionRS),
            _ => [],
        };
        return environments.Distinct()
            .Select(environment => new FieldWorksLinkTarget(EnvironmentTool, environment.Guid))
            .ToArray();
    }

    /// <summary>
    /// The natural class an environment names as <c>[abbreviation]</c>, opening in FieldWorks' Natural
    /// Classes; <see langword="null"/> when no natural class carries that abbreviation in any writing system.
    /// </summary>
    public static FieldWorksLinkTarget? NaturalClassTargetFor(LcmCache cache, string abbreviation)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(abbreviation);
        var naturalClass = cache.LangProject.PhonologicalDataOA?.NaturalClassesOS.FirstOrDefault(candidate =>
            candidate.Abbreviation.AvailableWritingSystemIds.Any(ws =>
                candidate.Abbreviation.get_String(ws).Text == abbreviation));
        return naturalClass is null ? null : new FieldWorksLinkTarget(NaturalClassTool, naturalClass.Guid);
    }

    /// <summary>
    /// The name FieldWorks shows for <paramref name="tool"/>, such as Lexicon Edit for <c>lexiconEdit</c>, so a
    /// link can say where it lands; "FieldWorks" for a tool Motif does not know.
    /// </summary>
    public static string ToolName(string? tool) =>
        tool is not null && ToolNames.TryGetValue(tool, out var name) ? name : "FieldWorks";

    /// <summary>
    /// The FieldWorks tool a <c>silfw:</c> link opens, read from the link itself, whether its query is encoded
    /// whole, as Motif writes it, or field by field; <see langword="null"/> when the link names no tool.
    /// </summary>
    public static string? ToolOf(string? link)
    {
        if (link is null || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Query.Length < 2)
            return null;
        return HttpUtility.UrlDecode(uri.Query[1..]).Split('&')
            .Select(pair => pair.Split('=', 2))
            .FirstOrDefault(pair => pair.Length == 2 && pair[0] == "tool" && pair[1].Length > 0)?[1];
    }

    /// <summary>The name FieldWorks shows for the tool <paramref name="link"/> opens, as <see cref="ToolName"/>.</summary>
    public static string ToolNameOf(string? link) => ToolName(ToolOf(link));

    /// <summary>Builds a request-specific link from a stable FieldWorks destination.</summary>
    public static string? ForTarget(string projectName, FieldWorksLinkTarget? target)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        return target is null ? null : Build(projectName, target.Tool, target.ObjectId);
    }

    private const string EnvironmentTool = "EnvironmentEdit";
    private const string NaturalClassTool = "naturalClassedit";

    // FieldWorks' own tool labels, from each tool's toolConfiguration.xml entry.
    private static readonly Dictionary<string, string> ToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lexiconEdit"] = "Lexicon Edit",
        ["posEdit"] = "Category Edit",
        ["phonemeEdit"] = "Phonemes",
        [NaturalClassTool] = "Natural Classes",
        [EnvironmentTool] = "Environments",
        ["PhonologicalRuleEdit"] = "Phonological Rules",
        ["compoundRuleAdvancedEdit"] = "Compound Rules",
        ["AdhocCoprohibEdit"] = "Ad hoc Rules",
        ["Analyses"] = "Word Analyses",
        ["phonologicalFeaturesAdvancedEdit"] = "Phonological Features",
        ["featuresAdvancedEdit"] = "Inflection Features",
        ["variantEntryTypeEdit"] = "Variant Types",
        ["ProdRestrictEdit"] = "Exception \"Features\"",
    };

    // Mirrors FieldWorks' own link follower, which also opens an object at its nearest listed owner.
    private static (string Tool, Guid Guid)? Target(LcmCache cache, ICmObject found)
    {
        var project = cache.LangProject;
        for (var current = found; current is not null; current = current.Owner)
        {
            switch (current)
            {
                case ILexEntry: return ("lexiconEdit", current.Guid);
                case IPartOfSpeech: return ("posEdit", current.Guid);
                case IPhPhoneme: return ("phonemeEdit", current.Guid);
                case IPhNaturalClass: return (NaturalClassTool, current.Guid);
                case IPhEnvironment: return (EnvironmentTool, current.Guid);
                case IPhSegmentRule: return ("PhonologicalRuleEdit", current.Guid);
                case IMoCompoundRule: return ("compoundRuleAdvancedEdit", current.Guid);
                case IMoAdhocProhib: return ("AdhocCoprohibEdit", current.Guid);
                case IWfiWordform: return ("Analyses", current.Guid);
                case IFsFeatDefn feature:
                    return feature.Owner == project.PhFeatureSystemOA
                        ? ("phonologicalFeaturesAdvancedEdit", current.Guid)
                        : ("featuresAdvancedEdit", current.Guid);
                case ILexEntryType when current.Owner == project.LexDbOA?.VariantEntryTypesOA:
                    return ("variantEntryTypeEdit", current.Guid);
                case ICmPossibility when current.Owner == project.MorphologicalDataOA?.ProdRestrictOA:
                    return ("ProdRestrictEdit", current.Guid);
            }
        }
        return null;
    }

    // FwLinkArgs decodes the whole query before splitting it on '&', so it is encoded as one string.
    private static string Build(string projectName, string tool, Guid guid)
    {
        var query = new StringBuilder()
            .Append("database=").Append(projectName)
            .Append("&tool=").Append(tool)
            .Append("&guid=").Append(guid.ToString("D"))
            .Append("&tag=")
            .ToString();
        return "silfw://localhost/link?" + HttpUtility.UrlEncode(query);
    }
}
