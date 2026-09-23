using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the stage shell as a headless platform can see it: one stage's controls showing at a time, a rail
/// entry that opens its stage, and the Handoff drag source that a keyboard can also reach. Also pins that
/// every Semi colour key a view names resolves to a brush in both theme variants, because an unknown key in
/// a <c>DynamicResource</c> is not an error — the setter silently does nothing.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WorkflowShellTests
{
    private static readonly string[] StageHosts =
        ["ProjectStage", "GrammarStage", "TextsStage", "ResultsStage", "HandoffStage"];

    private readonly AvaloniaHeadlessFixture _avalonia;

    public WorkflowShellTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void EverySemiColourKeyAViewNamesResolvesToABrushInBothThemeVariants()
    {
        var keys = ViewSources()
            .SelectMany(text => Regex.Matches(text, @"DynamicResource\s+(SemiColor\w+)").Select(match => match.Groups[1].Value))
            .Distinct()
            .Order()
            .ToList();
        Assert.NotEmpty(keys);

        _avalonia.Invoke(() =>
        {
            var application = Application.Current!;
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var key in keys)
            {
                Assert.True(application.TryGetResource(key, variant, out var value), $"{key} is not a Semi resource.");
                Assert.True(value is IBrush, $"{key} is a {value?.GetType().Name}, not a brush, in {variant}.");
            }
        });
    }

    [Fact]
    public void OnlyTheCurrentStagesScrollViewerIsVisible()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                foreach (var stage in Enum.GetValues<WorkflowStage>())
                {
                    workspace.CurrentStage = stage;

                    var visible = StageHosts.Where(name => Host(window, name).IsVisible).ToList();
                    Assert.Equal([$"{stage}Stage"], visible);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ChoosingAStepperEntryOpensItsStageAndEveryEntryIsNamedForItsStage()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var entries = window.GetLogicalDescendants().OfType<ListBoxItem>().ToList();
                Assert.Equal(
                    ["Project stage", "Grammar stage", "Texts stage", "Results stage", "Handoff stage"],
                    entries.Select(AutomationProperties.GetName));

                entries[4].IsSelected = true;

                Assert.Equal(WorkflowStage.Handoff, workspace.CurrentStage);
                Assert.True(Host(window, "HandoffStage").IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ThePrimaryActionOfEachStageLivesInTheBarOnlyWhileThatStageShows()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                string[] actions =
                [
                    "Continue to Grammar", "Continue to Texts", "Run the Assessment", "Continue to Handoff",
                    "Write the Handoff folder",
                ];
                foreach (var stage in Enum.GetValues<WorkflowStage>())
                {
                    workspace.CurrentStage = stage;
                    window.UpdateLayout();

                    var shown = actions.Where(name => ButtonNamed(window, name).IsEffectivelyVisible).ToList();
                    Assert.Equal([actions[(int)stage]], shown);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OnlyTheCurrentResultsViewShowsAndItsTabIsMarkedActive()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                workspace.CurrentStage = WorkflowStage.Results;
                window.Show();
                window.UpdateLayout();

                // Compare is the first view Results opens on; the others are one click away.
                Assert.Contains("active", ButtonNamed(window, "Compare results view").Classes);
                Assert.DoesNotContain("active", ButtonNamed(window, "Words results view").Classes);
                Assert.False(Host(window, "AssessHost").IsEffectivelyVisible);

                ButtonNamed(window, "Words results view").Command!.Execute(ResultsView.Words);
                window.UpdateLayout();

                Assert.Contains("active", ButtonNamed(window, "Words results view").Classes);
                Assert.DoesNotContain("active", ButtonNamed(window, "Statistics results view").Classes);
                Assert.True(Host(window, "AssessHost").IsEffectivelyVisible);
                Assert.False(Host(window, "StatisticsHost").IsEffectivelyVisible);

                ButtonNamed(window, "Statistics results view").Command!.Execute(ResultsView.Statistics);
                window.UpdateLayout();

                Assert.False(Host(window, "AssessHost").IsEffectivelyVisible);
                Assert.Contains("active", ButtonNamed(window, "Statistics results view").Classes);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PressingTheAllFilesButtonStartsADragOfEveryFileAndActivatingItFromTheKeyboardCopiesTheFolder()
    {
        string? clipboardText = null;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = NewComposedWindow();
            try
            {
                workspace.Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
                workspace.Handoff.Files.Add(new HandoffFileViewModel("grammar.json", @"C:\handoff\grammar.json"));
                workspace.Handoff.OutputDirectory = @"C:\handoff";
                workspace.Handoff.State = RunState.Completed;
                workspace.CurrentStage = WorkflowStage.Handoff;
                window.Show();
                window.UpdateLayout();

                var button = ButtonNamed(window, "Drag all Handoff files");
                Assert.True(button.Focusable);
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var press = new PointerPressedEventArgs(
                    button, pointer, window, new Point(), 0, PointerPointProperties.None, KeyModifiers.None);
                button.RaiseEvent(press);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal([@"C:\handoff\handoff.md", @"C:\handoff\grammar.json"], DragSource.LastPaths);
                Assert.True(press.Handled);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clipboardText = await window.Clipboard!.TryGetTextAsync();
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(@"C:\handoff", clipboardText);
    }

    private static readonly RecordingDragSource DragSource = new();

    private static Control Host(MainWindow window, string name) =>
        window.FindControl<Control>(name) ?? throw new InvalidOperationException($"No stage host named '{name}'.");

    private static Button ButtonNamed(MainWindow window, string accessibleName) =>
        window.GetLogicalDescendants().OfType<Button>().Single(control =>
            AutomationProperties.GetName(control) == accessibleName);

    private static IEnumerable<string> ViewSources()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var views = Path.Combine(root.FullName, "src", "SIL.Motif.App");
        return Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories).Select(File.ReadAllText);
    }

    private static (HandoffWorkspaceViewModel Workspace, MainWindow Window) NewComposedWindow()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new NoProjectPicker()),
            new ProjectHistoryViewModel(fake),
            new BaselineViewModel(fake),
            new GrammarViewModel(fake),
            selection,
            words,
            new AssessViewModel(fake, selection),
            new StatisticsViewModel(fake),
            new HandoffViewModel(fake, selection, new NoFolderPicker(), DragSource));

        var window = new MainWindow();
        window.Compose(workspace);
        return (workspace, window);
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class RecordingDragSource : IFileDragSource
    {
        public IReadOnlyList<string>? LastPaths { get; private set; }

        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
        {
            LastPaths = filePaths;
            return Task.FromResult(allowedEffects);
        }
    }
}
