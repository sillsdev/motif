using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
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
            var (workspace, window, _) = NewComposedWindow();

            Assert.Same(workspace, window.DataContext);
            Assert.Same(workspace.Project, Assert.Single(window.GetLogicalDescendants().OfType<ProjectPanel>()).Project);
            Assert.Same(workspace.Baseline, Assert.Single(window.GetLogicalDescendants().OfType<ProjectPanel>()).Baseline);
            Assert.Same(
                workspace.ProjectHistory, Assert.Single(window.GetLogicalDescendants().OfType<ProjectPanel>()).History);
            Assert.Same(workspace.Grammar, Assert.Single(window.GetLogicalDescendants().OfType<GrammarPanel>()).Grammar);
            Assert.Same(workspace.Selection, Assert.Single(window.GetLogicalDescendants().OfType<SelectionPanel>()).Selection);
            Assert.Same(workspace.Words, Assert.Single(window.GetLogicalDescendants().OfType<SelectionPanel>()).Words);
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
            var (workspace, window, _) = NewComposedWindow();
            window.Show();
            ShowEveryStage(window, workspace);

            var controls = window.GetLogicalDescendants().OfType<Control>()
                .Where(control => control is Button or HyperlinkButton or CheckBox or ComboBox or TextBox or NumericUpDown or DataGrid)
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
            var (workspace, _, _) = NewComposedWindow();
            ((IProgress<AssessmentProgress>)workspace.Assess).Report(
                new AssessmentProgress(AssessmentStage.Parsing, 1, 2, "Parsing the Selection..."));
            workspace.Assess.Refusal = new Refusal(
                "assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built.");
            workspace.Handoff.Refusal = new Refusal(
                "handoff.cancelled", FailureReason.Cancelled, "The Handoff run was cancelled.");

            var application = Application.Current!;
            var exception = Record.Exception(() =>
            {
                application.RequestedThemeVariant = ThemeVariant.Dark;
                application.RequestedThemeVariant = ThemeVariant.Light;
            });

            Assert.Null(exception);
        });
    }

    [Fact]
    public void AssessmentPanelShowsCollapsibleSectionsAndWordAndWarningTablesWithoutIdentifiers()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.Assess.Result = new AssessCommandResponse(
                    new BaselineCaptureResponse(
                        new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                            "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
                        "project.fwdata", DateTimeOffset.UtcNow, false, false),
                    new SelectionProjection([], []), [], "4 searches completed; 1 incomplete")
                {
                    Words =
                    [
                        new AssessmentWordResult("motifa", "analysed", false, "Search completed", 1, null)
                        {
                            Morphology = new ParseWordEvidence(
                                ParseMorphEvidence.Schema, 0, "motifa", 1, false, false, false,
                                [new ParseAnalysis([
                                    new("11111111-1111-1111-1111-111111111111",
                                        "22222222-2222-2222-2222-222222222222", null, null)])], []),
                            Readings = [new ParserReading([new ParserReadingMorph(
                                "motif-", "first gloss", "v", null, false,
                                "silfw://localhost/link?database%3dp%26tool%3dlexiconEdit")])],
                            TryWordLink = "silfw://localhost/link?database%3dp%26tool%3dAnalyses",
                        },
                        new AssessmentWordResult("motifb", "capped", true,
                            "INCOMPLETE — parsing did not finish (step limit)", 700, "partial")
                        {
                            Morphology = new ParseWordEvidence(
                                ParseMorphEvidence.Schema, 1, "motifb", 700, true, false, false,
                                [new ParseAnalysis([
                                    new("33333333-3333-3333-3333-333333333333",
                                        "44444444-4444-4444-4444-444444444444", null, null)])], [])
                        },
                        new AssessmentWordResult("motifc", "no-analysis", false, "Search completed", 5, null),
                        new AssessmentWordResult("motifd", "skipped", false, "Not attempted", 0, null)
                        {
                            Morphology = new ParseWordEvidence(
                                ParseMorphEvidence.Schema, 3, "motifd", 0, false, false, true, [], [])
                        },
                    ],
                };

                workspace.CurrentStage = WorkflowStage.Results;
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<AssessPanel>());
                var sections = panel.GetLogicalDescendants().OfType<Expander>()
                    .Select(AutomationProperties.GetName).Where(name => name?.EndsWith(" section") == true);
                Assert.Equal(["Run section"], sections);
                Assert.Single(window.GetLogicalDescendants().OfType<GrammarPanel>());

                Assert.Equal(4, workspace.Assess.Words.TotalCount);
                var rows = workspace.Assess.Words.Rows.Cast<AssessWordRowViewModel>().ToList();
                Assert.Equal("motif- = first gloss", Assert.Single(rows[0].Readings).Text);
                Assert.True(rows[0].HasTryWordLink);
                Assert.Contains("INCOMPLETE — parsing did not finish (step limit)", rows[1].Detail);
                Assert.Contains("Morphology evidence unavailable.", rows[2].Detail);
                Assert.Contains("Morphology evidence unavailable: invalid shape.", rows[3].Detail);

                var words = panel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Words");
                words.SelectedItem = rows[0];
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Contains(panel.GetVisualDescendants().OfType<HyperlinkButton>(),
                    link => Equals(link.Content, "motif-") && link.IsEffectivelyVisible);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("11111111-1111", StringComparison.Ordinal) == true);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CompletedHandoffShowsAccessibleFileTiles()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, dragSource) = NewComposedWindow();
            try
            {
                var files = new[]
                {
                    new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"),
                    new HandoffFileViewModel("grammar.json", @"C:\handoff\grammar.json"),
                    new HandoffFileViewModel("assessment.json", @"C:\handoff\assessment.json"),
                };
                foreach (var file in files) workspace.Handoff.Files.Add(file);
                workspace.Handoff.State = RunState.Completed;
                workspace.CurrentStage = WorkflowStage.Handoff;

                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<HandoffPanel>());
                var tiles = panel.GetLogicalDescendants().OfType<Border>()
                    .Where(tile => AutomationProperties.GetName(tile)?.StartsWith("Drag ", StringComparison.Ordinal)
                        == true)
                    .ToList();
                Assert.Equal(files.Length, tiles.Count);
                foreach (var file in files)
                    Assert.Contains(tiles, tile => AutomationProperties.GetName(tile) == file.DragAccessibleName);

                Assert.Contains(panel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Drag all Handoff files" && button.Focusable);

                var tile = tiles.Single(item =>
                    AutomationProperties.GetName(item) == "Drag assessment.json");
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var args = new PointerPressedEventArgs(
                    tile, pointer, window, new Point(), 0, PointerPointProperties.None, KeyModifiers.None);
                tile.RaiseEvent(args);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal([files[2].FullPath], dragSource.LastPaths);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CompletedHandoffShowsStarterPromptCopyButton()
    {
        string? clipboardText = null;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
                workspace.Handoff.PastedHeader = "pasted header text";
                workspace.Handoff.State = RunState.Completed;
                workspace.CurrentStage = WorkflowStage.Handoff;

                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var button = window.GetLogicalDescendants().OfType<Button>().Single(control =>
                    AutomationProperties.GetName(control) == "Copy the starter prompt");
                Assert.True(button.IsEffectivelyEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                IClipboard clipboard = window.Clipboard
                    ?? throw new InvalidOperationException("The headless window has no clipboard.");
                clipboardText = await clipboard.TryGetTextAsync();
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(5));

        Assert.Equal("pasted header text", clipboardText);
    }

    // A stage nobody has opened has no template applied, so its controls join the tree only once it is shown.
    private static void ShowEveryStage(MainWindow window, HandoffWorkspaceViewModel workspace)
    {
        foreach (var stage in Enum.GetValues<WorkflowStage>())
        foreach (var view in Enum.GetValues<ResultsView>())
        {
            workspace.CurrentStage = stage;
            workspace.ResultsView = view;
            window.UpdateLayout();
        }
    }

    // The explicit AutomationProperties.Name, or the plain-text Content a Button/CheckBox falls back to.
    private static string? EffectiveAccessibleName(Control control)
    {
        var explicitName = AutomationProperties.GetName(control);
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
        return control is ContentControl { Content: string text } ? text : null;
    }

    private static (HandoffWorkspaceViewModel Workspace, MainWindow Window, FakeDragSource DragSource)
        NewComposedWindow()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        var dragSource = new FakeDragSource();
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new FakeProjectPicker()),
            new ProjectHistoryViewModel(fake),
            new BaselineViewModel(fake),
            new GrammarViewModel(fake),
            selection,
            words,
            new AssessViewModel(fake, selection),
            new StatisticsViewModel(fake),
            new HandoffViewModel(fake, selection, new FakeFolderPicker(), dragSource));

        var window = new MainWindow();
        window.Compose(workspace);
        return (workspace, window, dragSource);
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
        public IReadOnlyList<string>? LastPaths { get; private set; }

        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
        {
            LastPaths = filePaths;
            return Task.FromResult(allowedEffects);
        }
    }
}
