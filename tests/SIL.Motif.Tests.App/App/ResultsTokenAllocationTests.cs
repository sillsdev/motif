using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

public sealed class ResultsTokenAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void UnparsedOccurrencesKeepStoredMorphDisplaysAffordableUntilTheyAreRead()
    {
        var wordform = Guid.NewGuid();
        var link = $"silfw://localhost/link?database=Scale&tool=lexiconEdit&guid={Guid.NewGuid():D}";
        var morphs = Enumerable.Range(0, 32).Select(index =>
            new ParserReadingMorph($"morph{index}", $"gloss{index}", "n", null, false, link)).ToArray();
        var analysis = new ProjectAnalysis("stored", morphs)
        {
            StoredAnalysisId = CanonicalId.FromGuid(Guid.NewGuid()).Value,
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };
        var source = new TextToken("word", "word", "gloss", "approved")
        {
            Analysis = analysis, StoredAnalyses = [analysis], WordformId = wordform, WordLink = link,
        };
        var line = new TextLine(1, Enumerable.Range(0, 2000).Select(index =>
            source with { OccurrenceIndex = index }).ToArray());
        var before = GC.GetAllocatedBytesForCurrentThread();

        var projected = new ResultsLineViewModel("Scale", line, new Dictionary<string, AssessmentWordResult>());

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"2000 unparsed occurrences with 32 stored morphs: {allocated / 1048576d:F1} MiB allocated");
        Assert.True(allocated < 32 * 1048576L, $"Unparsed occurrence construction allocated {allocated / 1048576d:F1} MiB");
        Assert.Equal(2000, projected.Tokens.Count);
        var first = projected.Tokens[0];
        Assert.Equal(32, first.Stored.Count);
        var display = Assert.Single(first.FieldWorksAnalyses);
        Assert.Equal(32, display.Morphs.Count);
        Assert.Equal("gloss31", display.Morphs[^1].Gloss);
        Assert.Equal(new Uri(link), first.WordLink);
        Assert.Equal(OccurrenceVerdict.NotAssessed, first.Verdict);
        Assert.Equal(ProjectStanding.Approved, first.Comparison.Standing);
        Assert.Equal(WordRowOutcome.NotParsed, first.Comparison.Outcome);
        Assert.Same(first.Comparison, first.Comparison);
        Assert.Same(first.Stored, first.Stored);
        Assert.Same(first.FieldWorksAnalyses, first.FieldWorksAnalyses);
        Assert.Same(first.WordLink, first.WordLink);
    }
}
