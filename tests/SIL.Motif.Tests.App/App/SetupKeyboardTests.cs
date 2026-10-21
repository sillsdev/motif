using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Real key presses in Setup: Enter confirms, Esc cancels, and text inputs keep their own Enter.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SetupKeyboardTests
{
    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Frame, SetupViewModel Setup, SetupDialog Dialog,
        MotifTestWindow Window, FakeCommandClient Fake)> OpenAsync(int step)
    {
        FakeCommandClient? captured = null;
        var (workspace, frame) = await PageScreenshots.OpenOverSampleData(configure: (fake, _) => captured = fake);
        var setup = workspace.Context.Setup!;
        setup.IsOpen = true;
        setup.Step = step;
        var dialog = new SetupDialog { DataContext = setup };
        dialog.FitToAvailableArea(900, 600);
        var window = new MotifTestWindow { Content = dialog, Width = 900, Height = 600 };
        window.Show();
        PageScreenshots.Settle(window);
        return (workspace, frame, setup, dialog, window, captured!);
    }

    private static async Task CloseAsync(WorkspaceShellViewModel workspace, MainWindow frame, SetupViewModel setup, Window window)
    {
        setup.IsOpen = false;
        window.Close();
        frame.Close();
        await workspace.DisposeAsync();
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(SetupDialog dialog, string id) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(control =>
            AutomationProperties.GetAutomationId(control) == id);

    [Fact]
    public void EnterOnTheFirstStepAdvances()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, _, window, _) = await OpenAsync(0);
            try
            {
                Press(window, Key.Enter);
                Assert.Equal(1, setup.Step);
            }
            finally { await CloseAsync(workspace, frame, setup, window); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void EnterInTheAddWordsBoxAndTheLimitFieldDoesNotAdvance()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, dialog, window, _) = await OpenAsync(1);
            try
            {
                var words = Named<TextBox>(dialog, AutomationIds.SetupAddWords);
                words.Focus();
                Dispatcher.UIThread.RunJobs();
                Press(window, Key.Enter);
                Assert.Equal(1, setup.Step);

                setup.Step = 2;
                PageScreenshots.Settle(window);
                var limit = Named<NumericUpDown>(dialog, AutomationIds.SetupStepLimit);
                limit.Focus();
                Dispatcher.UIThread.RunJobs();
                Assert.True(limit.IsKeyboardFocusWithin, "limit field has focus");
                Press(window, Key.Enter);
                Assert.Equal(2, setup.Step);
            }
            finally { await CloseAsync(workspace, frame, setup, window); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void EscCancelsWithoutRecordingASkipAndSetupIsOfferedAgain()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, _, window, fake) = await OpenAsync(1);
            try
            {
                var skipsBefore = fake.SkipSetupRequests.Count;
                Press(window, Key.Escape);
                Assert.False(setup.IsOpen);
                Assert.Equal(skipsBefore, fake.SkipSetupRequests.Count);

                await setup.ProjectOpenedAsync(PageScreenshots.SampleProjectPath);
                Assert.True(setup.IsOpen);
            }
            finally { await CloseAsync(workspace, frame, setup, window); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void EnterOnTheFinishStepFinishes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, _, window, fake) = await OpenAsync(3);
            try
            {
                Assert.True(setup.FinishCommand.CanExecute(null));
                Press(window, Key.Enter);
                var until = DateTime.UtcNow.AddSeconds(30);
                while (fake.SetDefaultSelectionRequests.Count == 0 && DateTime.UtcNow < until)
                {
                    await Task.Delay(50);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.NotEmpty(fake.SetDefaultSelectionRequests);
            }
            finally { await CloseAsync(workspace, frame, setup, window); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void CancelAndEscAreUnavailableWhileTheSelectionIsBeingSaved()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, dialog, window, fake) = await OpenAsync(3);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.OnSetDefaultSelection(async (_, _) =>
            {
                await release.Task;
                return CommandOutcome<DefaultSelectionResponse>.Refused(
                    new Refusal("selection.test", FailureReason.Refused, "Held save finished."));
            });
            try
            {
                var cancel = Named<Button>(dialog, AutomationIds.SetupCancel);
                Assert.True(cancel.IsEffectivelyEnabled);
                setup.FinishCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.False(setup.CancelCommand.CanExecute(null));
                Assert.False(cancel.IsEffectivelyEnabled);

                Press(window, Key.Escape);
                Assert.True(setup.IsOpen);

                release.SetResult();
                var until = DateTime.UtcNow.AddSeconds(30);
                while (!setup.CancelCommand.CanExecute(null) && DateTime.UtcNow < until)
                {
                    await Task.Delay(50);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.True(setup.CancelCommand.CanExecute(null));
                Press(window, Key.Escape);
                Assert.False(setup.IsOpen);
            }
            finally
            {
                release.TrySetResult();
                await CloseAsync(workspace, frame, setup, window);
            }
        }, TimeSpan.FromMinutes(2));
    }
}
