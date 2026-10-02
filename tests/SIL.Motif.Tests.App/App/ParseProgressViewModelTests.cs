using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ParseProgressViewModelTests
{
    [Fact]
    public async Task EstimateUsesCompletedWordsAndAStalledWordDoesNotInflateTheMeasuredRate()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var progress = new ParseProgressViewModel(clock);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = progress.TrackAsync(() => finish.Task);
        progress.Report(Step(0));
        clock.Advance(TimeSpan.FromSeconds(2));
        progress.Report(Step(1));
        Assert.Contains("estimating", progress.TimeText);
        clock.Advance(TimeSpan.FromSeconds(4));
        progress.Report(Step(3));
        Assert.Contains("about 4 s left", progress.TimeText);
        clock.Advance(TimeSpan.FromSeconds(12));
        progress.Report(Step(3));
        Assert.True(progress.IsStalled);
        Assert.Contains("about 4 s left", progress.TimeText);
        progress.Report(Step(4));
        Assert.False(progress.IsStalled);
        finish.SetResult(true);
        await run;
        Assert.False(progress.IsActive);
    }

    [Fact]
    public async Task FinishingWordsAndReadingStatisticsNeverShowsANoProgressWarning()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var progress = new ParseProgressViewModel(clock);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = progress.TrackAsync(() => finish.Task);
        progress.Report(Step(5));
        clock.Advance(TimeSpan.FromSeconds(12));
        progress.Report(Step(5));
        Assert.False(progress.IsStalled);
        progress.Report(new AssessmentProgress(AssessmentStage.ReadingStatistics, 0, null, "Reading statistics..."));
        Assert.False(progress.IsStalled);
        finish.SetResult(true);
        await run;
    }

    private static AssessmentProgress Step(int completed) => new(AssessmentStage.Parsing, completed, 5, "Parsing")
        { CurrentWord = "word", PerWordLimitMs = 1000 };
}
