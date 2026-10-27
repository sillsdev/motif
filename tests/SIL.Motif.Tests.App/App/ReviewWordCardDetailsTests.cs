using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewWordCardDetailsTests
{
    [Fact]
    public void CompletedCardDetailsOwnOneTokenAndReleaseItWithTheCardLifetime()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            await using var fixture = new SelectionModelFixture(fake);
            var assessment = PageScreenshots.Assessment();
            await fixture.PublishAsync(PageScreenshots.TextWords(), assessment);
            var reader = fixture.Reads.Reader!;
            var word = fixture.Reads.Summary!.Words.First(item => item.Assessment is { Morphology.Analyses.Count: > 0 } &&
                item.Actions.CandidateWordformIds.Count == 1 && item.Actions.StoredAnalysisIds.Count > 0);
            var target = new WordActionTarget(word.Key.Form, word.Actions.CandidateWordformIds[0],
                word.FirstOccurrence?.Anchor, word.Assessment!.AssessmentId, reader.Context.ExpectedWriteContext());
            using var closed = new CancellationTokenSource();
            var source = new ReviewWordCardDetails(reader, word, [], target, "Fixture");
            var before = reader.Diagnostics;
            try
            {
                Assert.True(await source.OpenAsync(closed.Token), source.Failure);
                var token = Assert.IsType<ResultsTokenViewModel>(source.Token);
                var captured = token.CaptureActionTarget(null);
                Assert.Equal(target.Form, captured.Form);
                Assert.Equal(target.WordformId, captured.WordformId);
                Assert.Equal(target.Occurrence, captured.Occurrence);
                Assert.Equal(target.AssessmentId, captured.AssessmentId);
                Assert.Equal(target.ExpectedContext, captured.ExpectedContext);
                Assert.NotEmpty(token.Readings);
                Assert.NotEmpty(token.FieldWorksAnalyses);
                Assert.Equal(before.LiveCardModels + 1, reader.Diagnostics.LiveCardModels);
                Assert.Equal(before.LiveTokenModels + 1, reader.Diagnostics.LiveTokenModels);
                Assert.Equal(before.LivePinnedModels + 1, reader.Diagnostics.LivePinnedModels);
                Assert.Equal(before.LeasedResults + 1, reader.Diagnostics.LeasedResults);
                Assert.Equal(before.LiveLineModels, reader.Diagnostics.LiveLineModels);
                closed.Cancel();
                Assert.Null(source.Token);
                Assert.Equal(before.LiveCardModels, reader.Diagnostics.LiveCardModels);
                Assert.Equal(before.LiveTokenModels, reader.Diagnostics.LiveTokenModels);
                Assert.Equal(before.LivePinnedModels, reader.Diagnostics.LivePinnedModels);
                Assert.Equal(before.LeasedResults, reader.Diagnostics.LeasedResults);
            }
            finally { await source.StopAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReleasedCardDrainsItsHeldReadWithoutPublishingALateToken(bool cancelCard)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var resume = new ManualResetEventSlim();
            var armed = false;
            var fake = new FakeCommandClient();
            await using var fixture = new SelectionModelFixture(fake)
            {
                PauseAt = point =>
                {
                    if (point != SelectionReaderPausePoint.BeforeCachePublication || !Volatile.Read(ref armed)) return;
                    reached.TrySetResult();
                    resume.Wait();
                },
            };
            var assessment = PageScreenshots.Assessment();
            await fixture.PublishAsync(PageScreenshots.TextWords(), assessment);
            var reader = fixture.Reads.Reader!;
            var word = fixture.Reads.Summary!.Words.First(item => item.Assessment is { Morphology.Analyses.Count: > 0 } &&
                item.Actions.CandidateWordformIds.Count == 1 && item.Actions.StoredAnalysisIds.Count > 0);
            var target = new WordActionTarget(word.Key.Form, word.Actions.CandidateWordformIds[0],
                word.FirstOccurrence?.Anchor, word.Assessment!.AssessmentId, reader.Context.ExpectedWriteContext());
            using var closed = new CancellationTokenSource();
            var source = new ReviewWordCardDetails(reader, word, [], target, "Fixture");
            var before = reader.Diagnostics;
            Volatile.Write(ref armed, true);
            var pending = source.OpenAsync(closed.Token);
            try
            {
                Assert.Same(reached.Task, await Task.WhenAny(reached.Task, pending));
                if (cancelCard) closed.Cancel();
                else source.Release();
                var stopping = source.StopAsync();
                Assert.False(stopping.IsCompleted);
                resume.Set();
                await stopping;
                Assert.False(await pending);
                Assert.Null(source.Token);
                Assert.Equal(before.LiveCardModels, reader.Diagnostics.LiveCardModels);
                Assert.Equal(before.LiveTokenModels, reader.Diagnostics.LiveTokenModels);
                Assert.Equal(before.LivePinnedModels, reader.Diagnostics.LivePinnedModels);
                Assert.Equal(before.LeasedResults, reader.Diagnostics.LeasedResults);
                Assert.Equal(0, reader.Diagnostics.PendingReads);
            }
            finally
            {
                resume.Set();
                await source.StopAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
