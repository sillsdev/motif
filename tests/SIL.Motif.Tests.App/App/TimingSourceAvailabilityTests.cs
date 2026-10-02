using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingSourceAvailabilityTests
{
    private const string ProjectPath = @"C:\projects\reading.fwdata";

    [Fact]
    public void WordSourcesStayBehindOneCompactControlStrip()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var (workspace, window) = FakeComposedWindow.Create(fake);
            try
            {
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();
                workspace.Context.ProjectPath = ProjectPath;
                workspace.Assess.ProjectPath = ProjectPath;
                workspace.Assess.Result = Assessment();
                workspace.Context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow,
                    WasRerun: false));
                await workspace.Context.EvidencePublication;
                workspace.Context.OpenPage(WorkspacePage.Timing);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var controls = Assert.Single(window.GetVisualDescendants().OfType<StackPanel>(),
                    panel => AutomationProperties.GetName(panel) == "Timing controls");
                var sources = Assert.Single(window.GetVisualDescendants().OfType<Expander>(),
                    expander => AutomationProperties.GetName(expander) == "More word sources");
                Assert.Equal(Orientation.Horizontal, controls.Orientation);
                Assert.False(sources.IsExpanded);
                sources.IsExpanded = true;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.Contains(sources.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Matrix cell");
                Assert.Contains(sources.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "List from Texts");
                Assert.Equal(2, sources.GetVisualDescendants().OfType<ComboBox>().Count());
                AssertPickerLabelBesideControl(sources, "Matrix cell", "Matrix cell from Texts");
                AssertPickerLabelBesideControl(sources, "List from Texts", "List from Texts");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(10));
    }

    private static void AssertPickerLabelBesideControl(Control row, string labelText, string controlName)
    {
        var label = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == labelText);
        var control = Assert.Single(row.GetVisualDescendants().OfType<ComboBox>(),
            combo => AutomationProperties.GetName(combo) == controlName);

        Assert.True(label.Bounds.Right <= control.Bounds.Left);
        Assert.True(label.Bounds.Top < control.Bounds.Bottom && control.Bounds.Top < label.Bounds.Bottom);
    }

    [Fact]
    public void ACancelledParseKeepsTimingSourcesOnTheEarlierWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.AssessBlocksUntilCancelled(new Refusal(
                "assessment.cancelled", FailureReason.Cancelled, "The Assessment run was cancelled."));
            var (workspace, window) = FakeComposedWindow.Create(fake);
            try
            {
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();
                workspace.Context.ProjectPath = ProjectPath;
                workspace.Assess.ProjectPath = ProjectPath;
                workspace.Assess.Result = Assessment();
                workspace.Context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow,
                    WasRerun: false));
                await workspace.Context.EvidencePublication;

                var compare = workspace.Assess.Compare;
                Assert.Single(compare.Words).IsChecked = true;
                var timing = workspace.PageModel<TimingPageModel>();
                timing.SelectedTextsList = Assert.Single(compare.Presets, preset => preset.Count > 0);
                workspace.Context.OpenPage(WorkspacePage.Timing);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.GetVisualDescendants().OfType<Expander>()
                    .Single(expander => AutomationProperties.GetName(expander) == "More word sources").IsExpanded = true;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var chosenWords = FakeComposedWindow.FindButton(window, "Chosen in Texts");
                var useList = FakeComposedWindow.FindButton(window, "Use list");
                Assert.True(chosenWords.IsEffectivelyEnabled);
                Assert.True(useList.IsEffectivelyEnabled);

                var run = workspace.Assess.RerunAsync(["reading"], 1000);
                Assert.Equal(RunState.Running, workspace.Assess.State);
                workspace.Assess.CancelCommand.Execute(null);
                await run;

                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(RunState.Cancelled, workspace.Assess.State);
                Assert.True(workspace.Assess.ShowsEarlierResults);
                Assert.True(chosenWords.IsEffectivelyEnabled);
                Assert.True(useList.IsEffectivelyEnabled);
                Assert.NotNull(timing.SelectedTextsList);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(10));
    }

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(
            new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
            ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), ["assessment/one"], "summary")
    {
        Words = [new AssessmentWordResult("reading", "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = "approved",
            OccurrenceCount = 1,
        }],
    };
}
