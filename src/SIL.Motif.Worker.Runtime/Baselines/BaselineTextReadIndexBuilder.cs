using SIL.Motif.Host.Texts;

namespace SIL.Motif.Worker.Baselines;

internal static class BaselineTextReadIndexBuilder
{
    public static IReadOnlyList<BaselineTextReadIndex> Build(TextWordsProjection projection)
    {
        var wordforms = projection.Wordforms.ToDictionary(wordform => wordform.WordformId);
        return projection.Texts.Select(text => Build(text, wordforms)).ToArray();
    }

    private static BaselineTextReadIndex Build(TextWordsProjectedText text,
        IReadOnlyDictionary<Guid, TextWordsProjectedWordform> wordforms)
    {
        var referenced = text.Lines.SelectMany(line => line.Tokens).Select(token => token.WordformId)
            .OfType<Guid>().Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var facts = referenced.Select(id =>
        {
            if (!wordforms.TryGetValue(id, out var wordform))
                throw new InvalidDataException("A Text token references a missing wordform projection.");
            return new BaselineTextReadWordform(id, wordform.Approved.Count, wordform.CandidateCount,
                wordform.Disapproved.Count, wordform.IncorrectSpelling,
                wordform.Analyses.Select(analysis => new BaselineTextReadAnalysis(
                    analysis.Key, analysis.AnalysisId, analysis.Opinion, analysis.Identity ??
                        throw new InvalidDataException("An analysis has no semantic identity."))).ToArray());
        }).ToArray();
        var lines = text.Lines.Select(line => new BaselineTextReadLine(line.Number, line.ParagraphId,
            line.SegmentId, line.ParseIsCurrent, line.Tokens.Select(token => new BaselineTextReadToken(
                token.OccurrenceIndex, token.WordformId, token.Status, token.AnalysisKey, token.AnalysisId,
                token.Forms.ToArray())).ToArray())).ToArray();
        return new BaselineTextReadIndex(text.TextId, text.Title, text.TitleWritingSystem, lines, facts);
    }
}
