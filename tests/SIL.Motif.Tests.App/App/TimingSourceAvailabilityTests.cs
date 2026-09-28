using Avalonia.Automation;
using Avalonia.Threading;
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
    public void ACancelledAssessmentDisablesTimingSourcesThatLostTheirWords()
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

                var compare = workspace.Assess.Compare;
                Assert.Single(compare.Words).IsChecked = true;
                var timing = workspace.PageModel<TimingPageModel>();
                timing.SelectedTextsList = Assert.Single(compare.Presets, preset => preset.Count > 0);
                workspace.Context.OpenPage(WorkspacePage.Timing);
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
                Assert.False(chosenWords.IsEffectivelyEnabled);
                Assert.Equal("Tick words in Texts first.",
                    AutomationProperties.GetHelpText(chosenWords));
                Assert.False(useList.IsEffectivelyEnabled);
                Assert.Null(timing.SelectedTextsList);
                Assert.Equal("Choose a word list above first.",
                    AutomationProperties.GetHelpText(useList));
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
