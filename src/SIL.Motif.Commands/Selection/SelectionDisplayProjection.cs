using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;

namespace SIL.Motif.Commands.SelectionReading;

/// <summary>Projects a bounded captured slice into shared word analysis and source-token display records.</summary>
public static class SelectionDisplayProjection
{
    /// <summary>Converts one visible wordform's morphology without creating occurrence or sentence records.</summary>
    public static IReadOnlyList<ProjectAnalysis> StoredAnalyses(TextWordsProjectedWordform wordform,
        string? projectName = null) => wordform.Analyses.Select(analysis =>
            TextWordsQuery.ReadAnalysis(analysis, projectName ?? string.Empty,
                target => projectName is null ? null : FieldWorksLinks.ForTarget(projectName, target))).ToArray();

    /// <summary>Converts only the returned fragment, sharing stored analyses once per exact wordform.</summary>
    public static IReadOnlyList<TextLine> Lines(TextLineSlice slice, SavedProjectNavigation? navigation = null)
    {
        var wordforms = slice.Wordforms.Values.DistinctBy(word => word.WordformId)
            .ToDictionary(word => word.WordformId);
        var analyses = wordforms.ToDictionary(pair => pair.Key, pair => pair.Value.Analyses
            .Select(analysis => TextWordsQuery.ReadAnalysis(analysis, string.Empty,
                target => navigation?.LinkFor(target))).ToArray());
        return slice.Lines.Select(line => new TextLine(line.LineNumber, line.Tokens.Select(item =>
        {
            var token = item.Token;
            var stored = token.WordformId is { } id && analyses.TryGetValue(id, out var values) ? values : [];
            var storedId = token.AnalysisId is { } analysisId ? CanonicalId.FromGuid(analysisId).Value : null;
            var selected = storedId is null ? null : stored.FirstOrDefault(analysis => analysis.StoredAnalysisId == storedId);
            var primary = token.Forms.FirstOrDefault();
            var form = primary?.Text.Trim().Normalize(System.Text.NormalizationForm.FormD);
            return new TextToken(token.Text, string.IsNullOrEmpty(form) ? null : form,
                selected is null ? null : string.Join(" ", selected.Morphs.Select(morph =>
                    morph.Gloss.Length == 0 ? "?" : morph.Gloss)), token.Status)
            {
                TextWritingSystem = token.TextWritingSystem,
                FormWritingSystem = primary?.WritingSystem,
                WordGlossWritingSystem = token.WordGlossWritingSystem,
                CategoryWritingSystem = token.CategoryWritingSystem,
                Analysis = selected,
                StoredAnalyses = stored,
                WordGloss = token.WordGloss,
                Category = token.Category,
                WordformId = token.WordformId,
                OccurrenceIndex = token.OccurrenceIndex,
                StoredAnalysisId = storedId,
                IncorrectSpelling = token.WordformId is { } markedId && wordforms.TryGetValue(markedId, out var wordform) &&
                    wordform.IncorrectSpelling,
                WordLink = navigation?.LinkFor(token.WordLinkTarget),
            };
        }).ToArray())
        {
            ParagraphId = line.ParagraphId,
            SegmentId = line.SegmentId,
            ParseIsCurrent = line.ParseIsCurrent,
            SentenceStyle = line.SentenceStyle,
            SentenceWritingSystem = line.SentenceWritingSystem,
        }).ToArray();
    }
}
