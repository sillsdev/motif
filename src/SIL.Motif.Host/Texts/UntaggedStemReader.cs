using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Texts;

/// <summary>
/// Finds stems not tagged with an inflection class whose category has no default class and has an affix restricted
/// by class, which is when the parser gives the stem no class (HCLoader.GetInflClass) and so cannot attach that affix.
/// </summary>
public static class UntaggedStemReader
{
    /// <summary>Counts the entries and lists at most <see cref="OverviewMeasureLimits.TopWords"/> of them.</summary>
    public static UntaggedStems Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToArray();
        var restrictedByCategory = RestrictedAffixesByCategory(entries);
        if (restrictedByCategory.Count == 0) return UntaggedStems.None;

        var found = new List<(ILexEntry Entry, IMoStemAllomorph Stem, IPartOfSpeech Category, int Restricted)>();
        foreach (var entry in entries)
        {
            if (entry.LexemeFormOA is not IMoStemAllomorph stem) continue;
            var held = entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>()
                .Where(msa => msa.InflectionClassRA is null && msa.PartOfSpeechRA is not null)
                .Select(msa => Held(msa.PartOfSpeechRA, restrictedByCategory))
                .Where(item => item.Restricted > 0).OrderByDescending(item => item.Restricted).ToArray();
            if (held.Length == 0) continue;
            found.Add((entry, stem, held[0].Category, held[0].Restricted));
        }
        // Rank by count first; a headword is only worked out for the entries tied at or above the cut.
        var ranked = found.OrderByDescending(item => item.Restricted).ToArray();
        var cut = ranked.Length > OverviewMeasureLimits.TopWords ? ranked[OverviewMeasureLimits.TopWords - 1].Restricted : 0;
        var top = ranked.Where(item => item.Restricted >= cut)
            .Select(item => new UntaggedStem(item.Entry.Guid, item.Stem.Guid.ToString("D"), item.Entry.HeadWord?.Text ?? string.Empty,
                item.Category.Name.BestAnalysisAlternative.Text ?? string.Empty, item.Restricted))
            .OrderByDescending(item => item.RestrictedAffixes).ThenBy(item => item.Headword, StringComparer.Ordinal)
            .ThenBy(item => item.EntryId).Take(OverviewMeasureLimits.TopWords).ToArray();
        return new UntaggedStems(found.Count, top);
    }

    // The category and its ancestors give the stem its default class and the affixes that restrict it.
    private static (IPartOfSpeech Category, int Restricted) Held(IPartOfSpeech category,
        IReadOnlyDictionary<Guid, int> restrictedByCategory)
    {
        var restricted = 0;
        var visited = new HashSet<Guid>();
        for (IPartOfSpeech? current = category; current is not null && visited.Add(current.Guid);
             current = current.Owner as IPartOfSpeech)
        {
            if (current.DefaultInflectionClassRA is not null) return (category, 0);
            restricted += restrictedByCategory.GetValueOrDefault(current.Guid);
        }
        return (category, restricted);
    }

    // Only an inflectional affix's class restriction gates a stem; derivational and stem MSAs ignore it.
    private static Dictionary<Guid, int> RestrictedAffixesByCategory(IEnumerable<ILexEntry> entries)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var entry in entries)
        {
            var restricted = entry.AlternateFormsOS.Concat(entry.LexemeFormOA is { } lexeme ? [lexeme] : [])
                .OfType<IMoAffixForm>().Count(form => form.InflectionClassesRC.Count > 0);
            if (restricted == 0) continue;
            foreach (var category in entry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>()
                         .Select(msa => msa.PartOfSpeechRA).Where(pos => pos is not null).Select(pos => pos!.Guid).Distinct())
                counts[category] = counts.GetValueOrDefault(category) + restricted;
        }
        return counts;
    }
}
