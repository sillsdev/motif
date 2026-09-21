using System;
using System.Text;
using System.Web;
using SIL.LCModel;
using SIL.LCModel.Core.Text;

namespace SIL.Motif.Host.PanGloss;

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
        return Target(cache, found) is { } target ? Build(projectName, target.Tool, target.Guid) : null;
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
        var repository = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
        return repository.TryGetObject(TsStringUtils.MakeString(word, cache.DefaultVernWs), true, out var wordform)
            ? Build(projectName, "Analyses", wordform.Guid)
            : null;
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
