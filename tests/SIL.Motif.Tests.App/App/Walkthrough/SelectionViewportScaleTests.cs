using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SelectionViewportScaleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 3000, 40, 9760)]
    [InlineData(true, 21604, 129, 76761)]
    public async Task SequentialRangesKeepActualDisplayModelsAndReaderReadsBounded(
        bool representative, int wordRows, int textCount, int occurrenceCount)
    {
        using var project = new LargeProjectFixture(representative);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        using var reads = ScaleCountHarness.ObserveRepositoryReads();
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(project.FwDataPath, project.TextIds, []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        Assert.Equal(textCount, reader.Diagnostics.IndexRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.Morphs);
        Assert.Equal(0, reader.Diagnostics.LiveLineModels);
        Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
        Assert.InRange(reader.Diagnostics.QueriesIssued, 1, 8 + (textCount + 255) / 256 + (wordRows + 511) / 512);
        var summaryOutcome = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summaryOutcome.Succeeded, summaryOutcome.Refusal?.Message);
        using var summary = summaryOutcome.Value!;
        Assert.Equal(wordRows, summary.Value.WordRowCount);
        Assert.Equal(textCount, summary.Value.Texts.Count);
        Assert.Equal(occurrenceCount, summary.Value.PhysicalOccurrenceCount);
        var viewport = new SelectionTextViewport();
        var weakModels = new List<WeakReference<object>>();
        var sourceTokens = 0;
        var distinctAnchors = new HashSet<SIL.Motif.Contract.Requests.OccurrenceAnchor>();
        var ranges = 0;
        try
        {
            foreach (var text in summary.Value.Texts)
            {
                var position = new TextLineRange();
                while (true)
                {
                    var before = reader.Diagnostics;
                    await viewport.ShowAsync(reader, text, position);
                    Assert.Null(viewport.Refusal);
                    var slice = Assert.IsType<TextLineSlice>(viewport.Slice);
                    var returnedWordforms = slice.Wordforms.Values.Select(word => word.WordformId).Distinct().Count();
                    Assert.InRange(reader.Diagnostics.QueriesIssued - before.QueriesIssued, 0,
                        5 + (returnedWordforms + 255) / 256);
                    Assert.InRange(reader.Diagnostics.TextRowsDeserialized - before.TextRowsDeserialized, 0, 1);
                    Assert.Equal(viewport.Lines.Count, reader.Diagnostics.LiveLineModels);
                    Assert.Equal(slice.ReturnedTokens, reader.Diagnostics.LiveTokenModels);
                    Assert.InRange(reader.Diagnostics.LiveLineModels, 0, 32);
                    Assert.InRange(reader.Diagnostics.LiveTokenModels, 0, 512);
                    Assert.InRange(reader.Diagnostics.CachedTextGraphs, 0, 1);
                    Assert.InRange(reader.Diagnostics.CachedLineRanges, 0, 3);
                    Assert.InRange(reader.Diagnostics.CachedWordDetails, 0, 32);
                    Assert.InRange(reader.Diagnostics.LeasedLineRanges, 0, 1);
                    Assert.InRange(reader.Diagnostics.PendingReads, 0, 2);
                    sourceTokens += slice.ReturnedTokens;
                    foreach (var token in viewport.Lines.SelectMany(line => line.Tokens).Where(token => token.IsWord))
                        Assert.True(distinctAnchors.Add(Assert.IsType<SIL.Motif.Contract.Requests.OccurrenceAnchor>(token.Occurrence)));
                    weakModels.AddRange(ScaleCountHarness.ObserveViewModels(viewport.Lines.Cast<object>()
                        .Concat(viewport.Lines.SelectMany(line => line.Tokens)),
                        typeof(ResultsLineViewModel), typeof(ResultsTokenViewModel)));
                    var sameRangeCounts = reader.Diagnostics;
                    await viewport.ShowAsync(reader, text, position);
                    Assert.Equal(sameRangeCounts.CreatedLineModels, reader.Diagnostics.CreatedLineModels);
                    Assert.Equal(sameRangeCounts.CreatedTokenModels, reader.Diagnostics.CreatedTokenModels);
                    Assert.Equal(sameRangeCounts.QueriesIssued, reader.Diagnostics.QueriesIssued);
                    ranges++;
                    if (slice.NextLine is not { } next) break;
                    position = new TextLineRange(next, TokenOffset: slice.NextTokenOffset ?? 0);
                }
            }
            Assert.Equal(occurrenceCount, distinctAnchors.Count);
            Assert.Equal(occurrenceCount, sourceTokens);
            Assert.Equal(textCount, reader.Diagnostics.TextRowsDeserialized);
            await viewport.ShowAsync(reader, summary.Value.Texts[0], new TextLineRange());
            Assert.Equal(textCount + 1, reader.Diagnostics.TextRowsDeserialized);
            await viewport.StopAsync();
            Assert.Equal(0, reader.Diagnostics.LiveLineModels);
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
            Assert.Equal(0, reader.Diagnostics.LeasedLineRanges);
            ScaleCountHarness.AssertLiveViewModelBudget(weakModels, typeof(ResultsLineViewModel), 0, 0);
            ScaleCountHarness.AssertLiveViewModelBudget(weakModels, typeof(ResultsTokenViewModel), 0, 0);
            Assert.Equal(reads.Snapshot().Queries, reader.Diagnostics.QueriesIssued);
            Assert.Equal(reads.Snapshot().RecordsDeserialized, reader.Diagnostics.RepositoryRecordsDeserialized);
            output.WriteLine($"SCALE Selection viewport: {wordRows} rows, {textCount} Texts, {distinctAnchors.Count} occurrences; " +
                $"{ranges} sequential ranges; {reader.Diagnostics.QueriesIssued} queries; " +
                $"{reader.Diagnostics.TextRowsDeserialized} Text rows; {reader.Diagnostics.CreatedLineModels} line models; " +
                $"{reader.Diagnostics.CreatedTokenModels} token models, all released.");
        }
        finally
        {
            await viewport.StopAsync();
        }
    }
}
