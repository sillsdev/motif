using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;
using SIL.Motif.Projection;

namespace SIL.Motif.Worker.Baselines;

/// <summary>Builds the complete ordered Text words projection from one saved Baseline cache.</summary>
public static class TextWordsProjectionBuilder
{
    /// <summary>
    /// Reads selected Text lines, occurrences, analyses and FieldWorks link targets from a saved cache, checking
    /// <paramref name="cancellationToken"/> before each selected Text. A null id set selects all Texts.
    /// </summary>
    public static TextWordsProjection Build(LcmCache cache, CancellationToken cancellationToken,
        IReadOnlySet<Guid>? textIds = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var wordforms = new Dictionary<Guid, TextWordsProjectedWordform>();
        var formsByWordform = new Dictionary<Guid, WritingSystemText[]>();
        var texts = new List<TextWordsProjectedText>();
        // The query looks Texts up by id; a fixed order only keeps the stored bytes stable across captures.
        foreach (var text in cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances()
                     .OrderBy(text => text.Guid.ToString("D"), StringComparer.Ordinal))
        {
            if (textIds is not null && !textIds.Contains(text.Guid)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            texts.Add(ReadText(cache, text, wordforms, formsByWordform));
        }
        return new TextWordsProjection(texts, wordforms.Values
            .OrderBy(wordform => wordform.WordformId.ToString("D"), StringComparer.Ordinal).ToArray());
    }

    private static TextWordsProjectedText ReadText(
        LcmCache cache, IText text, Dictionary<Guid, TextWordsProjectedWordform> wordforms,
        Dictionary<Guid, WritingSystemText[]> formsByWordform)
    {
        var lines = new List<TextWordsProjectedLine>();
        var analyses = new Dictionary<string, TextWordsProjectedAnalysis>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var paragraph in text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? Enumerable.Empty<IStTxtPara>())
        {
            var style = paragraph.StyleRules?.GetStrPropValue((int)FwTextPropType.ktptNamedStyle);
            var occurrencesBySegment = SegmentServices.GetAnalysisOccurrences(paragraph).ToLookup(o => o.Segment);
            foreach (var segment in paragraph.SegmentsOS)
            {
                lineNumber++;
                var sentence = segment.BaselineText?.Text ?? string.Empty;
                var tokens = occurrencesBySegment[segment]
                    .Select(occurrence => ReadToken(cache, occurrence, wordforms, analyses, formsByWordform)).ToArray();
                lines.Add(new TextWordsProjectedLine(lineNumber, sentence, tokens, paragraph.Guid,
                    segment.Guid, paragraph.ParseIsCurrent)
                {
                    SentenceWritingSystem = WritingSystemTextReader.SingleId(cache, segment.BaselineText),
                    SentenceStyle = string.IsNullOrEmpty(style) ? "Normal" : style,
                });
            }
        }

        var title = WritingSystemTextReader.First(cache, text.Name);
        if (title.Text.Length == 0) title = new WritingSystemText("(Untitled Text)", null);
        return new TextWordsProjectedText(text.Guid, title.Text, lines, analyses.Values.ToArray())
        { TitleWritingSystem = title.WritingSystem };
    }

