using System.Text.Json;
using SIL.Motif.Commands.Queries;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
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
/// headless platform has no display scaling and starts no dispatcher loop, so those remain for a human running
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
            Assert.Same(workspace.PageModel<OverviewPageModel>(),
                Assert.Single(window.GetLogicalDescendants().OfType<OverviewPage>()).DataContext);
            Assert.Same(workspace.PageModel<WarningsPageModel>().Grammar, Assert.Single(window.GetLogicalDescendants().OfType<GrammarPanel>()).Grammar);
            Assert.Same(workspace.Selection, Assert.Single(window.GetLogicalDescendants().OfType<SelectionPanel>()).Selection);
            Assert.Same(workspace.PageModel<TextsPageModel>().Words, Assert.Single(window.GetLogicalDescendants().OfType<SelectionPanel>()).Words);
            Assert.Same(workspace.Assess, Assert.Single(window.GetLogicalDescendants().OfType<AssessPanel>()).Assess);
            Assert.Same(
                workspace.PageModel<TimingPageModel>().Statistics, Assert.Single(window.GetLogicalDescendants().OfType<StatisticsPanel>()).Statistics);
            Assert.Same(workspace.PageModel<AiHandoffPageModel>().Handoff, Assert.Single(window.GetLogicalDescendants().OfType<HandoffPanel>()).Handoff);
        });
    }

    [Fact]
    public void OverviewShowsStoredNumbersAndItsTilesNavigateWithoutRunningAnAssessment()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.Context.ProjectPath = @"C:\projects\aweti.fwdata";
                workspace.Context.ProjectOpenedUtc = DateTimeOffset.Parse("2026-09-24T11:02:00Z");
                workspace.PageModel<OverviewPageModel>().Overview = SampleOverview() with { IsStale = true };
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<UserControl>(),
                    control => control.GetType().Name == "ProjectPanel");
                var page = Assert.Single(window.GetLogicalDescendants().OfType<OverviewPage>());
                var text = string.Join("\n", page.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                Assert.Contains("41 of 125", text);
                Assert.Contains("18 of 39", text);
                Assert.Contains("8.4 ms", text);
                Assert.Contains("123.5 ms", text);
                Assert.Contains("6 findings", text);
                Assert.Contains("1 left out of the grammar", text);
                Assert.Contains("5 worth a look", text);
                Assert.Contains("Largest kind: environment failed validation (4)", text);
                Assert.Contains("FieldWorks has changed since the Baseline behind these numbers.", text);
                var tiles = window.GetLogicalDescendants().OfType<Button>()
                    .Where(item => AutomationProperties.GetName(item) is "Open Text Coverage in Texts" or
                        "Open accuracy in Texts" or "Open Timing" or "Open Warnings");
                Assert.All(tiles, tile =>
                {
                    Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, tile.HorizontalAlignment);
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, tile.VerticalAlignment);
                });

                Click("Open Text Coverage in Texts");
                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Equal(TextsTab.Matrix, workspace.PageModel<TextsPageModel>().Tab);

                Click("Open accuracy in Texts");
                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Equal(TextsTab.Matrix, workspace.PageModel<TextsPageModel>().Tab);

                Click("Open Timing");
                Assert.Equal(WorkspacePage.Timing, workspace.CurrentPage);

                Click("Open Warnings");
                Assert.Equal(WorkspacePage.Warnings, workspace.CurrentPage);

                Click("Start an AI Handoff");
                Assert.Equal(WorkspacePage.AiHandoff, workspace.CurrentPage);
                Assert.Empty(((FakeCommandClient)workspace.Context.Commands).AssessRequests);

                void Click(string accessibleName)
                {
                    var button = window.GetLogicalDescendants().OfType<Button>()
                        .Single(item => AutomationProperties.GetName(item) == accessibleName);
                    button.Command!.Execute(button.CommandParameter);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OverviewWithoutStoredAssessmentDoesNotShowZeroesAsResults()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.PageModel<OverviewPageModel>().Overview = SampleOverview() with
                {
                    AssessmentId = null,
                    AssessedUtc = null,
                    AssessmentElapsedSeconds = null,
                    TextCoverage = new OverviewTextCoverage(0, 0, 0, 0, 0, 0),
                    Accuracy = new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
                    Timing = new OverviewTiming(null, null, [], 0),
                    Warnings = null,
                };
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var page = Assert.Single(window.GetLogicalDescendants().OfType<OverviewPage>());
                var text = string.Join("\n", page.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                Assert.Contains("No Assessment for the default Selection.", text);
                Assert.Contains("No Assessment", text);
                Assert.Contains("Not recorded", text);
                Assert.Contains("No warning summary is available.", text);
                Assert.DoesNotContain("0 of 0", text);
                Assert.DoesNotContain("words hit the step limit", text);
            }
            finally
            {
                window.Close();
            }
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
    public void ReadOnlyDisplayedTextCanBeSelectedAndCopiedButButtonTextCannot()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                window.Show();
                var inspected = 0;
                foreach (var page in Enum.GetValues<WorkspacePage>())
                foreach (var tab in Enum.GetValues<TextsTab>())
                {
                    workspace.CurrentPage = page;
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    window.UpdateLayout();

                    var readOnlyText = window.GetVisualDescendants().OfType<TextBlock>()
                        .Where(text => text.IsEffectivelyVisible)
                        .Where(text => !IsWithinInteractiveControl(text))
                        .Where(text => !string.IsNullOrEmpty(text.Text))
                        .ToList();

                    inspected += readOnlyText.Count;
                    Assert.All(readOnlyText.Where(text => text.Classes.Contains("stage-title")),
                        title => Assert.Equal(22, title.FontSize));
                    Assert.All(readOnlyText, text =>
                    {
                        var selectable = text as SelectableTextBlock;
                        Assert.True(selectable is not null,
                            $"'{text.Text}' is plain text under {string.Join(" > ", text.GetVisualAncestors().Select(item => item.GetType().Name))}.");
                        selectable.SelectAll();
                        Assert.True(selectable.CanCopy, $"'{selectable.Text}' cannot be copied.");
                        Assert.Equal(selectable.Text, selectable.SelectedText);
                    });
                }

                Assert.True(inspected > 0);
                Assert.DoesNotContain(
                    window.GetVisualDescendants().OfType<SelectableTextBlock>(),
                    IsWithinInteractiveControl);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ResultTablesRenderSelectableDynamicCells()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                using var statisticsDocument = JsonDocument.Parse(
                    "{\"kind\":\"word\",\"form\":\"motifa\",\"attempts\":2,\"passes\":1,\"elapsed_ns\":5000000}");
                workspace.Context.PublishEvidence(new WorkspaceEvidence(new AssessCommandResponse(
                    new BaselineCaptureResponse(
                        new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                            "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
                        "project.fwdata", DateTimeOffset.UtcNow, false, false),
                    new SelectionProjection([], []), [], "summary"), DateTimeOffset.Now, WasRerun: false));
                workspace.PageModel<TimingPageModel>().Statistics.Rows.Add(new StatsRowViewModel(statisticsDocument.RootElement.Clone()));
                workspace.CurrentPage = WorkspacePage.Timing;

                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var details = window.GetVisualDescendants().OfType<Expander>()
                    .Single(expander => Equals(expander.Header, "Detailed statistics"));
                details.IsExpanded = true;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var statistics = window.GetVisualDescendants().OfType<DataGrid>()
                    .Single(grid => AutomationProperties.GetName(grid) == "Statistics rows");
                AssertSelectableCells(statistics);
                statistics.SelectedItem = null;
                var statisticsCell = statistics.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .First(cell => cell.IsEffectivelyVisible && cell.Text == "motifa");
                RaiseLeftPointerPress(statisticsCell, window);
                Assert.Same(workspace.PageModel<TimingPageModel>().Statistics.Rows[0], statistics.SelectedItem);

                workspace.PageModel<WarningsPageModel>().Grammar.Warnings.Load([
                    new GrammarWarning(
                        GrammarDiagnosticLevel.Warning, "Entry",
                        [new GrammarWarningPart("lex entry", GrammarWarningPartRole.Text)],
                        [new GrammarWarningPart("dropped", GrammarWarningPartRole.Text)],
                        "warning: lex entry: dropped")
                    {
                        Group = "Dropped item",
                        Code = "fwdata.dropped-item",
                        Guidance = "Restore the missing item in FieldWorks.",
                    }]);
                workspace.PageModel<WarningsPageModel>().Grammar.HasBaseline = true;
                workspace.PageModel<WarningsPageModel>().Grammar.HasChecked = true;
                workspace.CurrentPage = WorkspacePage.Warnings;
                workspace.PageModel<WarningsPageModel>().Grammar.Warnings.SelectGroupCommand.Execute(
                    Assert.Single(workspace.PageModel<WarningsPageModel>().Grammar.Warnings.WarningGroups));
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.Text == "Restore the missing item in FieldWorks.");

                var grammar = window.GetVisualDescendants().OfType<DataGrid>()
                    .Single(grid => AutomationProperties.GetName(grid) == "Grammar warnings");
                AssertSelectableCells(grammar);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CopyableTextStillLetsClickableParentsReceivePointerEvents()
    {
        _avalonia.Invoke(() =>
        {
            var text = new CopyableTextBlock { Text = "select or click me" };
            var parent = new Border { Child = text };
            var window = new Window { Content = parent };
            var presses = 0;
            var releases = 0;
            parent.PointerPressed += (_, _) => presses++;
            parent.PointerReleased += (_, _) => releases++;

            try
            {
                window.Show();
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var properties = new PointerPointProperties(
                    RawInputModifiers.LeftMouseButton,
                    PointerUpdateKind.LeftButtonPressed);
                text.RaiseEvent(new PointerPressedEventArgs(
                    text, pointer, window, new Point(), 0, properties, KeyModifiers.None));

                Assert.Equal(1, presses);
                var releaseProperties = new PointerPointProperties(
                    RawInputModifiers.None,
                    PointerUpdateKind.LeftButtonReleased);
                text.RaiseEvent(new PointerReleasedEventArgs(
                    text, pointer, window, new Point(), 1, releaseProperties, KeyModifiers.None, MouseButton.Left));
                Assert.Equal(1, releases);
                text.SelectAll();
                Assert.Equal(text.Text, text.SelectedText);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void LinkedGrammarWarningPartsKeepLinkSemanticsWithoutReplacementCharacters()
    {
        _avalonia.Invoke(() =>
        {
            var block = new GrammarWarningPartsBlock
            {
                Parts =
                [
                    new GrammarWarningPart("entry", GrammarWarningPartRole.Object, "id", "lex entry", "silfw://localhost/link"),
                    new GrammarWarningPart("has", GrammarWarningPartRole.Text),
                    new GrammarWarningPart("value", GrammarWarningPartRole.Value),
                ],
            };

            var link = Assert.Single(block.Children.OfType<HyperlinkButton>());
            Assert.Equal("entry", link.Content);
            Assert.Equal(new Uri("silfw://localhost/link"), link.NavigateUri);

            var pieces = block.Children.OfType<CopyableTextBlock>().ToList();
            Assert.Equal(["has", "value"], pieces.Select(piece => piece.Text));
            Assert.All(pieces, piece =>
            {
                piece.SelectAll();
                Assert.Equal(piece.Text, piece.SelectedText);
                Assert.DoesNotContain('\uFFFC', piece.SelectedText);
            });
        });
    }

    [Fact]
    public void UnavailableGrammarWarningLinksExplainWhyFieldWorksCannotOpenTheSubject()
    {
        _avalonia.Invoke(() =>
        {
            var block = new GrammarWarningPartsBlock
            {
                Parts =
                [
                    new GrammarWarningPart("entry", GrammarWarningPartRole.Object, "id", "lex entry")
                    {
                        LinkStatus = FieldWorksLinkStatus.Unavailable,
                        LinkReason = FieldWorksLinkReason.GuidNotRecorded,
                    },
                ],
            };

            Assert.Equal("entry (FieldWorks link unavailable: PanGloss did not record a GUID)",
                Assert.IsType<CopyableTextBlock>(Assert.Single(block.Children)).Text);
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
            workspace.PageModel<AiHandoffPageModel>().Handoff.Refusal = new Refusal(
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
    public void ClickingACompareCellListsOnlyItsWords()
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
                    new SelectionProjection([], []), [], "2 searches completed; 0 incomplete")
                {
                    Words =
                    [
                        new AssessmentWordResult("kitabu", "no-analysis", false, "Search completed", 1, null)
                            { ProjectStanding = ProjectStanding.Approved, MissedApproved = [] },
                        new AssessmentWordResult("mwalimu", "no-analysis", false, "Search completed", 1, null)
                            { ProjectStanding = ProjectStanding.NotPresent },
                    ],
                };
                workspace.Context.OpenTexts(TextsTab.Matrix);
                window.Show();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<ComparePanel>());
                Assert.True(panel.IsEffectivelyVisible);
                var lost = panel.GetVisualDescendants().OfType<Border>().Single(border =>
                    border.Tag is CompareCellViewModel { Row: WordProjectStatus.Approved, Column: CompareColumnKind.NoParse });
                Assert.StartsWith("Approved, No parse: 1 words", AutomationProperties.GetName(lost));
                lost.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(lost,
                    new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true), window, default, 0,
                    new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton,
                        Avalonia.Input.PointerUpdateKind.LeftButtonPressed), Avalonia.Input.KeyModifiers.None));
                window.UpdateLayout();

                Assert.Equal(["kitabu"], workspace.Assess.Compare.Words.Select(word => word.Word));
                Assert.Contains("selected", lost.Classes);
            }
            finally
            {
                window.Close();
            }
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

                workspace.Context.OpenTexts(TextsTab.Words);
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
                foreach (var file in files) workspace.PageModel<AiHandoffPageModel>().Handoff.Files.Add(file);
                workspace.PageModel<AiHandoffPageModel>().Handoff.State = RunState.Completed;
                workspace.CurrentPage = WorkspacePage.AiHandoff;

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
                var tileText = tile.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Single(text => text.Text == "assessment.json");
                RaiseLeftPointerPress(tileText, window);
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
                workspace.PageModel<AiHandoffPageModel>().Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
                workspace.PageModel<AiHandoffPageModel>().Handoff.PastedHeader = "pasted header text";
                workspace.PageModel<AiHandoffPageModel>().Handoff.State = RunState.Completed;
                workspace.CurrentPage = WorkspacePage.AiHandoff;

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

    private static void RaiseLeftPointerPress(Control target, Window window)
    {
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed);
        target.RaiseEvent(new PointerPressedEventArgs(
            target, pointer, window, new Point(), 0, properties, KeyModifiers.None));
    }

    private static void AssertSelectableCells(DataGrid grid)
    {
        var cells = grid.GetVisualDescendants().OfType<SelectableTextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .ToList();

        Assert.NotEmpty(cells);
        Assert.All(cells, text =>
        {
            text.SelectAll();
            Assert.True(text.CanCopy, $"'{text.Text}' cannot be copied.");
            Assert.Equal(text.Text, text.SelectedText);
        });
    }

    private static bool IsWithinInteractiveControl(Visual visual) =>
        visual.FindAncestorOfType<Button>(includeSelf: false) is not null
        || visual.FindAncestorOfType<ToggleButton>(includeSelf: false) is not null
        || visual.FindAncestorOfType<TextBox>(includeSelf: false) is not null;

    // A page nobody has opened has no template applied, so its controls join the tree only once it is shown.
    private static void ShowEveryStage(MainWindow window, HandoffWorkspaceViewModel workspace)
    {
        foreach (var page in Enum.GetValues<WorkspacePage>())
        foreach (var tab in Enum.GetValues<TextsTab>())
        {
            workspace.CurrentPage = page;
            workspace.PageModel<TextsPageModel>().Tab = tab;
            window.UpdateLayout();
        }
    }

    [Fact]
    public void AtTheNarrowestWindowEveryPageOpensOnAClickInTheCollapsedSidebar()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.Show();
            try
            {
                Assert.True(workspace.IsSidebarCollapsed);
                foreach (var page in Enum.GetValues<WorkspacePage>().Reverse())
                {
                    var entry = window.GetLogicalDescendants().OfType<ListBoxItem>()
                        .Single(item => item.DataContext is PageViewModel model && model.Page == page);

                    var centre = entry.TranslatePoint(new Point(entry.Bounds.Width / 2, entry.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(centre, MouseButton.Left);
                    window.MouseUp(centre, MouseButton.Left);
                    window.UpdateLayout();

                    Assert.Equal(page, workspace.CurrentPage);
                }
            }
            finally
            {
                window.Close();
            }
        });
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
        var dragSource = new FakeDragSource();
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new FakeFolderPicker(), dragSource,
            fake);

        var window = new MainWindow();
        window.Compose(workspace);
        return (workspace, window, dragSource);
    }

    private static OverviewResponse SampleOverview() => new(
        "Aweti", DateTimeOffset.Parse("2026-09-24T11:02:00Z"), DateTimeOffset.Parse("2026-09-24T10:58:00Z"),
        125, 3, 2, 313, 812, 38, 167, "assessment-1", DateTimeOffset.Parse("2026-09-24T11:04:00Z"), 130,
        "grammar-fingerprint", "selection-fingerprint",
        new OverviewTextCoverage(41, 23, 55, 6, 313, 183),
        new OverviewAccuracy(18, 39, 5, 55, 2, 4, 9, 18),
        new OverviewTiming(8.4, 123.5, [new SlowWordTiming("miboko", 1000)], 55),
        new OverviewWarningsSummary(6, 1, "environment failed validation", 4)
        {
            WarningCount = 1,
            InformationCount = 5,
        })
    {
        ProjectFileName = "aweti.fwdata",
    };

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
