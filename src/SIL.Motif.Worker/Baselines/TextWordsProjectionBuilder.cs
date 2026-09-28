using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;

namespace SIL.Motif.Worker.Baselines;

/// <summary>Builds the complete ordered Text words projection from one saved Baseline cache.</summary>
public static class TextWordsProjectionBuilder
{
    /// <summary>
    /// Reads Text lines, occurrences, analyses and stable FieldWorks link targets from a saved cache, checking
    /// <paramref name="cancellationToken"/> before each Text.
    /// </summary>
    public static TextWordsProjection Build(LcmCache cache, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var wordforms = new Dictionary<Guid, TextWordsProjectedWordform>();
        var texts = new List<TextWordsProjectedText>();
        // The query looks Texts up by id; a fixed order only keeps the stored bytes stable across captures.
        foreach (var text in cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances()
                     .OrderBy(text => text.Guid.ToString("D"), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            texts.Add(ReadText(cache, text, wordforms));
        }
        return new TextWordsProjection(texts, wordforms.Values
            .OrderBy(wordform => wordform.WordformId.ToString("D"), StringComparer.Ordinal).ToArray());
    }

    private static TextWordsProjectedText ReadText(
        LcmCache cache, IText text, Dictionary<Guid, TextWordsProjectedWordform> wordforms)
    {
        var lines = new List<TextWordsProjectedLine>();
        var analyses = new Dictionary<string, TextWordsProjectedAnalysis>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var paragraph in text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? Enumerable.Empty<IStTxtPara>())
        {
            var occurrencesBySegment = SegmentServices.GetAnalysisOccurrences(paragraph).ToLookup(o => o.Segment);
            foreach (var segment in paragraph.SegmentsOS)
            {
                lineNumber++;
                var sentence = segment.BaselineText?.Text ?? string.Empty;
                var tokens = occurrencesBySegment[segment]
                    .Select(occurrence => ReadToken(cache, occurrence, wordforms, analyses)).ToArray();
                lines.Add(new TextWordsProjectedLine(lineNumber, sentence, tokens, paragraph.Guid,
                    segment.Guid, paragraph.ParseIsCurrent));
            }
        }

        return new TextWordsProjectedText(text.Guid, ReadTitle(text), lines, analyses.Values.ToArray());
    }

    private static TextWordsProjectedToken ReadToken(
        LcmCache cache, AnalysisOccurrence occurrence, Dictionary<Guid, TextWordsProjectedWordform> wordforms,
        Dictionary<string, TextWordsProjectedAnalysis> analyses)
    {
        var analysis = occurrence.Analysis;
        if (analysis is IPunctuationForm punctuation)
            return new TextWordsProjectedToken(
                punctuation.Form?.Text ?? string.Empty, [], null, null, null, null, null, null,
                occurrence.Index, null);

        var (wordform, wfiAnalysis) = analysis switch
        {
            IWfiGloss wordGloss => ((IWfiWordform)wordGloss.Owner.Owner, (IWfiAnalysis)wordGloss.Owner),
            IWfiAnalysis wordAnalysis => ((IWfiWordform)wordAnalysis.Owner, wordAnalysis),
            IWfiWordform bare => (bare, (IWfiAnalysis?)null),
            _ => throw new NotSupportedException($"Unrecognized analysis kind: {analysis.GetType()}"),
        };

        if (!wordforms.ContainsKey(wordform.Guid))
            wordforms.Add(wordform.Guid, ReadWordform(cache, wordform));

        var forms = TxtForms(wordform.Form).ToArray();
        var status = wfiAnalysis is { } chosen
            ? wordform.HumanApprovedAnalyses.Contains(chosen)
                ? InterlinearAnalysisStatus.Approved
                : InterlinearAnalysisStatus.Unapproved
            : InterlinearAnalysisStatus.Unanalysed;
        string? analysisKey = null;
        if (wfiAnalysis is { } selected)
        {
            var projected = BuildProjectAnalysis(cache, selected);
            analyses.TryAdd(projected.Key, projected);
            analysisKey = projected.Key;
        }
        var tokenText = forms.Length > 0 ? forms[0] : string.Empty;
        var chosenWordGloss = analysis is IWfiGloss chosenGloss ? BestText(chosenGloss.Form) : null;
        var category = wfiAnalysis?.CategoryRA is { } pos ? BestText(pos.Abbreviation) ?? BestText(pos.Name) : null;
        // Its own wordform, never a lookup by spelling: another wordform can share the spelling and win the lookup.
        var wordLinkTarget = tokenText.Length == 0 ? null : FieldWorksLinks.TargetFor(cache, wordform);
        return new TextWordsProjectedToken(tokenText, forms, wordform.Guid, status, analysisKey,
            chosenWordGloss, category, wordLinkTarget, occurrence.Index, wfiAnalysis?.Guid);
    }

    private static TextWordsProjectedWordform ReadWordform(LcmCache cache, IWfiWordform wordform)
    {
        var humanApproved = wordform.HumanApprovedAnalyses.ToList();
        var humanDisapproved = wordform.HumanDisapprovedParses.ToList();
        var withOpinion = humanApproved.Concat(humanDisapproved).ToHashSet();
        var candidates = wordform.AnalysesOC.Count(analysis => !withOpinion.Contains(analysis));
        return new TextWordsProjectedWordform(wordform.Guid,
            humanApproved.Select(analysis => BuildProjectAnalysis(cache, analysis)).ToArray(),
            humanDisapproved.Select(analysis => BuildProjectAnalysis(cache, analysis)).ToArray(),
            candidates, wordform.SpellingStatus == IncorrectSpellingStatus);
    }

    private static TextWordsProjectedAnalysis BuildProjectAnalysis(LcmCache cache, IWfiAnalysis analysis)
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
            ParserReadingReader.EntryTargetFor(cache, morphs[index]))).ToArray();
        return new TextWordsProjectedAnalysis(AnalysisContent.ComputeDigest(bundles), projectedMorphs);
    }

    private static string? BestText(IMultiAccessorBase accessor) => accessor.AvailableWritingSystemIds.OrderBy(ws => ws)
        .Select(ws => accessor.get_String(ws)?.Text).FirstOrDefault(text => !string.IsNullOrEmpty(text));

    private static IEnumerable<string> TxtForms<TAccessor>(TAccessor accessor)
        where TAccessor : IMultiAccessorBase, ITsMultiString
    {
        foreach (var ws in accessor.AvailableWritingSystemIds.OrderBy(writingSystem => writingSystem))
        {
            var text = accessor.get_String(ws)?.Text;
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }

    private static string ReadTitle(IText text)
    {
        foreach (var writingSystem in text.Name.AvailableWritingSystemIds.OrderBy(id => id))
        {
            var value = text.Name.get_String(writingSystem)?.Text;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return string.Empty;
    }

    private const int IncorrectSpellingStatus = 2;
}
