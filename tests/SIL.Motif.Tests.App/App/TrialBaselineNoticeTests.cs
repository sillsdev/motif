using System.Globalization;
using Avalonia.Controls;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TrialBaselineNoticeTests
{
    private const string Reason = "the project has no published Baseline";

    [Fact]
    public void AWarningIsShownNextToTheTrialAndSaysItWasNotComparedAgainstABaseline()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var block = new TrialBaselineNoticeBlock { DataContext = TrialBaselineNotice.For(Reason) };
            var window = new Window { Content = block };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var notice = block.FindControl<Border>("TrialBaselineNotice")!;
                Assert.True(notice.IsVisible);
                var text = block.FindControl<TextBlock>("TrialBaselineNoticeText")!.Text;
                Assert.Equal(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TrialBaselineNotice, Reason), text);
                Assert.Equal(MotifAppStrings.TrialBaselineNoticeAccessibleName, Avalonia.Automation.AutomationProperties.GetName(notice));
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NoWarningLeavesTheNoticeHidden()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var block = new TrialBaselineNoticeBlock { DataContext = TrialBaselineNotice.For(null) };
            var window = new Window { Content = block };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.False(block.FindControl<Border>("TrialBaselineNotice")!.IsVisible);
                Assert.False(block.IsVisible);
                Assert.Equal(0, block.DesiredSize.Height);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TryAgainKeepsTheWorkflowsWarningWithTheProposalsTrialRunAndNoWarningMeansNone()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync(@"C:\projects\one.fwdata");

        fake.EnqueueTrialCompletesWith(new JobEnqueuedResponse("job/one", "trial", "project/test"), Reason);
        await context.TryAgainAsync("proposal/one", CancellationToken.None);
        var warned = context.TrialRun.StateFor("proposal/one").BaselineNotice;
        Assert.NotNull(warned);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TrialBaselineNotice, Reason), warned);

        fake.EnqueueTrialCompletesWith(new JobEnqueuedResponse("job/two", "trial", "project/test"));
        await context.TryAgainAsync("proposal/one", CancellationToken.None);
        Assert.Null(context.TrialRun.StateFor("proposal/one").BaselineNotice);
    }

    [Fact]
    public void TheAppQueuesTrialsOnlyThroughTheSharedWorkflowInBestEffortMode()
    {
        var root = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App");
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(file => file, File.ReadAllText);

        Assert.DoesNotContain(sources, source => source.Value.Contains("JobCommands.EnqueueTrial", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, source => source.Value.Contains("EnqueueWithBaseline", StringComparison.Ordinal));
        var client = Assert.Single(sources, source => Path.GetFileName(source.Key) == "CommandClient.Workbench.cs");
        Assert.Contains("TrialWorkflow.Enqueue(", client.Value, StringComparison.Ordinal);
        Assert.Contains("BaselineMode.BestEffort", client.Value, StringComparison.Ordinal);
    }
}
