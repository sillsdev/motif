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
        Assert.Equal("Parsing · 3 of 5 words · about 4 s left", progress.StatusText);
        clock.Advance(TimeSpan.FromSeconds(12));
        progress.Report(Step(3));
        Assert.True(progress.IsStalled);
        Assert.Contains("about 4 s left", progress.TimeText);
        Assert.DoesNotContain("0 s", progress.StatusText, StringComparison.Ordinal);
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
        progress.Report(Step(5) with
        {
            SlowestWord = new SIL.Motif.Contract.Jobs.ParseWordTiming("brief", 2.5259),
        });
        Assert.Equal("Slowest so far: brief · 2.5259 ms", progress.SlowestText);
        Assert.Equal(0.98, progress.Fraction);
        Assert.Equal("All words have been parsed; finishing the Assessment...", progress.ProgressText);
        Assert.Equal("All words have been parsed; finishing the Assessment...", progress.StatusText);
        Assert.Contains("finishing the Assessment", progress.TimeText);
        Assert.DoesNotContain("0 s left", progress.TimeText);
        clock.Advance(TimeSpan.FromSeconds(12));
        progress.Report(Step(5));
        Assert.False(progress.IsStalled);
        progress.Report(new AssessmentProgress(AssessmentStage.ReadingStatistics, 0, null, "Reading statistics..."));
        Assert.Equal("All words have been parsed; Motif is reading PanGloss's statistics...", progress.ProgressText);
        Assert.Equal("All words have been parsed; Motif is reading PanGloss's statistics...", progress.StatusText);
        Assert.Contains("reading PanGloss's statistics", progress.TimeText);
        progress.Report(new AssessmentProgress(AssessmentStage.Complete, 5, 5, "Complete"));
        Assert.Equal(1, progress.Fraction);
        Assert.Equal("Assessment complete", progress.StatusText);
        Assert.Contains("complete", progress.TimeText);
        Assert.False(progress.IsStalled);
        finish.SetResult(true);
        await run;
    }

    private static AssessmentProgress Step(int completed) => new(AssessmentStage.Parsing, completed, 5, "Parsing")
        { CurrentWord = "word", PerWordLimitMs = 1000 };
}
