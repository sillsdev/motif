using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A store write that lands while Analyze Texts reads its presentation only makes that read stale; the page reads
/// again and shows its own empty-state sentence, not a failure.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnalyzeTextsNoTextsTests
{
    [Fact]
    public void AStoreWriteDuringThePresentationReadShowsNoFailure()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([], []));
            var writes = 0;
            var client = new FakeCommandClient
            {
                SelectionReaderHandler = (request, cancellationToken) => SelectionReader.OpenAsync(fixture.Database,
                    new SelectionReadRequest(fixture.ProjectKey, request.TextIds, request.AddedWords)
                    {
                        ProjectPath = fixture.ProjectPath,
                        PauseAt = point =>
                        {
                            if (point == SelectionReaderPausePoint.AfterPresentationStateLoad &&
                                Interlocked.Increment(ref writes) == 1)
                                fixture.CommitUnrelatedWrite();
                        },
                    }, cancellationToken),
            };
            var selection = new SelectionViewModel(client);
            var reads = new WorkspaceSelection(client);
            var words = new TextWordsViewModel(client, selection, reads);
            var assess = new AssessViewModel(client, selection);
            var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { }, new ChangesViewModel(client),
                client, reads);
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [], ["pasted"]);
                await owner.SelectionRefresh;
                Assert.Null(reads.Refusal);
                Assert.Null(owner.ReadStateRefusal);
                Assert.Null(words.ShownRefusal);
                Assert.True(Volatile.Read(ref writes) >= 2, "The presentation was not read again after the store write.");
                Assert.Empty(owner.Texts);
                Assert.False(owner.HasTexts);
            }
            finally
            {
                await owner.StopAsync();
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void ARefusalOfTheTextViewChangingHasItsOwnSentence()
    {
        var refusal = WindowRefusal.From(new Refusal(RefusalCodes.SelectionReaderPresentationChanged,
            FailureReason.Refused, "The presentation changed during the read."));

        Assert.Equal("The text view changed while it was loading. Try again.", refusal.Sentence);
        Assert.NotEqual(WindowRefusal.GenericSentence, refusal.Sentence);
    }

    [Fact]
    public void AnOlderRefreshThatIsRefusedAfterANewerOneSucceededLeavesThePageAlone()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([], []));
            using var reached = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            var paused = 0;
            var armed = 0;
            var staleEveryRead = 0;
            var client = new FakeCommandClient
            {
                SelectionReaderHandler = (request, cancellationToken) => SelectionReader.OpenAsync(fixture.Database,
                    new SelectionReadRequest(fixture.ProjectKey, request.TextIds, request.AddedWords)
                    {
                        ProjectPath = fixture.ProjectPath,
                        PauseAt = point =>
                        {
                            if (point != SelectionReaderPausePoint.AfterPresentationStateLoad ||
                                Volatile.Read(ref armed) == 0) return;
                            if (Volatile.Read(ref staleEveryRead) == 1) fixture.CommitUnrelatedWrite();
                            if (Interlocked.CompareExchange(ref paused, 1, 0) != 0) return;
                            reached.Set();
                            if (!resume.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("The read was not released.");
                        },
                    }, cancellationToken),
            };
            var selection = new SelectionViewModel(client);
            var reads = new WorkspaceSelection(client);
            var words = new TextWordsViewModel(client, selection, reads);
            var assess = new AssessViewModel(client, selection);
            var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { }, new ChangesViewModel(client),
                client, reads);
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [], ["pasted"]);
                await owner.SelectionRefresh;
                Volatile.Write(ref armed, 1);

                var older = owner.StartPresentationRefresh();
                Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(120))));
                await owner.StartPresentationRefresh();
                Assert.Null(owner.ReadStateRefusal);

                fixture.CommitUnrelatedWrite();
                Volatile.Write(ref staleEveryRead, 1);
                resume.Set();
                await older;

                Assert.Null(owner.ReadStateRefusal);
            }
            finally
            {
                resume.Set();
                await owner.StopAsync();
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(180));
    }
}
