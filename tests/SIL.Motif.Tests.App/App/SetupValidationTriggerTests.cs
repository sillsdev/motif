using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Setup shows the attempt-limit error when the field is left or a confirming button is pressed, not while typing.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SetupValidationTriggerTests
{
    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Frame, SetupViewModel Setup, SetupDialog Dialog,
        MotifTestWindow Window)> OpenAsync(int step)
    {
        var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false);
        var setup = workspace.Context.Setup!;
        setup.IsOpen = true;
        setup.Step = step;
        var dialog = new SetupDialog { DataContext = setup };
        dialog.FitToAvailableArea(900, 600);
        var window = new MotifTestWindow { Content = dialog, Width = 900, Height = 600 };
        window.Show();
        PageScreenshots.Settle(window);
        return (workspace, frame, setup, dialog, window);
    }

    private static async Task CloseAsync(WorkspaceShellViewModel workspace, MainWindow frame, SetupViewModel setup, Window window)
    {
        setup.IsOpen = false;
        window.Close();
        frame.Close();
        await workspace.DisposeAsync();
    }

    private static T Named<T>(SetupDialog dialog, string id) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(control =>
            AutomationProperties.GetAutomationId(control) == id);

    private static string? ShownError(SetupDialog dialog) =>
        dialog.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(block => block.Classes.Contains("setupError") && block.IsVisible && !string.IsNullOrEmpty(block.Text))?.Text;

    [Fact]
    public void TypingAnInvalidLimitShowsNoErrorUntilTheFieldIsLeft()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, dialog, window) = await OpenAsync(2);
            try
            {
                var field = Named<NumericUpDown>(dialog, AutomationIds.SetupStepLimit);
                field.Focus();
                Dispatcher.UIThread.RunJobs();
                field.Value = null;
                PageScreenshots.Settle(window);

                Assert.Null(setup.StepLimitValidationMessage);
                Assert.Null(ShownError(dialog));

                Named<Button>(dialog, AutomationIds.SetupBack).Focus();
                PageScreenshots.Settle(window);

                Assert.NotNull(setup.StepLimitValidationMessage);
                Assert.NotNull(ShownError(dialog));

                field.Focus();
                field.Value = 5000;
                Named<Button>(dialog, AutomationIds.SetupBack).Focus();
                PageScreenshots.Settle(window);

                Assert.Null(setup.StepLimitValidationMessage);
                Assert.Null(ShownError(dialog));
            }
            finally
            {
                await CloseAsync(workspace, frame, setup, window);
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NextWithAnInvalidLimitShowsTheErrorAndStaysOnTheStep()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, dialog, window) = await OpenAsync(2);
            try
            {
                setup.StepLimitSteps = null;
                Assert.Null(setup.StepLimitValidationMessage);

                setup.NextCommand.Execute(null);
                PageScreenshots.Settle(window);

                Assert.Equal(2, setup.Step);
                Assert.NotNull(setup.StepLimitValidationMessage);
                Assert.NotNull(ShownError(dialog));

                setup.StepLimitSteps = 5000;
                Assert.True(setup.ValidateStepLimit());
                Assert.Null(setup.StepLimitValidationMessage);
                setup.NextCommand.Execute(null);
                Assert.Equal(3, setup.Step);
            }
            finally
            {
                await CloseAsync(workspace, frame, setup, window);
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TypingAValidLimitAfterTheErrorClearsItWithoutLeavingTheField()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, dialog, window) = await OpenAsync(2);
            try
            {
                var field = Named<NumericUpDown>(dialog, AutomationIds.SetupStepLimit);
                field.Focus();
                field.Value = null;
                Named<Button>(dialog, AutomationIds.SetupBack).Focus();
                PageScreenshots.Settle(window);
                Assert.NotNull(ShownError(dialog));

                field.Focus();
                field.Value = 5000;
                PageScreenshots.Settle(window);

                Assert.True(field.IsKeyboardFocusWithin);
                Assert.Null(setup.StepLimitValidationMessage);
                Assert.Null(ShownError(dialog));
                Assert.DoesNotContain("Enter a positive", setup.StepLimitEstimateText, StringComparison.Ordinal);
            }
            finally
            {
                await CloseAsync(workspace, frame, setup, window);
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TickingNoAttemptLimitClearsTheError()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, dialog, window) = await OpenAsync(2);
            try
            {
                setup.StepLimitSteps = null;
                Assert.False(setup.ValidateStepLimit());
                PageScreenshots.Settle(window);
                Assert.NotNull(ShownError(dialog));

                setup.IsStepLimitUnbounded = true;
                PageScreenshots.Settle(window);

                Assert.Null(setup.StepLimitValidationMessage);
                Assert.Null(ShownError(dialog));
            }
            finally
            {
                await CloseAsync(workspace, frame, setup, window);
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ThePressedNextButtonWithAnEmptyFieldShowsTheErrorAndStaysOnTheStep()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, dialog, window) = await OpenAsync(2);
            try
            {
                var field = Named<NumericUpDown>(dialog, AutomationIds.SetupStepLimit);
                field.Focus();
                field.Value = null;
                Assert.Null(ShownError(dialog));

                Named<Button>(dialog, AutomationIds.SetupNext).Focus();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();
                PageScreenshots.Settle(window);

                Assert.Equal(2, setup.Step);
                Assert.NotNull(ShownError(dialog));
            }
            finally
            {
                await CloseAsync(workspace, frame, setup, window);
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void StartWithAnInvalidLimitShowsTheErrorAndDoesNotProceed()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame, setup, _, window) = await OpenAsync(3);
            try
            {
                setup.StepLimitSteps = null;
                Assert.Null(setup.StepLimitValidationMessage);

                await setup.FinishCommand.ExecuteAsync(null);

                Assert.NotNull(setup.StepLimitValidationMessage);
                Assert.True(setup.IsOpen);
                Assert.Equal(3, setup.Step);
            }
            finally
            {
                await CloseAsync(workspace, frame, setup, window);
            }
        }, TimeSpan.FromSeconds(30));
    }
}
