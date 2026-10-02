using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
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
                Assert.Equal(Orientation.Horizontal, controls.Orientation);
                foreach (var name in new[]
                         {
                             "Words from a Matrix cell", "Words from a list", "More ways to choose words",
                             "AI Handoff for these words", "Re-run words with new limits",
                         })
                    Assert.True(Assert.Single(controls.Children.OfType<Button>(), button =>
                        AutomationProperties.GetName(button) == name).IsTabStop);

                var matrix = ShowFlyout(window, "Words from a Matrix cell");
                Assert.Contains(matrix.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text == "Matrix cell");
                Assert.Single(matrix.GetLogicalDescendants().OfType<ComboBox>(), combo =>
                    AutomationProperties.GetName(combo) == "Matrix cell from Texts");
                Assert.Single(matrix.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Use cell");

                var list = ShowFlyout(window, "Words from a list");
                Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text == "List from Texts");
                Assert.Single(list.GetLogicalDescendants().OfType<ComboBox>(), combo =>
                    AutomationProperties.GetName(combo) == "List from Texts");
                Assert.Single(list.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Use list");

                var more = ShowFlyout(window, "More ways to choose words");
                Assert.Single(more.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Chosen in Texts");
                Assert.Single(more.GetLogicalDescendants().OfType<TextBox>(), input =>
                    AutomationProperties.GetName(input) == "Words picked by hand");
                Assert.Single(more.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Pick words");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(10));
    }

    private static Control ShowFlyout(Window window, string accessibleName)
    {
        var button = window.GetLogicalDescendants().OfType<Button>()
            .Single(candidate => AutomationProperties.GetName(candidate) == accessibleName);
        button.Flyout!.ShowAt(button);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return Assert.IsAssignableFrom<Control>(Assert.IsAssignableFrom<Flyout>(button.Flyout).Content);
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
                Assert.True(timing.UseCheckedWordsCommand.CanExecute(null));
                Assert.True(timing.UseTextsListCommand.CanExecute(null));

                var run = workspace.Assess.RerunAsync(["reading"], 1000);
                Assert.Equal(RunState.Running, workspace.Assess.State);
                workspace.Assess.CancelCommand.Execute(null);
                await run;

                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(RunState.Cancelled, workspace.Assess.State);
                Assert.True(workspace.Assess.ShowsEarlierResults);
                Assert.True(timing.UseCheckedWordsCommand.CanExecute(null));
                Assert.True(timing.UseTextsListCommand.CanExecute(null));
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
