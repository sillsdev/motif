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
            Assert.True(walkthrough.Find<Button>("Write the Handoff folder").IsEffectivelyEnabled);

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
            var assessPanel = Assert.Single(walkthrough.Window.GetLogicalDescendants().OfType<AssessPanel>());
            Assert.Same(walkthrough.Workspace.Assess, assessPanel.Assess);
            var rows = walkthrough.Workspace.Assess.Words.Rows.Cast<AssessWordRowViewModel>().ToList();
            Assert.Equal(3, rows.Count);
            // Readings are resolved against the project: every parsed word's morphs name a form, never an identifier.
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.All(row.Readings.SelectMany(r => r.Morphs),
                morph => Assert.DoesNotContain("(missing", morph.Form, StringComparison.Ordinal)));
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.NotEmpty(row.Readings));
            Assert.True(walkthrough.Window.FindControl<ContentControl>("ProjectHost")!.IsEffectivelyEnabled);
            Assert.True(walkthrough.Window.FindControl<ContentControl>("SelectionHost")!.IsEffectivelyEnabled);

            Assert.True(walkthrough.Workspace.HasEverAssessed);
            Assert.True(walkthrough.Window.FindControl<ContentControl>("StatisticsHost")!.IsVisible);
            Assert.NotNull(walkthrough.Workspace.Statistics.AssessmentId);
            Assert.Equal(walkthrough.Workspace.Statistics.Groups[0],
                walkthrough.Workspace.Statistics.SelectedGroup);

            walkthrough.Click("Refresh statistics");
            var statisticsDeadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Statistics.Rows.Count > 0 &&
                    walkthrough.Workspace.Statistics.LoadCommand.ExecutionTask is not { IsCompleted: false },
                WalkthroughSteps.Remaining(statisticsDeadline), "statistics did not load any rows");

            Assert.True(walkthrough.Workspace.Baseline.HasAssessment);
            var projectLocator = new ProjectLocator(
                Path.GetFullPath(project.FwDataPath), Path.GetFileNameWithoutExtension(project.FwDataPath));
            using var database = MotifDatabase.OpenOwned(
                ProjectDatabaseCatalog.DatabasePathFor(projectLocator), projectLocator,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var invocations = new RetainedInvocationRepository(database).List(
                ProjectWorkspaceKey.Compute(projectLocator));
            Assert.Single(invocations);
            Assert.Equal(result.InvocationId, walkthrough.Workspace.Handoff.InvocationId);
            Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

}
