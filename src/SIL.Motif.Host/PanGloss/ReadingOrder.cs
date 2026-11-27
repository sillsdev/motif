using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Puts a word's readings in an order the project's lexicon decides and the parser's emission order does not:
/// a reading rooted in a closed category first, then the root entry's homograph number, the root sense's
/// position and the entry's example count, then fewest morphemes, then the morph identifiers. A key the
/// project cannot supply ties instead of failing, so a lexicon without examples still sorts.
/// </summary>
/// <remarks>
/// A category is closed when nothing in the project builds on it: no affix attaches to it or an ancestor, and neither
/// it nor an ancestor owns inflection classes, affix slots or templates. A project that tags function words with an
/// open category cannot be told apart by this rule, and those readings tie on the first key.
/// </remarks>
internal static class ReadingOrder
{
    /// <summary>
    /// The closed categories of the project, found in one pass over its affixes and categories; a caller ordering
    /// many words computes this once and passes it to every <see cref="Order"/>.
    /// </summary>
    public static IReadOnlySet<Guid> ClosedCategories(LcmCache cache)
    {
        var services = cache.ServiceLocator;
        var attached = new HashSet<Guid>();
        void Add(IPartOfSpeech? category) { if (category is not null) attached.Add(category.Guid); }
        foreach (var msa in services.GetInstance<IMoInflAffMsaRepository>().AllInstances()) Add(msa.PartOfSpeechRA);
        foreach (var msa in services.GetInstance<IMoDerivAffMsaRepository>().AllInstances()) Add(msa.FromPartOfSpeechRA);
        foreach (var msa in services.GetInstance<IMoUnclassifiedAffixMsaRepository>().AllInstances()) Add(msa.PartOfSpeechRA);
        var categories = services.GetInstance<IPartOfSpeechRepository>().AllInstances().ToArray();
        var closed = new HashSet<Guid>();
        foreach (var category in categories)
        {
            var isOpen = false;
            for (var level = category; level is not null && !isOpen; level = level.Owner as IPartOfSpeech)
                isOpen = attached.Contains(level.Guid) || level.InflectionClassesOC.Count > 0 ||
                    level.AffixSlotsOC.Count > 0 || level.AffixTemplatesOS.Count > 0;
            if (!isOpen) closed.Add(category.Guid);
        }
        return closed;
    }

    /// <summary>
    /// <paramref name="readings"/> reordered, each carrying its position in <paramref name="analyses"/> as
    /// <see cref="ParserReading.SourceIndex"/>; the same readings, only their order differs.
    /// </summary>
    public static IReadOnlyList<ParserReading> Order(LcmCache cache, IReadOnlyList<ParseAnalysis> analyses,
        IReadOnlyList<ParserReading> readings, IReadOnlySet<Guid> closedCategories)
    {
        var objects = cache.ServiceLocator.ObjectRepository;
        return readings
            .Select((reading, index) => (reading: reading with { SourceIndex = index },
                key: KeyOf(objects, analyses[index], closedCategories)))
            .OrderBy(item => item.key.Open)
            .ThenBy(item => item.key.Homograph)
            .ThenBy(item => item.key.SensePosition)
            .ThenByDescending(item => item.key.Examples)
            .ThenBy(item => item.key.Morphemes)
            .ThenBy(item => item.key.Identifiers, StringComparer.Ordinal)
            .Select(item => item.reading)
            .ToArray();
    }

    private readonly record struct Key(bool Open, int Homograph, int SensePosition, int Examples, int Morphemes, string Identifiers);

    private static Key KeyOf(ICmObjectRepository objects, ParseAnalysis analysis, IReadOnlySet<Guid> closedCategories)
    {
        var identifiers = string.Join('|', analysis.Morphs.Select(morph =>
            $"{morph.Form}/{morph.Msa}/{morph.InflType}/{morph.GuessedString}"));
        var rootMsa = analysis.Morphs
            .Select(morph => Guid.TryParse(morph.Msa, out var id) && objects.TryGetObject(id, out var found) ? found : null)
            .OfType<IMoStemMsa>().FirstOrDefault();
        var open = rootMsa?.PartOfSpeechRA is not { } category || !closedCategories.Contains(category.Guid);
        if (rootMsa?.Owner is not ILexEntry entry)
            return new Key(open, int.MaxValue, int.MaxValue, 0, analysis.Morphs.Count, identifiers);
        var senses = entry.AllSenses;
        var position = senses.FindIndex(sense => sense.MorphoSyntaxAnalysisRA == rootMsa);
        return new Key(open, entry.HomographNumber, position < 0 ? int.MaxValue : position,
            senses.Sum(sense => sense.ExamplesOS.Count), analysis.Morphs.Count, identifiers);
    }
}
