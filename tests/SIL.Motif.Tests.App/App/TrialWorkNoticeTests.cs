using System.Globalization;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TrialWorkNoticeTests
{
    [Fact]
    public void TheNoteSaysTheTrialMakesParsingAboutTheWholePercentMoreWorkThanTheBaseline()
    {
        Assert.Equal("This Trial makes parsing about 26% more work than the Baseline.",
            TrialWorkNotice.For(TrialWorkAt(26.0)));
    }

    [Theory]
    [InlineData(20.0)]
    [InlineData(19.9)]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void TheNoteIsHiddenAtTwentyPercentAndBelow(double percentChange)
    {
        Assert.Null(TrialWorkNotice.For(TrialWorkAt(percentChange)));
    }

    [Fact]
    public void TheNoteUsesTheSerializedComparisonWithoutRawTotals()
    {
        var serialized = System.Text.Json.JsonSerializer.Serialize(TrialWorkAt(26.0));
        var restored = System.Text.Json.JsonSerializer.Deserialize<TrialWork>(serialized);
        Assert.Equal("This Trial makes parsing about 26% more work than the Baseline.", TrialWorkNotice.For(restored));
        Assert.Null(TrialWorkNotice.For(TrialWorkAt(null)));
    }

    [Fact]
    public void TheNoteIsHiddenWhenThereIsNoTrialWork()
    {
        Assert.Null(TrialWorkNotice.For(null));
    }

    [Theory]
    [InlineData(20.1, 21)]
    [InlineData(26.4, 27)]
    [InlineData(26.0, 26)]
    [InlineData(22.1, 23)]
    public void TheNoteRoundsThePercentToAWholeNumber(double percentChange, int shown)
    {
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TrialWorkNotice, shown),
            TrialWorkNotice.For(TrialWorkAt(percentChange)));
    }

    [Fact]
    public void TheRunStateCarriesTheNoteOfTheTrialItStored()
    {
        var shown = new TrialRunState("proposal/one", false, "job/one", null, null, null, TrialWorkAt(26.0));
        Assert.Equal("This Trial makes parsing about 26% more work than the Baseline.", shown.WorkNotice);

        var hidden = new TrialRunState("proposal/one", false, "job/one", null, null, null, TrialWorkAt(20.0));
        Assert.Null(hidden.WorkNotice);
        Assert.Null(TrialRunState.Idle("proposal/one").WorkNotice);
    }

    [Fact]
    public async Task TryAgainKeepsTheWorkNoticeOfTheLatestTrialForTheProposal()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync(@"C:\projects\one.fwdata");

        fake.EnqueueTrialCompletesWith(new JobEnqueuedResponse("job/one", "trial", "project/test"));
        fake.ReadTrialCompletesWith(TrialRecordWith(TrialWorkAt(26.0)));
        await context.TryAgainAsync("proposal/one", CancellationToken.None);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TrialWorkNotice, 26),
            context.TrialRun.StateFor("proposal/one").WorkNotice);

        fake.EnqueueTrialCompletesWith(new JobEnqueuedResponse("job/two", "trial", "project/test"));
        fake.ReadTrialCompletesWith(TrialRecordWith(TrialWorkAt(20.0)));
        await context.TryAgainAsync("proposal/one", CancellationToken.None);
        Assert.Null(context.TrialRun.StateFor("proposal/one").WorkNotice);
    }

    [Fact]
    public async Task ReviewNoticeFollowsTheProposalAndClearsForPendingChanges()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(@"C:\projects\one.fwdata");
        fake.ReadTrialCompletesWith(TrialRecordWith(TrialWorkAt(26.0)));
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        fake.EnqueueTrialCompletesWith(new JobEnqueuedResponse("job/one", "trial", "project/test"));
        await page.TryAgainCommand.ExecuteAsync(null);
        Assert.Equal("This Trial makes parsing about 26% more work than the Baseline.", page.WorkNotice);

        fake.ReadTrialCompletesWith(TrialRecordWith(TrialWorkAt(0.0)) with { ProposalId = "proposal/two" });
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        Assert.Null(page.WorkNotice);

        await context.LeaveProposalAsync();
        Assert.Null(page.WorkNotice);
    }

    [Fact]
    public async Task ReviewNoticeLoadsFromTheStoredTrialAndClearsWhenTheProjectChanges()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(@"C:\projects\one.fwdata");
        fake.ReadTrialCompletesWith(TrialRecordWith(TrialWorkAt(26.0)));

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        Assert.Equal("This Trial makes parsing about 26% more work than the Baseline.", page.WorkNotice);

        await context.OpenProjectAsync(@"C:\projects\two.fwdata");
        Assert.Null(page.WorkNotice);
    }

    private static TrialWork TrialWorkAt(
        double? percentChange) =>
        new(3, 1000, percentChange is null ? null : 1000 + (long)Math.Round(percentChange.Value * 10), percentChange, [], 0, 0);

    private static TrialRecord TrialRecordWith(TrialWork? work) =>
        new("job/trial", "proposal/one", "sha256:intent", PageScreenshots.Token(), null,
            new Dictionary<string, string>(), [], false, null, SIL.Motif.Contract.Jobs.JobStatus.Completed,
            DateTimeOffset.UtcNow, TrialFreshness.Current)
        {
            Work = work,
        };
}