    private static TextWordsProjectedToken ReadToken(
        LcmCache cache, AnalysisOccurrence occurrence, Dictionary<Guid, TextWordsProjectedWordform> wordforms,
        Dictionary<string, TextWordsProjectedAnalysis> analyses, Dictionary<Guid, WritingSystemText[]> formsByWordform)
    {
        var analysis = occurrence.Analysis;
        if (analysis is IPunctuationForm punctuation)
            return new TextWordsProjectedToken(
                punctuation.Form?.Text ?? string.Empty, [], null, null, null, null, null, null,
                occurrence.Index, null) { TextWritingSystem = WritingSystemTextReader.SingleId(cache, punctuation.Form) };

        var (wordform, wfiAnalysis) = analysis switch
        {
            IWfiGloss wordGloss => ((IWfiWordform)wordGloss.Owner.Owner, (IWfiAnalysis)wordGloss.Owner),
            IWfiAnalysis wordAnalysis => ((IWfiWordform)wordAnalysis.Owner, wordAnalysis),
            IWfiWordform bare => (bare, (IWfiAnalysis?)null),
            _ => throw new NotSupportedException($"Unrecognized analysis kind: {analysis.GetType()}"),
        };

        if (!wordforms.TryGetValue(wordform.Guid, out var projectedWordform))
        {
            projectedWordform = ReadWordform(cache, wordform);
            wordforms.Add(wordform.Guid, projectedWordform);
        }
        if (!formsByWordform.TryGetValue(wordform.Guid, out var forms))
        {
            forms = TxtForms(cache, wordform.Form).ToArray();
            formsByWordform.Add(wordform.Guid, forms);
        }
        var projected = wfiAnalysis is { } selected
            ? projectedWordform.Analyses.First(item => item.AnalysisId == selected.Guid) : null;
        var status = projected is not null
            ? projected.Opinion == "approved" ? InterlinearAnalysisStatus.Approved : InterlinearAnalysisStatus.Unapproved
            : InterlinearAnalysisStatus.Unanalysed;
        if (projected is not null) analyses.TryAdd(projected.Key, projected);
        var analysisKey = projected?.Key;
        var tokenText = forms.Length > 0 ? forms[0].Text : string.Empty;
        var chosenWordGloss = analysis is IWfiGloss chosenGloss ? WritingSystemTextReader.First(cache, chosenGloss.Form) : null;
        var category = wfiAnalysis?.CategoryRA is { } pos
            ? WritingSystemTextReader.First(cache, pos.Abbreviation) : null;
        if (category?.Text.Length == 0 && wfiAnalysis?.CategoryRA is { } namedPos)
            category = WritingSystemTextReader.First(cache, namedPos.Name);
        // Its own wordform, never a lookup by spelling: another wordform can share the spelling and win the lookup.
        var wordLinkTarget = tokenText.Length == 0 ? null : FieldWorksLinks.TargetFor(cache, wordform);
        return new TextWordsProjectedToken(tokenText, forms, wordform.Guid, status, analysisKey,
            chosenWordGloss?.Text, category?.Text, wordLinkTarget, occurrence.Index, wfiAnalysis?.Guid)
        {
            TextWritingSystem = forms.FirstOrDefault()?.WritingSystem,
            WordGlossWritingSystem = chosenWordGloss?.WritingSystem,
            CategoryWritingSystem = category?.WritingSystem,
        };
    }

    /// <summary>Captures all analyses and opinions of a wordform, including one absent from every Text.</summary>
    public static TextWordsProjectedWordform ReadWordform(LcmCache cache, IWfiWordform wordform)
    {
        var humanApproved = wordform.HumanApprovedAnalyses.ToList();
        var humanDisapproved = wordform.HumanDisapprovedParses.ToList();
        var analyses = wordform.AnalysesOC.Select(analysis =>
            BuildProjectAnalysis(cache, wordform, analysis, OpinionOf(humanApproved, humanDisapproved, analysis))).ToArray();
        return new TextWordsProjectedWordform(wordform.Guid,
            analyses.Where(analysis => analysis.Opinion == "approved").ToArray(),
            analyses.Where(analysis => analysis.Opinion == "disapproved").ToArray(),
            analyses.Count(analysis => analysis.Opinion == "unknown"),
            wordform.SpellingStatus == IncorrectSpellingStatus, analyses);
    }

