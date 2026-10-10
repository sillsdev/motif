using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

// The reader allocates on pool threads, so the count is process-wide and must not include other tests' work.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessAllocationCollection
{
    public const string Name = "Process allocation measurement";
}

[Collection(ProcessAllocationCollection.Name)]
public sealed class ResultsTokenAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UnparsedOccurrencesKeepStoredMorphDisplaysAffordableUntilTheyAreRead()
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
        var textId = Guid.NewGuid();
        using var store = SIL.Motif.Tests.TestFixtures.StoredSelectionFixture.FromDisplayRecords(
            new TextWordsResponse([], [new TextLines(textId, "Scale", [line])], true));
        var client = new FakeCommandClient();
        await using var fixture = new SelectionModelFixture(client);
        var selection = new SelectionViewModel(client);
        var words = new TextWordsViewModel(client, selection, client.ReaderOwner);
        var owner = new ResultsInTextViewModel(words, new AssessViewModel(client, selection), _ => { }, _ => { },
            new ChangesViewModel(client), client, client.ReaderOwner);
        client.SelectionReaderHandler = store.OpenAsync;
        await client.ReaderOwner.ReloadAsync(store.ProjectPath,
            [textId], []);
        var before = GC.GetTotalAllocatedBytes(true);

        await owner.SelectionRefresh;
        var projected = owner.RealizeLine(Assert.Single(owner.VisibleHeaders));
        await ((SIL.Motif.App.Services.IProgressivePageSource)projected.TokenSource!).ReadPageAsync(1980, 20, CancellationToken.None);

        var allocated = GC.GetTotalAllocatedBytes(true) - before;
        output.WriteLine($"20 leased occurrences from a 2000-token sentence with 32 stored morphs: {allocated / 1048576d:F1} MiB allocated");
        Assert.True(allocated < 32 * 1048576L, $"Unparsed occurrence construction allocated {allocated / 1048576d:F1} MiB");
        Assert.Equal(20, projected.Tokens.Count);
        Assert.Equal(20, client.ReaderOwner.Reader!.Diagnostics.LiveTokenModels);
        var first = projected.Tokens[0];
        Assert.Equal(32, first.Stored.Count);
        var display = Assert.Single(first.FieldWorksAnalyses);
        Assert.Equal(32, display.Morphs.Count);
        Assert.Equal("gloss31", display.Morphs[^1].Gloss);
        Assert.Null(first.WordLink);
        Assert.Equal(OccurrenceVerdict.NotAssessed, first.Verdict);
        Assert.Equal(ProjectStanding.Approved, first.Comparison.Standing);
        Assert.Equal(WordRowOutcome.NotParsed, first.Comparison.Outcome);
        Assert.Same(first.Comparison, first.Comparison);
        Assert.Same(first.Stored, first.Stored);
        Assert.Same(first.FieldWorksAnalyses, first.FieldWorksAnalyses);
        await owner.StopAsync();
        await words.StopAsync();
        await client.ReaderOwner.StopAsync();
    }
}
