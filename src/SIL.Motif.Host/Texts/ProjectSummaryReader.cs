using SIL.LCModel;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.WritingSystems;

namespace SIL.Motif.Host.Texts;

/// <summary>Captures the project and Text counts needed by stored Overview and Timing reads.</summary>
public static class ProjectSummaryReader
{
    /// <summary>Reads counts and word identities from a saved scratch or live project model.</summary>
    public static ProjectSummarySnapshot Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var texts = cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances()
            .Select(text =>
            {
                var occurrences = TextOccurrenceReader.Read(cache, [text.Guid]);
                var byWord = occurrences.OccurrencesByWord.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
                return new ProjectTextSummary(text.Guid, ReadTitle(text), byWord.Count,
                    occurrences.TotalOccurrences, byWord);
            })
            .OrderBy(text => text.TextId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        var words = texts.SelectMany(text => text.OccurrencesByWord.Keys)
            .Distinct(StringComparer.Ordinal).Count();
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
        var affixRules = entries.AllInstances()
            .SelectMany(entry => entry.AlternateFormsOS.OfType<IMoAffixAllomorph>()
                .Concat(entry.LexemeFormOA is IMoAffixAllomorph allomorph ? [allomorph] : []))
            .Select(form => form.Guid).Distinct().Count();
        var ruleCount = affixRules + cache.LangProject.MorphologicalDataOA.CompoundRulesOS.Count +
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Count;
        var wordformCount = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count;
        var wordforms = LcmWordformCorpus.ExtractForms(cache)
            .Distinct(StringComparer.Ordinal).OrderBy(word => word, StringComparer.Ordinal).ToArray();
        return new ProjectSummarySnapshot(words, texts.Sum(text => text.OccurrenceCount), wordformCount,
            ruleCount, entries.Count, wordforms, texts)
        { WritingSystems = WritingSystemDisplayReader.Read(cache), WordWritingSystems = ReadWordWritingSystems(cache) };
    }

    public static IReadOnlyDictionary<string, string?> ReadWordWritingSystems(LcmCache cache) =>
        cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
            .SelectMany(wordform => wordform.Form.AvailableWritingSystemIds.Select(ws =>
                (Text: wordform.Form.get_String(ws)?.Text?.Trim().Normalize(System.Text.NormalizationForm.FormD),
                 Tag: cache.WritingSystemFactory.GetStrFromWs(ws))))
            .Where(value => !string.IsNullOrEmpty(value.Text)).GroupBy(value => value.Text!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(value => value.Tag).Distinct().Take(2).ToArray() is
                { Length: 1 } tags ? tags[0] : null, StringComparer.Ordinal);

    private static string ReadTitle(IText text)
    {
        foreach (var writingSystem in text.Name.AvailableWritingSystemIds.OrderBy(id => id))
        {
            var value = text.Name.get_String(writingSystem)?.Text;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return "(Untitled Text)";
    }
}
