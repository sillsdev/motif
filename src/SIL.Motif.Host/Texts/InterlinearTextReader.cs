using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;

namespace SIL.Motif.Host.Texts;

/// <summary>
/// Builds an <see cref="InterlinearTextProjection"/> from a live <see cref="LcmCache"/>, entirely through
/// LibLCM's domain services — never by reading <c>.fwdata</c> as XML.
/// </summary>
/// <remarks>
/// <para>
/// <b>Word order comes from <see cref="SegmentServices.GetAnalysisOccurrences"/></b>, grouped back to each
/// declared segment with <c>ToLookup</c> (which preserves both key-first-seen and per-key insertion
/// order), rather than from a second, hand-rolled walk of <c>Segment.AnalysesRS</c>. The two would agree
/// on this projection's own fixtures, but only one of them is the service the design calls for.
/// </para>
/// <para>
/// <b>Matching is by declared reference only.</b> A bundle's morpheme, sense and category come from
/// whichever <c>MoForm</c>, <c>LexSense</c> and <c>PartOfSpeech</c> the analysis itself references, never
/// by comparing forms or glosses to guess a match.
/// </para>
/// </remarks>
public static class InterlinearTextReader
{
    /// <summary>Reads one Text, following its declared paragraph, segment and analysis order throughout.</summary>
    public static InterlinearTextProjection Read(LcmCache cache, IText text)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(text);

        var title = ReadItems(cache, "title", text.Name).ToList();
        var paragraphs = text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>()
            .Select(paragraph => ReadParagraph(cache, paragraph)).ToList()
            ?? new List<InterlinearParagraph>();

        return new InterlinearTextProjection(text.Guid, title, paragraphs);
    }

    private static InterlinearParagraph ReadParagraph(LcmCache cache, IStTxtPara paragraph)
    {
        var occurrencesBySegment = SegmentServices.GetAnalysisOccurrences(paragraph).ToLookup(o => o.Segment);

        var phrases = paragraph.SegmentsOS
            .Select(segment => ReadPhrase(cache, segment, occurrencesBySegment[segment]))
            .ToList();

        return new InterlinearParagraph(paragraph.Guid, phrases);
    }

    private static InterlinearPhrase ReadPhrase(
        LcmCache cache, ISegment segment, IEnumerable<AnalysisOccurrence> occurrences)
    {
        var items = ReadItems(cache, "gls", segment.FreeTranslation)
            .Concat(ReadItems(cache, "lit", segment.LiteralTranslation))
            .ToList();
        var words = occurrences.Select(occurrence => ReadWord(cache, occurrence.Analysis)).ToList();

        return new InterlinearPhrase(segment.Guid, items, words);
    }

    private static InterlinearWord ReadWord(LcmCache cache, IAnalysis analysis) => analysis switch
    {
        IWfiGloss gloss => ReadAnalysedWord(cache, (IWfiAnalysis)gloss.Owner),
        IWfiAnalysis wordAnalysis => ReadAnalysedWord(cache, wordAnalysis),
        IWfiWordform wordform => new InterlinearWord(
            wordform.Guid, InterlinearAnalysisStatus.Unanalysed, ReadItems(cache, "txt", wordform.Form).ToList(),
            []),
        IPunctuationForm punctuation => new InterlinearWord(
            null, InterlinearAnalysisStatus.Unanalysed, ReadPunctuationItem(cache, punctuation).ToList(), []),
        _ => throw new NotSupportedException($"Unrecognized analysis kind: {analysis.GetType()}"),
    };

    private static InterlinearWord ReadAnalysedWord(LcmCache cache, IWfiAnalysis analysis)
    {
        var wordform = (IWfiWordform)analysis.Owner;

        var items = ReadItems(cache, "txt", wordform.Form).ToList();
        if (analysis.CategoryRA is { } category) items.AddRange(ReadCategoryItem(cache, category));

        var status = wordform.HumanApprovedAnalyses.Contains(analysis)
            ? InterlinearAnalysisStatus.Approved
            : InterlinearAnalysisStatus.Unapproved;
        var morphemes = analysis.MorphBundlesOS.Select(bundle => ReadMorpheme(cache, bundle)).ToList();

        return new InterlinearWord(wordform.Guid, status, items, morphemes);
    }

    private static InterlinearMorpheme ReadMorpheme(LcmCache cache, IWfiMorphBundle bundle)
    {
        var items = new List<FlexItem>();

        var formItems = bundle.MorphRA?.Form is { } morphForm ? ReadItems(cache, "txt", morphForm).ToList() : [];
        items.AddRange(formItems.Count > 0 ? formItems : ReadItems(cache, "txt", bundle.Form));

        if (bundle.SenseRA?.Entry.CitationForm is { } citationForm)
            items.AddRange(ReadItems(cache, "cf", citationForm));
        if (bundle.SenseRA?.Gloss is { } gloss)
            items.AddRange(ReadItems(cache, "gls", gloss));

        var msaAbbreviation = bundle.MsaRA?.InterlinearAbbr;
        if (!string.IsNullOrEmpty(msaAbbreviation))
            items.Add(new FlexItem("msa", AnalysisTag(cache), msaAbbreviation));

        return new InterlinearMorpheme(bundle.MorphRA?.Guid, items);
    }

    private static IEnumerable<FlexItem> ReadCategoryItem(LcmCache cache, IPartOfSpeech category)
    {
        var items = ReadItems(cache, "pos", category.Abbreviation).ToList();
        return items.Count > 0 ? items : ReadItems(cache, "pos", category.Name);
    }

    private static IEnumerable<FlexItem> ReadPunctuationItem(LcmCache cache, IPunctuationForm punctuation)
    {
        var text = punctuation.Form?.Text;
        if (string.IsNullOrEmpty(text)) yield break;

        var ws = TsStringUtils.GetWsAtOffset(punctuation.Form!, 0);
        yield return new FlexItem("punct", cache.WritingSystemFactory.GetStrFromWs(ws), text);
    }

    private static IEnumerable<FlexItem> ReadItems<TAccessor>(LcmCache cache, string type, TAccessor accessor)
        where TAccessor : IMultiAccessorBase, ITsMultiString
    {
        foreach (var ws in accessor.AvailableWritingSystemIds.OrderBy(w => w))
        {
            var text = accessor.get_String(ws)?.Text;
            if (string.IsNullOrEmpty(text)) continue;
            yield return new FlexItem(type, cache.WritingSystemFactory.GetStrFromWs(ws), text);
        }
    }

    private static string AnalysisTag(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
}
