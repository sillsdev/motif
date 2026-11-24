using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class WorkspaceSelectionTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.WorkspaceSelectionTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReplacementSharesCompactCountsAndDisposesThePreviousReader()
    {
        var (path, textId) = Capture();
        var fake = new FakeCommandClient
        {
            SelectionReaderHandler = SelectionReader.OpenAsync,
        };
        var selection = new WorkspaceSelection(fake);
        try
        {
            await selection.ReloadAsync(path, [textId], []);
            var first = Assert.IsType<SelectionReader>(selection.Reader);
            var summary = Assert.IsType<SelectionSummary>(selection.Summary);
            Assert.Single(summary.Texts);
            Assert.True(summary.PhysicalOccurrenceCount > 0);
            Assert.True(selection.Navigation!.ProjectMatches);
            Assert.NotNull(selection.Navigation.LinkFor(new("analyses",
                summary.SourcePositions[0].Word.WordformId!.Value)));
            Assert.Equal(0, first.Diagnostics.TextRowsDeserialized);
            Assert.Equal(0, first.Diagnostics.WordformRowsDeserialized);
            Assert.Equal(0, first.Diagnostics.LiveLineModels);
            Assert.Equal(0, first.Diagnostics.LiveTokenModels);
            var replacements = 0;
            selection.Replacing += () => replacements++;

            await selection.ReloadAsync(path, [], ["new word"]);

            Assert.True(first.Diagnostics.IsDisposed);
            Assert.NotSame(first, selection.Reader);
            Assert.Equal(1, replacements);
            Assert.Empty(selection.Summary!.Texts);
            Assert.Equal("new word", Assert.Single(selection.Summary.Words).Key.Form);
        }
        finally
        {
            var final = selection.Reader;
            await selection.StopAsync();
            Assert.Null(selection.Reader);
            Assert.Null(selection.Summary);
            Assert.Null(selection.Navigation);
            Assert.True(final!.Diagnostics.IsDisposed);
        }
    }

    [Fact]
    public async Task StopWaitsForAnIgnoringOpenAndDisposesItsLateReaderWithoutPublishingIt()
    {
        var (path, textId) = Capture();
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(path, [textId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        var lateReader = opened.Value!;
        var completion = new TaskCompletionSource<CommandOutcome<SelectionReader>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeCommandClient { SelectionReaderHandler = (_, _) => completion.Task };
        var selection = new WorkspaceSelection(fake);
        var textIds = new List<Guid> { textId };
        var words = new List<string> { "first" };
        var loading = selection.ReloadAsync(path, textIds, words);
        textIds.Clear();
        words[0] = "changed";
        Assert.Equal([textId], Assert.Single(fake.OpenSelectionReaderRequests).TextIds);
        Assert.Equal(["first"], Assert.Single(fake.OpenSelectionReaderRequests).AddedWords);
        var stopping = selection.StopAsync();
        try
        {
            Assert.False(stopping.IsCompleted);
        }
        finally
        {
            completion.SetResult(opened);
            await Task.WhenAll(loading, stopping);
        }
        Assert.Null(selection.Reader);
        Assert.Null(selection.Summary);
        Assert.True(lateReader.Diagnostics.IsDisposed);
    }

    [Fact]
    public async Task RefusedReplacementClearsTheOldCountsAndRetainsItsTypedReason()
    {
        var (path, textId) = Capture();
        var fake = new FakeCommandClient { SelectionReaderHandler = SelectionReader.OpenAsync };
        var selection = new WorkspaceSelection(fake);
        try
        {
            await selection.ReloadAsync(path, [textId], []);
            var previous = selection.Reader!;
            var refusal = new Refusal("selection-reader.obsolete-context", FailureReason.Refused, "Refresh first.");
            fake.SelectionReaderHandler = (_, _) => Task.FromResult(CommandOutcome<SelectionReader>.Refused(refusal));
            await selection.ReloadAsync(path, [], []);
            Assert.True(previous.Diagnostics.IsDisposed);
            Assert.Null(selection.Reader);
            Assert.Null(selection.Summary);
            Assert.Same(refusal, selection.Refusal);
        }
        finally
        {
            await selection.StopAsync();
        }
    }

    private (string Path, Guid TextId) Capture()
    {
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Path.Combine(_root, "managed"));
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        return (path, text.TextId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
