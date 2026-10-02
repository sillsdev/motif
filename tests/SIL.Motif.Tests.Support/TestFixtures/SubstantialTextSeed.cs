using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.Tests.TestFixtures;

internal static class SubstantialTextSeed
{
    internal const int WordsPerLine = 8;
    internal const int DistinctUnanalysedForms = 31;

    internal static SubstantialTextWorkload AppendLines(
        LcmCache cache, SeededText existingText, int lineCount = 300)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(existingText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineCount);

        var services = cache.ServiceLocator;
        var text = services.GetInstance<ITextRepository>().GetObject(existingText.TextId);
        var contents = text.ContentsOA!;
        var vernWs = cache.DefaultVernWs;
        var analysedWordform = services.GetInstance<IWfiWordformRepository>()
            .GetObject(existingText.AnalysedWordformId);
        IWfiWordform[] unanalysedWordforms = [];
        Guid firstParagraphId = default;
        Guid firstSegmentId = default;
        Guid lastParagraphId = default;
        Guid lastSegmentId = default;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            unanalysedWordforms = Enumerable.Range(0, DistinctUnanalysedForms)
                .Select(index => services.GetInstance<IWfiWordformFactory>().Create(
                    TsStringUtils.MakeString($"scale-word-{index:D2}", vernWs)))
                .ToArray();

            for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
                contents.ParagraphsOS.Add(paragraph);
                var forms = Enumerable.Range(0, WordsPerLine - 1)
                    .Select(column => unanalysedWordforms[
                        (lineIndex * (WordsPerLine - 1) + column) % DistinctUnanalysedForms]
                        .Form.VernacularDefaultWritingSystem.Text)
                    .Append(SeededProject.AnalysedWordForm)
                    .ToArray();
                paragraph.Contents = TsStringUtils.MakeString(string.Join(' ', forms), vernWs);
                var segment = paragraph.SegmentsOS.Count > 0
                    ? paragraph.SegmentsOS[0]
                    : services.GetInstance<ISegmentFactory>().Create();
                if (paragraph.SegmentsOS.Count == 0) paragraph.SegmentsOS.Add(segment);
                foreach (var column in Enumerable.Range(0, WordsPerLine - 1))
                    segment.AnalysesRS.Add(unanalysedWordforms[
                        (lineIndex * (WordsPerLine - 1) + column) % DistinctUnanalysedForms]);
                segment.AnalysesRS.Add(analysedWordform.AnalysesOC.Single());
                paragraph.ParseIsCurrent = true;

                if (lineIndex == 0)
                {
                    firstParagraphId = paragraph.Guid;
                    firstSegmentId = segment.Guid;
                }
                if (lineIndex == lineCount - 1)
                {
                    lastParagraphId = paragraph.Guid;
                    lastSegmentId = segment.Guid;
                }
            }
        });

        return new SubstantialTextWorkload(existingText.TextId, firstParagraphId, firstSegmentId,
            lastParagraphId, lastSegmentId, lineCount, lineCount * WordsPerLine);
    }
}

internal sealed record SubstantialTextWorkload(
    Guid TextId,
    Guid FirstParagraphId,
    Guid FirstSegmentId,
    Guid LastParagraphId,
    Guid LastSegmentId,
    int AppendedLineCount,
    int AppendedOccurrenceCount);
