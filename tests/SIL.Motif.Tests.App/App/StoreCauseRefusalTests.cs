using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class StoreCauseRefusalTests
{
    public StoreCauseRefusalTests() => AvaloniaHeadlessPlatform.Initialize();

    private static Refusal MissingLock() => new(RefusalCodes.ProjectStoreIo, FailureReason.Refused,
        "The project could not access required storage.", new Dictionary<string, string>
        {
            ["fwDataPath"] = "/p/ceb.fwdata",
            [RefusalFactNames.StoreLockMissing] = "/p/ceb.motif.db.use.lock",
        });

    private static Refusal OtherProject() => new(RefusalCodes.StoreOtherProject, FailureReason.Refused,
        "belongs to another project", new Dictionary<string, string>
        {
            ["fwDataPath"] = "/p/ceb.fwdata",
            [RefusalFactNames.StorePath] = "/p/ceb.motif.db",
            [RefusalFactNames.RegisteredProjectPath] = "/old/ceb.fwdata",
        });

    [Fact]
    public void AMissingUseLockSaysSoAndWhatToDoInsteadOfCheckTheDisk()
    {
        var sentence = WindowRefusal.From(MissingLock()).Sentence;
        Assert.Contains(".use.lock", sentence);
        Assert.Contains("copied or moved", sentence);
        Assert.DoesNotContain("Check the disk", sentence);
        Assert.Equal(MotifAppStrings.RefusalStoreLockMissing, sentence);
    }

    [Fact]
    public void AStoreFromAnotherFolderSaysItWasCopiedOrMovedAndNamesTheFix()
    {
        var sentence = WindowRefusal.From(OtherProject()).Sentence;
        Assert.Contains("copied or moved", sentence);
        Assert.Contains("move the project's .motif.db file aside", sentence);
        Assert.DoesNotContain("Check the disk", sentence);
    }

    [Fact]
    public void AnOrdinaryDiskFailureStillSaysCheckTheDisk()
    {
        var plain = new Refusal(RefusalCodes.ProjectStoreIo, FailureReason.Refused, "x");
        Assert.Contains("Check that the folder can be written to", WindowRefusal.From(plain).Sentence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProgressNextNamesTheStoreCauseAndOffersNoTryAgain(bool missingLock)
    {
        var fake = new FakeCommandClient();
        var refusal = missingLock ? MissingLock() : OtherProject();
        fake.OnProposalStandings((_, _) =>
            Task.FromResult(CommandOutcome<ProposalStandings>.Refused(refusal)));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.True(page.HasUnavailableNext);
            Assert.Contains(WindowRefusal.From(refusal).Sentence, page.UnavailableNextText);
            Assert.False(page.CanOfferTryAgain);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public void ProgressRendersTheCauseAndHidesTryAgainForAStoreProblem()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnProposalStandings((_, _) =>
                Task.FromResult(CommandOutcome<ProposalStandings>.Refused(MissingLock())));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            var page = new ProgressPage(model);
            var window = new MotifTestWindow { Content = page, Width = 900, Height = 600 };
            try
            {
                window.Show();
                await context.OpenProjectAsync("project.fwdata");
                PageScreenshots.Settle(window);
                var shown = page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? string.Empty);
                Assert.Contains(shown, text => text.Contains(".use.lock", StringComparison.Ordinal));
                var retries = page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Content as string == MotifAppStrings.TryAgain);
                Assert.DoesNotContain(retries, retry => retry.IsEffectivelyVisible);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProgressOffersExactlyOneTryAgainAndNoneWhenAStoreCauseOnTheOverviewMakesRetryUseless(bool storeCause)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnOverview((_, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Refused(
                storeCause ? MissingLock() : new Refusal("overview.not-configured", FailureReason.Refused, "No Overview."))));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            var page = new ProgressPage(model);
            var window = new MotifTestWindow { Content = page, Width = 900, Height = 700 };
            try
            {
                window.Show();
                await context.OpenProjectAsync("project.fwdata");
                PageScreenshots.Settle(window);
                var retries = page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Content as string == MotifAppStrings.TryAgain && button.IsEffectivelyVisible);
                Assert.Equal(storeCause ? 0 : 1, retries.Count());
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(2));
    }
}
