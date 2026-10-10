using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextWordsReloadTests
{
    private static readonly Guid FirstTextId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondTextId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static StoredSelectionFixture Records() => StoredSelectionFixture.FromDisplayRecords(new TextWordsResponse([], [
        new TextLines(FirstTextId, "Alpha", [new TextLine(1, [new TextToken("alpha", "alpha", null, "unanalysed")
            { WordformId = Guid.NewGuid() }])]),
        new TextLines(SecondTextId, "Beta", [new TextLine(1, [new TextToken("beta", "beta", null, "unanalysed")
            { WordformId = Guid.NewGuid() }])]),
    ], true));

    [Fact]
    public void ThreeQuickSelectionChangesCancelSupersededReadersAndDisposeTheirLateAnswers()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var fixture = Records();
            var first = new TaskCompletionSource<CommandOutcome<SelectionReader>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<CommandOutcome<SelectionReader>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new FakeCommandClient();
            var reads = client.ReaderOwner;
            var words = new TextWordsViewModel(client, new SelectionViewModel(client), reads);
            client.SelectionReaderHandler = (request, cancellation) =>
            {
                if (request.TextIds.SequenceEqual([FirstTextId]))
                {
                    cancellation.Register(() => firstCancelled.TrySetResult());
                    return first.Task;
                }
                if (request.TextIds.SequenceEqual([FirstTextId, SecondTextId]))
                {
                    cancellation.Register(() => secondCancelled.TrySetResult());
                    return second.Task;
                }
                return fixture.OpenAsync(request, cancellation);
            };
            try
            {
                await words.SetProjectAsync(fixture.ProjectPath);
                var firstLoad = reads.ReloadAsync(fixture.ProjectPath, [FirstTextId], []);
                Assert.True(words.IsLoading);
                var secondLoad = reads.ReloadAsync(fixture.ProjectPath, [FirstTextId, SecondTextId], []);
                await firstCancelled.Task;
                await reads.ReloadAsync(fixture.ProjectPath, [SecondTextId], []);
                await secondCancelled.Task;
                Assert.False(words.IsLoading);
                Assert.Equal("beta", Assert.Single(words.Rows).Form);
                var lateFirst = await fixture.OpenAsync(client.OpenSelectionReaderRequests[0]);
                var lateSecond = await fixture.OpenAsync(client.OpenSelectionReaderRequests[1]);
                var drain = reads.StopAsync();
                Assert.False(drain.IsCompleted);
                first.SetResult(lateFirst);
                second.SetResult(lateSecond);
                await Task.WhenAll(firstLoad, secondLoad, drain);
                Assert.True(lateFirst.Value!.Diagnostics.IsDisposed);
                Assert.True(lateSecond.Value!.Diagnostics.IsDisposed);
                Assert.Empty(words.Rows);

            }
            finally
            {
                var cancelled = CommandOutcome<SelectionReader>.Refused(new Refusal(
                    "selection-reader.cancelled", FailureReason.Cancelled, "Cancelled test reader."));
                first.TrySetResult(cancelled);
                second.TrySetResult(cancelled);
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedReaderClearsThePreviousSummaryAndPresentsItsRefusal(bool throws)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var fixture = Records();
            var client = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var selection = new SelectionViewModel(client);
            client.ListTextsCompletesWith(new TextInventoryResponse(
                [new TextChoiceSummary(FirstTextId, "Alpha"), new TextChoiceSummary(SecondTextId, "Beta")], true));
            await selection.SetProjectAsync(fixture.ProjectPath);
            var reads = client.ReaderOwner;
            var words = new TextWordsViewModel(client, selection, reads);
            try
            {
                await words.SetProjectAsync(fixture.ProjectPath);
                await reads.ReloadAsync(fixture.ProjectPath, [FirstTextId], []);
                Assert.Equal("alpha", Assert.Single(words.Rows).Form);
                Assert.NotNull(selection.Texts[0].CountsText);
                var refusal = new Refusal("store.inconsistent", FailureReason.StoreInconsistent, "stored words are damaged");
                client.SelectionReaderHandler = (_, _) => throws
                    ? Task.FromException<CommandOutcome<SelectionReader>>(new IOException("store unavailable"))
                    : Task.FromResult(CommandOutcome<SelectionReader>.Refused(refusal));
                var exception = await Record.ExceptionAsync(() => reads.ReloadAsync(fixture.ProjectPath, [SecondTextId], []));
                Assert.Null(exception);
                Assert.Empty(words.Rows);
                Assert.Null(reads.Reader);
                Assert.Null(reads.Summary);
                Assert.Equal(0, words.WordCount);
                Assert.Equal(0, words.OccurrenceCount);
                Assert.All(selection.Texts, text => Assert.True(string.IsNullOrEmpty(text.CountsText)));
                Assert.False(words.IsLoading);
                Assert.Equal(throws ? "store unavailable" : refusal.Message, words.Refusal?.Message);
            }
            finally
            {
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }
}
