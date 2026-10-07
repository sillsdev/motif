using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using ContractWordRow = SIL.Motif.Contract.Responses.WordRow;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordPresentationTests
{
    [Fact]
    public void RowUsesOneFrameAndReadsTypedCardOnlyAfterOpen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var key = new WordPresentationKey("entry:stable-id");
            var host = new RecordingHost();
            var row = new WordRow
            {
                Data = new WordPresentation(key, 7,
                    new WordRowViewModel(new ContractWordRow("morpheme", WordRowOutcome.Same, "Kept", WordRowTone.Fine)
                    {
                        FieldWorksMorphemes = Morphs("fw"),
                        PanGlossMorphemes = Morphs("pg"),
                    }),
                    WordListOwner.ReadOnly),
                State = new WordInteractionState(key),
                Host = host,
            };
            var window = new Window { Content = row, Width = 900, Height = 120 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.IsType<Border>(row.Content);
                Assert.IsType<Grid>(((Border)row.Content!).Child);
                Assert.Empty(window.GetVisualDescendants().OfType<WordCard>());
                Assert.Equal(0, host.ReadCount);
                var counts = AvaloniaScaleCounts.CaptureSubtree(row);
                AvaloniaScaleCounts.AssertSubtreeBudget(counts, "WordRow", 64, 10);

                row.State = row.State! with { IsOpen = true };
                await host.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Equal(1, host.ReadCount);
                Assert.Equal(key, host.LastReadKey);
                Assert.Equal(7, host.LastReadRevision);
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "Complete evidence");
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "No parse is available yet." && text.IsEffectivelyVisible);
                var card = Assert.Single(window.GetVisualDescendants().OfType<WordCard>());
                AvaloniaScaleCounts.AssertSubtreeBudget(
                    AvaloniaScaleCounts.CaptureSubtree(card), "WordCard", 64, 18);
                var tryWord = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Try morpheme in Try a Word");
                tryWord.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(new WordRequest(key, 7, WordAction.TryWord), host.LastRequest);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ClosingCancelsTheReadAndReopeningRejectsItsLateResult()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var key = new WordPresentationKey("entry:reopen-id");
            var host = new DeferredHost();
            var row = NewRow(key, host);
            var window = new Window { Content = row, Width = 700, Height = 180 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                row.State = row.State! with { IsOpen = true };
                var firstRead = host.Reads[0];
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var firstCard = Assert.Single(window.GetVisualDescendants().OfType<WordCard>());
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "Loading word details…" && text.IsEffectivelyVisible);

                row.State = row.State! with { IsOpen = false };
                Assert.True(firstRead.CancellationToken.IsCancellationRequested);
                Assert.Empty(window.GetVisualDescendants().OfType<WordCard>());

                row.State = row.State! with { IsOpen = true };
                var reopenedRead = host.Reads[1];
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(key, reopenedRead.Key);
                Assert.Equal(7, reopenedRead.EvidenceRevision);
                Assert.NotSame(firstCard, Assert.Single(window.GetVisualDescendants().OfType<WordCard>()));
                firstRead.Complete(key, 7, "Stale closed evidence");
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "Stale closed evidence");

                reopenedRead.Complete(key, 7, "Reopened evidence");
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "Reopened evidence" && text.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void RecycledRowCancelsAndRejectsEvidenceForItsPreviousIdentity()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var oldKey = new WordPresentationKey("entry:old-recycled-id");
            var newKey = new WordPresentationKey("entry:new-recycled-id");
            var host = new DeferredHost();
            var row = NewRow(oldKey, host);
            var window = new Window { Content = row, Width = 700, Height = 180 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                row.State = row.State! with { IsOpen = true };
                var oldRead = host.Reads[0];

                row.Data = Presentation(newKey);
                Assert.True(oldRead.CancellationToken.IsCancellationRequested);
                Assert.Equal(newKey, row.State!.Key);
                Assert.False(row.State.IsOpen);
                row.State = row.State with { IsOpen = true };
                var newRead = host.Reads[1];
                Assert.Equal(newKey, newRead.Key);
                Assert.Equal(7, newRead.EvidenceRevision);
                oldRead.Complete(oldKey, 7, "Previous identity evidence");
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "Previous identity evidence");

                newRead.Complete(newKey, 7, "Current identity evidence");
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "Current identity evidence" && text.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(15));
    }

    private static WordRow NewRow(WordPresentationKey key, IWordPresentationHost host) => new()
    {
        Data = Presentation(key),
        State = new WordInteractionState(key),
        Host = host,
    };

    private static WordPresentation Presentation(WordPresentationKey key) => new(key, 7,
        new WordRowViewModel(new ContractWordRow("morpheme", WordRowOutcome.Same, "Kept", WordRowTone.Fine)),
        WordListOwner.ReadOnly);

    [Fact]
    public void AFailedCardReadCanBeRetriedByItsTypedOwner()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var key = new WordPresentationKey("entry:retry-id");
            var host = new RetryHost();
            var row = new WordRow
            {
                Data = new WordPresentation(key, 3,
                    new WordRowViewModel(new ContractWordRow("retry", WordRowOutcome.Same, "Kept", WordRowTone.Fine)),
                    WordListOwner.ReadOnly),
                State = new WordInteractionState(key),
                Host = host,
            };
            var window = new Window { Content = row, Width = 700, Height = 180 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                row.State = row.State! with { IsOpen = true };
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Equal(1, host.ReadCount);
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "Read failed.");
                var retry = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Try loading word details again");
                retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Equal(2, host.ReadCount);
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "Recovered evidence" && text.IsEffectivelyVisible);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Try loading word details again");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(15));
    }

    private static ParserReadingMorph[] Morphs(string prefix) => Enumerable.Range(1, 3)
        .Select(index => new ParserReadingMorph($"{prefix}{index}", $"gloss{index}", "n", null, false, null))
        .ToArray();

    private sealed class RecordingHost : IWordPresentationHost
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount { get; private set; }

        public WordPresentationKey LastReadKey { get; private set; }

        public long LastReadRevision { get; private set; }

        public WordRequest? LastRequest { get; private set; }

        public Task<WordCardReadResult> ReadCardAsync(
            WordPresentationKey key,
            long evidenceRevision,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            LastReadKey = key;
            LastReadRevision = evidenceRevision;
            ReadStarted.TrySetResult();
            return Task.FromResult(WordCardReadResult.Read(new WordCardDocument(key, evidenceRevision,
                [new WordCardAnalysis(null, null, "No parse is available yet."),
                    new WordCardHeading("Evidence"), new WordCardText("Source", "Complete evidence")])));
        }

        public ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return ValueTask.FromResult(new WordActionResult(true));
        }
    }

    private sealed class RetryHost : IWordPresentationHost
    {
        public int ReadCount { get; private set; }

        public Task<WordCardReadResult> ReadCardAsync(
            WordPresentationKey key,
            long evidenceRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(ReadCount == 1
                ? WordCardReadResult.Failed("Read failed.")
                : WordCardReadResult.Read(new WordCardDocument(key, evidenceRevision,
                    [new WordCardText("Recovered", "Recovered evidence")])));
        }

        public ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WordActionResult(true));
    }

    private sealed class DeferredHost : IWordPresentationHost
    {
        public List<PendingRead> Reads { get; } = [];

        public Task<WordCardReadResult> ReadCardAsync(
            WordPresentationKey key,
            long evidenceRevision,
            CancellationToken cancellationToken)
        {
            var read = new PendingRead(key, evidenceRevision, cancellationToken);
            Reads.Add(read);
            return read.Result.Task;
        }

        public ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WordActionResult(true));
    }

    private sealed class PendingRead(
        WordPresentationKey key,
        long evidenceRevision,
        CancellationToken cancellationToken)
    {
        public WordPresentationKey Key { get; } = key;
        public long EvidenceRevision { get; } = evidenceRevision;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource<WordCardReadResult> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(WordPresentationKey responseKey, long responseRevision, string text) =>
            Result.TrySetResult(WordCardReadResult.Read(new WordCardDocument(responseKey, responseRevision,
                [new WordCardText("Evidence", text)])));
    }
}
