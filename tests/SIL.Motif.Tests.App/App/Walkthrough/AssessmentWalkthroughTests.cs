using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AssessmentWalkthroughTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public void RunningAssessmentRendersReadingsAndPublishesStatistics()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            var baselineDeadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, baselineDeadline);

            var assessmentDeadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, assessmentDeadline);
            Assert.True(walkthrough.Find<Button>("Write the AI Handoff folder").IsEffectivelyEnabled);

            var result = walkthrough.Workspace.Assess.Result;
            Assert.NotNull(result);
            Assert.StartsWith("3 searches completed; 0 incomplete", result!.SummaryMarkdown);
            Assert.Equal(3, result.Words.Count);
            Assert.Equal(2, result.Words.Count(word => word.Outcome == "analysed"));
            Assert.Equal(1, result.Words.Count(word => word.Outcome == "no-analysis"));
            Assert.All(result.Words, word => Assert.False(word.IsIncomplete));

            walkthrough.Window.ApplyTemplate();
            walkthrough.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            var comparePanel = Assert.Single(walkthrough.Window.GetLogicalDescendants().OfType<ComparePanel>());
            Assert.Same(walkthrough.Workspace.Assess.Compare, comparePanel.Compare);
            Assert.Equal(result.Words.Select(word => word.Word).Order(StringComparer.Ordinal),
                walkthrough.Workspace.Assess.Compare.Words.Select(word => word.Word).Order(StringComparer.Ordinal));
            var rows = walkthrough.Workspace.Assess.Words.Rows.Cast<AssessWordRowViewModel>().ToList();
            Assert.Equal(3, rows.Count);
            // Readings are resolved against the project: every parsed word's morphs name a form, never an identifier.
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.All(row.Readings.SelectMany(r => r.Morphs),
                morph => Assert.DoesNotContain("(missing", morph.Form, StringComparison.Ordinal)));
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.NotEmpty(row.Readings));
            Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.True(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);

            Assert.True(walkthrough.Workspace.Context.HasEvidence);
            Assert.True(walkthrough.Named<ContentControl>("StatisticsHost").IsVisible);
            Assert.NotNull(walkthrough.Workspace.PageModel<TimingPageModel>().Statistics.AssessmentId);
            Assert.Equal(walkthrough.Workspace.PageModel<TimingPageModel>().Statistics.Groups[0],
                walkthrough.Workspace.PageModel<TimingPageModel>().Statistics.SelectedGroup);

            var details = walkthrough.Window.GetLogicalDescendants().OfType<Expander>()
                .Single(expander => Equals(expander.Header, "Detailed statistics"));
            details.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            walkthrough.Click("Refresh statistics");
            var statisticsDeadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
            var statistics = walkthrough.Workspace.PageModel<TimingPageModel>().Statistics;
            try
            {
                walkthrough.WaitUntil(
                    () => statistics.Rows.Count > 0 && statistics.LoadCommand.ExecutionTask is not { IsCompleted: false },
                    WalkthroughSteps.Remaining(statisticsDeadline), "statistics did not load any rows");
            }
            catch (Xunit.Sdk.XunitException failure)
            {
                Assert.Fail(failure.Message + "; " + StatisticsState(statistics));
            }

            Assert.True(walkthrough.Workspace.Baseline.HasAssessment);
            var projectLocator = new ProjectLocator(
                Path.GetFullPath(project.FwDataPath), Path.GetFileNameWithoutExtension(project.FwDataPath));
            using var database = MotifDatabase.OpenOwned(
                ProjectDatabaseCatalog.DatabasePathFor(projectLocator), projectLocator,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var invocations = new RetainedInvocationRepository(database).List(
                ProjectWorkspaceKey.Compute(projectLocator));
            Assert.Single(invocations);
            Assert.Equal(result.InvocationId, walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.InvocationId);
            Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    // Read when the wait gives up, so a failure tells a refusal from a load that never finished.
    private static string StatisticsState(StatisticsViewModel statistics) =>
        $"statistics refusal code='{statistics.Refusal?.Code}', " +
        $"statistics refusal='{statistics.Refusal?.Message}', stale='{statistics.IsStale}', " +
        $"fetched rows='{statistics.RowCount}', visible rows='{statistics.Rows.Count}', " +
        $"load running='{statistics.LoadCommand.IsRunning}', " +
        $"load task='{statistics.LoadCommand.ExecutionTask?.Status.ToString() ?? "never started"}'";
}
