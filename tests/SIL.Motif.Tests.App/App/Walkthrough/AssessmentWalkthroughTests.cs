using SIL.Motif.App;
using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    [Fact]
    public void AssessmentEvidenceRemainsAvailableAcrossTheRegisteredPages()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = WalkthroughSteps.DeadlineIn(360);

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: FakeParser.ExecutablePath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
            var result = Assert.IsType<SIL.Motif.Contract.Responses.AssessCommandResponse>(walkthrough.Workspace.Assess.Result);
            Assert.NotEmpty(result.Words);
            var evidence = walkthrough.Workspace.Context.Evidence.ParseTimeAssessmentId;
            Assert.NotNull(evidence);

            walkthrough.ShowPage(WorkspacePage.Progress);
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowPage(WorkspacePage.Words);
            Assert.Same(result, walkthrough.Workspace.Assess.Result);
            Assert.Equal(evidence, walkthrough.Workspace.Context.Evidence.ParseTimeAssessmentId);
            walkthrough.Workspace.Context.Diagnostics = true;
            walkthrough.Workspace.Context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
            PageScreenshots.Settle(walkthrough.Window);
            Assert.Equal(WorkspacePage.Diagnostics, walkthrough.Workspace.CurrentPage);
            Assert.Equal(evidence, walkthrough.Workspace.Context.Evidence.ParseTimeAssessmentId);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [RealParserFact]
    public void RunningAssessmentRendersReadingsAndPublishesStatistics()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = WalkthroughSteps.DeadlineIn(360);

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            var baselineDeadline = WalkthroughSteps.DeadlineIn(60);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, baselineDeadline);
            WalkthroughSteps.EnsureAssessmentForAnalyze(walkthrough, TimeSpan.FromMinutes(3));
            walkthrough.Workspace.Selection.AllWordforms = false;
            foreach (var text in walkthrough.Workspace.Selection.Texts) text.IsChecked = false;

            var assessmentDeadline = WalkthroughSteps.DeadlineIn(180);
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
            walkthrough.ShowPage(WorkspacePage.Words);
            Assert.Equal(result.Words.Select(word => word.Word).Order(StringComparer.Ordinal),
                walkthrough.Workspace.Assess.Compare.Words.Select(word => word.Word).Order(StringComparer.Ordinal));
            var rows = walkthrough.Workspace.Assess.Words.Rows.Cast<AssessWordRowViewModel>().ToList();
            Assert.Equal(3, rows.Count);
            // Readings are resolved against the project: every parsed word's morphs name a form, never an identifier.
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.All(row.Readings.SelectMany(r => r.Morphs),
                morph => Assert.DoesNotContain("(missing", morph.Form, StringComparison.Ordinal)));
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.NotEmpty(row.Readings));
            Assert.True(walkthrough.ProjectSwitchEnabled);

            Assert.True(walkthrough.Workspace.Context.HasEvidence);
            walkthrough.Workspace.Context.Diagnostics = true;
            walkthrough.Workspace.Context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
            Assert.Equal(WorkspacePage.Diagnostics, walkthrough.Workspace.CurrentPage);
            Assert.NotNull(walkthrough.Workspace.Context.Evidence.ParseTimeAssessmentId);

            Assert.False(walkthrough.Workspace.Context.NeedsAssessment);
            var projectLocator = new ProjectLocator(
                Path.GetFullPath(project.FwDataPath), Path.GetFileNameWithoutExtension(project.FwDataPath));
            using var database = MotifDatabase.OpenOwned(
                ProjectDatabaseCatalog.DatabasePathFor(projectLocator), projectLocator,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var invocations = new RetainedInvocationRepository(database).List(
                ProjectWorkspaceKey.Compute(projectLocator));
            Assert.Equal(2, invocations.Count);
            Assert.Contains(invocations, invocation => invocation.InvocationId == result.InvocationId);
            var setupInvocation = invocations.Single(invocation => invocation.InvocationId != result.InvocationId);
            Assert.Equal(["motifanalysed", "motifunanalysed"],
                setupInvocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));
            Assert.Equal(result.InvocationId, walkthrough.Workspace.PageModel<SettingsPageModel>().Handoff.Handoff.InvocationId);
            Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

}