    private static TextWordsProjectedAnalysis BuildProjectAnalysis(
        LcmCache cache, IWfiWordform wordform, IWfiAnalysis analysis, string opinion)
    {
        var bundles = analysis.MorphBundlesOS.Select(bundle => new MorphBundleContent(
            bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D")))
            .ToList();
        var morphs = analysis.MorphBundlesOS.Select(bundle => new ParseMorph(
            bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D"),
            GuessedString: null)).ToArray();
        var displayMorphs = ParserReadingReader.ReadMorphs(cache, string.Empty, morphs);
        var projectedMorphs = displayMorphs.Select((morph, index) => new TextWordsProjectedMorph(
            morph.Form, morph.Gloss, morph.Category, morph.InflectionType, morph.Guessed,
            ParserReadingReader.EntryTargetFor(cache, morphs[index])) { Entry = morph.Entry, FormWritingSystem = morph.FormWritingSystem,
                GlossWritingSystem = morph.GlossWritingSystem, CategoryWritingSystem = morph.CategoryWritingSystem,
                InflectionTypeWritingSystem = morph.InflectionTypeWritingSystem, EntryWritingSystem = morph.EntryWritingSystem }).ToArray();
        var identity = ReadIdentity(cache, wordform, analysis);
        return new TextWordsProjectedAnalysis(AnalysisContent.ComputeDigest(bundles), projectedMorphs)
        {
            AnalysisId = analysis.Guid,
            Opinion = opinion,
            Identity = identity,
        };
    }

    /// <summary>Reads matching identities and opinions without constructing display morphology.</summary>
    public static BaselineTextReadWordform ReadWordformFacts(LcmCache cache, IWfiWordform wordform)
    {
        var approved = wordform.HumanApprovedAnalyses.ToHashSet();
        var disapproved = wordform.HumanDisapprovedParses.ToHashSet();
        var analyses = wordform.AnalysesOC.Select(analysis => new BaselineTextReadAnalysis(
            AnalysisContent.ComputeDigest(analysis.MorphBundlesOS.Select(bundle => new MorphBundleContent(
                bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"),
                bundle.InflTypeRA?.Guid.ToString("D"))).ToList()),
            analysis.Guid, OpinionOf(approved, disapproved, analysis), ReadIdentity(cache, wordform, analysis))).ToArray();
        return new BaselineTextReadWordform(wordform.Guid,
            analyses.Count(analysis => analysis.Opinion == "approved"),
            analyses.Count(analysis => analysis.Opinion == "unknown"),
            analyses.Count(analysis => analysis.Opinion == "disapproved"),
            wordform.SpellingStatus == IncorrectSpellingStatus, analyses);
    }

    private static ApprovedMorphology ReadIdentity(LcmCache cache, IWfiWordform wordform, IWfiAnalysis analysis)
    {
        return new ApprovedMorphology(analysis.MorphBundlesOS.Select(bundle => new ApprovedMorph(
            bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D"),
            bundle.Form.AvailableWritingSystemIds.Order().Select(ws => bundle.Form.get_String(ws)?.Text)
                .OfType<string>().Where(text => text.Length > 0).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray())).ToArray())
        {
            SourceAnalysisId = CanonicalId.FromGuid(analysis.Guid).Value,
            SourceWordformGuid = CanonicalId.FromGuid(wordform.Guid).Value,
            WritingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
        };
    }

    private static string OpinionOf(IEnumerable<IWfiAnalysis> approved, IEnumerable<IWfiAnalysis> disapproved,
        IWfiAnalysis analysis) => approved.Contains(analysis) ? "approved"
        : disapproved.Contains(analysis) ? "disapproved" : "unknown";

    private static IEnumerable<WritingSystemText> TxtForms<TAccessor>(LcmCache cache, TAccessor accessor)
        where TAccessor : IMultiAccessorBase, ITsMultiString
    {
        foreach (var ws in accessor.AvailableWritingSystemIds.OrderBy(writingSystem => writingSystem))
        {
            var text = accessor.get_String(ws)?.Text;
            if (!string.IsNullOrEmpty(text)) yield return new(text, cache.WritingSystemFactory.GetStrFromWs(ws));
        }
    }

    private const int IncorrectSpellingStatus = 2;
}
