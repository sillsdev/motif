using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class SelectionTextViewportTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.SelectionTextViewportTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DisplayedModelsBelongToTheirRangeAndRepeatedLayoutKeepsTheSameModels()
    {
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Path.Combine(_root, "managed"));
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(path, [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var summaryResult = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summaryResult.Succeeded, summaryResult.Refusal?.Message);
        using var summary = summaryResult.Value!;
        var header = Assert.Single(summary.Value.Texts);
        var viewport = new SelectionTextViewport();
        try
        {
            await viewport.ShowAsync(reader, header, new TextLineRange());
            Assert.Null(viewport.Refusal);
            Assert.NotEmpty(viewport.Lines);
            Assert.Equal(viewport.Lines.Count, reader.Diagnostics.LiveLineModels);
            Assert.Equal(viewport.Lines.Sum(line => line.Tokens.Count), reader.Diagnostics.LiveTokenModels);
            Assert.Equal(viewport.Slice!.ReturnedTokens, reader.Diagnostics.LiveTokenModels);
            Assert.Equal(1, reader.Diagnostics.TextRowsDeserialized);
            var line = viewport.Lines.First(item => item.Tokens.Count > 1);
            Assert.Same(line.Tokens[0].Location, line.Tokens[1].Location);
            var fake = new FakeCommandClient();
            var changes = new ChangesViewModel(fake) { AssessmentId = "newer-global-assessment" };
            await changes.OpenProjectAsync(path);
            var word = viewport.Lines.SelectMany(item => item.Tokens).First(item => item.IsWord);
            Assert.True(await changes.AddFromTextAsync(ChangeKinds.IncorrectSpelling, word));
            var written = Assert.Single(fake.PendingPutRequests);
            Assert.Equal(reader.Context.ExpectedWriteContext(), written.ExpectedContext);
            Assert.Null(written.Change.AssessmentId);
            Assert.Equal(SIL.Motif.Contract.Ids.CanonicalId.FromGuid(word.WordformId!.Value).Value,
                written.Change.WordformId);
            var before = reader.Diagnostics;
            var models = viewport.Lines;

            await viewport.ShowAsync(reader, header, new TextLineRange());

            Assert.Same(models, viewport.Lines);
            Assert.Equal(before.CreatedLineModels, reader.Diagnostics.CreatedLineModels);
            Assert.Equal(before.CreatedTokenModels, reader.Diagnostics.CreatedTokenModels);
            Assert.Equal(before.QueriesIssued, reader.Diagnostics.QueriesIssued);
            Assert.Equal(before.RepositoryRecordsDeserialized, reader.Diagnostics.RepositoryRecordsDeserialized);
            var references = Observe(viewport);
            viewport.Clear();
            Assert.Equal(0, reader.Diagnostics.LiveLineModels);
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
            Assert.Equal(0, reader.Diagnostics.LeasedLineRanges);
            Assert.Empty(viewport.Lines);
            Assert.Null(viewport.Context);
            Assert.NotEmpty(references);
        }
        finally
        {
            await viewport.StopAsync();
        }
    }

    [Fact]
    public async Task HidingARangeReleasesTheActualModelsEvenAfterReaderLeaseInvalidation()
    {
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Path.Combine(_root, "managed"));
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(path, [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        var reader = opened.Value!;
        var summaryResult = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summaryResult.Succeeded, summaryResult.Refusal?.Message);
        using var summary = summaryResult.Value!;
        var viewport = new SelectionTextViewport();
        try
        {
            await viewport.ShowAsync(reader, Assert.Single(summary.Value.Texts), new TextLineRange());
            // Resumed inline inside ShowCoreAsync's frame, whose model temporaries Debug still reports as roots.
            await Task.Yield();
            var references = Observe(viewport);
            await reader.DisposeAsync();
            Assert.True(reader.Diagnostics.LiveLineModels > 0);
            Assert.True(reader.Diagnostics.LiveTokenModels > 0);

            await viewport.StopAsync();

            Assert.Equal(0, reader.Diagnostics.LiveLineModels);
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
            Assert.Equal(0, reader.Diagnostics.CachedEntries);
            Assert.Equal(0, reader.Diagnostics.LeasedResults);
            ScaleCountHarness.AssertLiveViewModelBudget(references, typeof(ResultsLineViewModel), 0, 0);
            ScaleCountHarness.AssertLiveViewModelBudget(references, typeof(ResultsTokenViewModel), 0, 0);
        }
        finally
        {
            await viewport.StopAsync();
            await reader.DisposeAsync();
        }
    }

    private static IReadOnlyList<WeakReference<object>> Observe(SelectionTextViewport viewport) =>
        ScaleCountHarness.ObserveViewModels(viewport.Lines.Cast<object>()
            .Concat(viewport.Lines.SelectMany(line => line.Tokens)),
            typeof(ResultsLineViewModel), typeof(ResultsTokenViewModel));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
