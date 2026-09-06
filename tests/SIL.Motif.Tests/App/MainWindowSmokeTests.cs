using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Composes <see cref="MainWindow"/> from a <see cref="HandoffWorkspaceViewModel"/> built over fakes, and
/// checks what a headless platform can actually verify: every panel is attached and bound to its own
/// child view model, every input and button carries an accessible name (its own explicit
/// <see cref="AutomationProperties.NameProperty"/> or, for a <see cref="Button"/> or
/// <see cref="CheckBox"/>, the plain-text <see cref="ContentControl.Content"/> a screen reader falls back
/// to), and switching the Semi theme variant while a refusal and an in-progress state are bound raises no
/// exception. It cannot check visual clipping at 125%/150%/200% scale or real keyboard tab order — a
/// headless platform renders no pixels and starts no dispatcher loop, so those remain for a human running
/// the app.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MainWindowSmokeTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public MainWindowSmokeTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void ComposeAttachesEveryPanelBoundToItsOwnChildViewModelAndSetsTheWindowsDataContext()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();

            Assert.Same(workspace, window.DataContext);
            Assert.Same(workspace.Project, Assert.Single(window.GetLogicalDescendants().OfType<ProjectPanel>()).Project);
            Assert.Same(workspace.Baseline, Assert.Single(window.GetLogicalDescendants().OfType<ProjectPanel>()).Baseline);
            Assert.Same(workspace.Selection, Assert.Single(window.GetLogicalDescendants().OfType<SelectionPanel>()).Selection);
            Assert.Same(workspace.Assess, Assert.Single(window.GetLogicalDescendants().OfType<AssessPanel>()).Assess);
            Assert.Same(
                workspace.Statistics, Assert.Single(window.GetLogicalDescendants().OfType<StatisticsPanel>()).Statistics);
            Assert.Same(workspace.Handoff, Assert.Single(window.GetLogicalDescendants().OfType<HandoffPanel>()).Handoff);
        });
    }

    [Fact]
    public void EveryInputAndButtonHasAnAccessibleName()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window) = NewComposedWindow();

            var controls = window.GetLogicalDescendants().OfType<Control>()
                .Where(control => control is Button or CheckBox or ComboBox or TextBox or NumericUpDown or DataGrid)
                .ToList();

            Assert.NotEmpty(controls);
            foreach (var control in controls)
                Assert.False(
                    string.IsNullOrWhiteSpace(EffectiveAccessibleName(control)),
                    $"{control.GetType().Name} (content '{(control as ContentControl)?.Content}') has no accessible name.");
        });
    }

    [Fact]
    public void SwitchingTheThemeVariantWithARefusalAndAnInProgressStateBoundRaisesNoException()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, _) = NewComposedWindow();
            ((IProgress<AssessmentProgress>)workspace.Assess).Report(
                new AssessmentProgress(AssessmentStage.Parsing, 1, 2, "Parsing the Selection..."));
            workspace.Assess.Refusal = new Refusal(
                "assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built.");
            workspace.Handoff.Refusal = new Refusal(
                "handoff.cancelled", FailureReason.Refused, "The Handoff run was cancelled.");

            var application = Application.Current!;
            var exception = Record.Exception(() =>
            {
                application.RequestedThemeVariant = ThemeVariant.Dark;
                application.RequestedThemeVariant = ThemeVariant.Light;
            });

            Assert.Null(exception);
        });
    }

    // The explicit AutomationProperties.Name, or the plain-text Content a Button/CheckBox falls back to.
    private static string? EffectiveAccessibleName(Control control)
    {
        var explicitName = AutomationProperties.GetName(control);
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
        return control is ContentControl { Content: string text } ? text : null;
    }

    private static (HandoffWorkspaceViewModel Workspace, MainWindow Window) NewComposedWindow()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new StatisticsViewModel(fake),
            new HandoffViewModel(fake, selection, new FakeFolderPicker(), new FakeDragSource()));

        var window = new MainWindow();
        window.Compose(workspace);
        return (workspace, window);
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
