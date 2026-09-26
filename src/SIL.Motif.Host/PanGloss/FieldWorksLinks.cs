using System;
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
    /// such wordform in its default vernacular writing system.
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

    /// <summary>Finds the stable wordform destination for a word in the default vernacular writing system.</summary>
    public static FieldWorksLinkTarget? WordformTargetFor(LcmCache cache, string word)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(word);
        var repository = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
        return repository.TryGetObject(TsStringUtils.MakeString(word, cache.DefaultVernWs), true, out var wordform)
            ? new FieldWorksLinkTarget("Analyses", wordform.Guid)
            : null;
    }

    /// <summary>Builds a request-specific link from a stable FieldWorks destination.</summary>
    public static string? ForTarget(string projectName, FieldWorksLinkTarget? target)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        return target is null ? null : Build(projectName, target.Tool, target.ObjectId);
    }

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
                case IPhNaturalClass: return ("naturalClassedit", current.Guid);
                case IPhEnvironment: return ("EnvironmentEdit", current.Guid);
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
