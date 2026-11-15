using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewPageWordsTests
{
    public OverviewPageWordsTests() => AvaloniaHeadlessPlatform.Initialize();

    [Fact]
    public void HealthShowsTheRecordedWarningCountIncludingZero()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var overview = SampleOverview.Populated();
        var health = new HealthStripViewModel(context)
        {
            Overview = overview with { Warnings = overview.Warnings! with { Count = 0 } },
        };

        Assert.True(health.HasWarnings);
        Assert.Contains("0", health.WarningText);
        Assert.Contains(health.WarningText, health.AccessibleName);
    }

    [Fact]
    public void RecordedTimingFixtureKeepsStoppedWordCountsConsistent()
    {
        SampleOverview.AssertCaptureStopCounts(SampleOverview.Populated(), "progress");
    }
}
