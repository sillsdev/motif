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
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LiveMarkdown.Avalonia;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Composes <see cref="MainWindow"/> from a <see cref="WorkspaceShellViewModel"/> built over fakes, and
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
    public async Task F1OpensHelpForTheCurrentPage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            workspace.CurrentPage = WorkspacePage.Timing;
            try
            {
                window.Show();
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var helpButton = window.FindControl<Button>("HelpButton");
                Assert.NotNull(helpButton);
                Assert.True(helpButton.Flyout?.IsOpen);
                var helpView = Assert.IsType<HelpPopupView>(Assert.IsType<Flyout>(helpButton.Flyout).Content);
                Assert.Equal("Timing", helpView.FindControl<TextBlock>("HelpTitle")?.Text);
                var helpDescription = helpView.FindControl<TextBlock>("HelpDescription");
                Assert.Contains("Timing shows where recorded parse time went",
                    helpDescription?.Text ?? string.Empty);
                var markdownRenderer = Assert.Single(helpView.GetVisualDescendants().OfType<MarkdownRenderer>());
                const string expectedSentence = "More time does not fix a search that reached its step limit";
                const string expectedSection = "Slowest words in Timing";
                var projectionCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                bool TimingIsRendered()
                {
                    if (markdownRenderer.RenderedTextProjection is not { } projection) return false;
                    var text = string.Join("\n", projection.Buffers.Select(buffer => buffer.Text.ToString()));
                    return text.Contains(expectedSentence, StringComparison.Ordinal) &&
                        text.Contains(expectedSection, StringComparison.Ordinal);
                }
                markdownRenderer.PropertyChanged += (_, changed) =>
                {
                    if (changed.Property == MarkdownRenderer.RenderedTextProjectionProperty && TimingIsRendered())
                        projectionCommitted.TrySetResult();
                };
                if (TimingIsRendered()) projectionCommitted.TrySetResult();
                await projectionCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var renderedTextProjection = Assert.IsType<MarkdownTextProjection>(
                    markdownRenderer.RenderedTextProjection);
                var renderedText = string.Join("\n", renderedTextProjection.Buffers.Select(buffer => buffer.Text.ToString()));
                Assert.Contains(expectedSentence, renderedText);
                Assert.Contains(expectedSection, renderedText);
                var help = Assert.IsType<HelpPopupViewModel>(helpView.DataContext);
                Assert.Equal(help.Title, helpView.FindControl<TextBlock>("HelpTitle")?.Text);
                Assert.Equal(help.Description, helpDescription?.Text);
                Assert.Contains(expectedSentence, help.Markdown);
                Assert.Contains(expectedSection, help.Markdown);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void PanGlossHelpFromAnalyzeTextsOpensTheLinguistPageInTheWindow()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                window.Show();
                workspace.Context.OpenTexts(TextsTab.AnalyzeTexts);
                workspace.PageModel<TextsPageModel>().ResultsInText.OpenPanGlossGuideCommand.Execute(null);
                window.UpdateLayout();

                var helpButton = Assert.IsType<Button>(window.FindControl<Button>("HelpButton"));
                Assert.True(helpButton.Flyout?.IsOpen);
                var help = Assert.IsType<HelpPopupViewModel>(
                    Assert.IsType<HelpPopupView>(Assert.IsType<Flyout>(helpButton.Flyout).Content).DataContext);
                Assert.Equal("PanGloss", help.Title);
                Assert.Equal("On Analyze texts, PanGloss runs the project's grammar on selected texts and compares its readings with what FieldWorks stores.",
                    help.Description);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnalyzeTextsWithoutAnAssessmentUsesTheSharedParsePrompt()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                window.Show();
                workspace.Context.Baseline = new WorkspaceBaseline(true, string.Empty, string.Empty,
                    string.Empty, string.Empty, null);
                workspace.Context.OpenTexts(TextsTab.AnalyzeTexts);
                window.UpdateLayout();
                var prompt = Assert.Single(window.GetVisualDescendants().OfType<ParsePrompt>(),
                    candidate => candidate.IsEffectivelyVisible);
                Assert.True(prompt.IsEffectivelyVisible);
                var parse = Assert.Single(prompt.GetVisualDescendants().OfType<Button>());
                Assert.True(parse.IsEffectivelyVisible);
                Assert.Equal(workspace.Context.ParsePromptActionText, parse.Content);

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>());
                Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Parse the words in the selected texts");
                var readButtons = panel.GetLogicalDescendants().OfType<Button>()
                    .Where(button => AutomationProperties.GetName(button) is
                        "Mark selected occurrences as read" or "Mark selected occurrences as unread" or
                        "Mark the selected Text as read" or "Mark the selected Text as unread").ToArray();
                Assert.Equal(4, readButtons.Length);
                Assert.All(readButtons, button => Assert.False(button.IsEffectivelyVisible));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ComposeAttachesEveryPanelBoundToItsOwnChildViewModelAndSetsTheWindowsDataContext()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();

            Assert.Same(workspace, window.DataContext);
            Assert.Equal("Motif (tech demo)", window.Title);
            Assert.Same(workspace.PageModel<OverviewPageModel>(),
                Assert.Single(window.GetLogicalDescendants().OfType<OverviewPage>()).DataContext);
            Assert.Same(workspace.PageModel<WarningsPageModel>().Grammar, Assert.Single(window.GetLogicalDescendants().OfType<GrammarPanel>()).Grammar);
            Assert.Same(workspace.Selection, Assert.Single(window.GetLogicalDescendants().OfType<SelectionPanel>()).Selection);
            Assert.Same(workspace.PageModel<TextsPageModel>().Words, Assert.Single(window.GetLogicalDescendants().OfType<SelectionPanel>()).Words);
            Assert.Same(workspace.PageModel<TextsPageModel>().Assess.Compare,
                Assert.Single(window.GetLogicalDescendants().OfType<ComparePanel>()).Compare);
            Assert.Same(workspace.PageModel<TextsPageModel>().ResultsInText,
                Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>()).InText);
            Assert.Same(workspace.PageModel<TextsPageModel>().TextsLists,
                Assert.Single(window.GetLogicalDescendants().OfType<TextsListsPanel>()).Lists);
            Assert.Same(
                workspace.PageModel<TimingPageModel>().Statistics, Assert.Single(window.GetLogicalDescendants().OfType<StatisticsPanel>()).Statistics);
            Assert.Same(workspace.PageModel<AiHandoffPageModel>().Handoff, Assert.Single(window.GetLogicalDescendants().OfType<HandoffPanel>()).Handoff);
        });
    }

    [Fact]
    public void ReviewChangesShowsTheFieldWorksBackupReminder()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window, _) = NewComposedWindow();
            try
            {
                Assert.Contains("FieldWorks must be closed, or it keeps its own copy. Keep a backup: this is a tech demo.",
                    window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FirstRunNoticeIsVisibleInTheWindow()
    {
        _avalonia.Invoke(() =>
        {
            var notice = new TechDemoNoticeViewModel(new MemoryTechDemoNoticePreferences(), new SucceedingUriLauncher());
            var (_, window, _) = NewComposedWindow(notice);
            try
            {
                window.Show();
                window.UpdateLayout();

                var banner = Assert.IsType<Border>(window.FindControl<Border>("TechDemoNotice"));
                Assert.True(banner.IsVisible);
                Assert.Contains(TechDemoNoticeViewModel.NoticeText,
                    window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text));
                Assert.Contains("Got it", window.GetLogicalDescendants().OfType<Button>()
                    .Select(button => button.Content as string));
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Acknowledge the tech demo notice");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OverviewKeepsItsTileLayoutAndShowsARefreshRefusalOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                const string projectPath = @"C:\projects\aweti.fwdata";
                var fake = (FakeCommandClient)workspace.Context.Commands;
                fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, DateTimeOffset.UtcNow, false));
                var overview = SampleOverview() with { IsStale = true };
                fake.OverviewCompletesWith(overview);
                fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
                    null, EvidenceFreshness.Stale, null, null, null, null, null));
                await workspace.Context.OpenProjectAsync(projectPath);
                Assert.Empty(fake.AssessRequests);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var page = Assert.Single(window.GetLogicalDescendants().OfType<OverviewPage>());
                var text = string.Join("\n", page.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                Assert.Same(overview, workspace.PageModel<OverviewPageModel>().Overview);
                Assert.Contains(@"C:\projects\aweti.fwdata", Assert.Single(fake.OverviewRequests).ProjectPath);
                Assert.DoesNotContain("opened ", text, StringComparison.Ordinal);
                Assert.DoesNotContain("last FieldWorks save", text, StringComparison.Ordinal);
                Assert.Contains("FieldWorks has changed since the Baseline behind these numbers.", text);
                Assert.Equal(2, page.GetVisualDescendants().OfType<OutcomeBar>().Count());
                var overviewModel = workspace.PageModel<OverviewPageModel>();
                Assert.Equal(Verdict.Limit,
                    Assert.Single(overviewModel.TextCoverageSegments, segment => segment.Label == "skipped").Meaning);
                Assert.Equal(Verdict.NoResult,
                    Assert.Single(overviewModel.AccuracySegments, segment => segment.Label == "no parse").Meaning);
                Assert.Equal(Avalonia.Media.FontWeight.Normal, page.GetVisualDescendants().OfType<TextBlock>()
                    .Single(item => item.Text == overviewModel.TextCoverageWords).FontWeight);
                Assert.Equal(Avalonia.Media.FontWeight.Normal, page.GetVisualDescendants().OfType<TextBlock>()
                    .Single(item => item.Text == overviewModel.SpeedMedian).FontWeight);
                var tiles = window.GetLogicalDescendants().OfType<Button>()
                    .Where(item => AutomationProperties.GetName(item) is "Open Text coverage in Texts" or
                        "Open Approved analyses kept in the Matrix" or "Open Speed in Timing" or "Open Grammar warnings in Warnings");
                Assert.All(tiles, tile =>
                {
                    Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, tile.HorizontalAlignment);
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, tile.VerticalAlignment);
                });

                workspace.Context.PublishEvidence(new WorkspaceEvidence(new AssessCommandResponse(
                    new BaselineCaptureResponse(
                        new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                            "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
                        projectPath, DateTimeOffset.UtcNow, false, false),
                    new SelectionProjection([], []), [], "summary"), DateTimeOffset.UtcNow, WasRerun: false));
                workspace.Baseline.FieldWorksHeldProject = true;
                workspace.Baseline.ShownRefusal = WindowRefusal.Plain("FieldWorks still has the project open.");
                window.UpdateLayout();
                var refusalText = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                Assert.Contains("Refresh refused", refusalText);
                Assert.Contains("FieldWorks still has the project open.", refusalText);
                Assert.Contains("FieldWorks holds this project open right now.", refusalText);
                var visibleText = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(item => item.IsEffectivelyVisible).Select(item => item.Text).ToArray();
                Assert.Equal(1, visibleText.Count(item => item?.Contains(
                    "FieldWorks still has the project open.", StringComparison.Ordinal) == true));
                Assert.Equal(1, visibleText.Count(item => item == "FieldWorks holds this project open right now."));
                Assert.Empty(fake.AssessRequests);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void OverviewWithoutStoredAssessmentDoesNotShowZeroesAsResults()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.Context.Baseline = new SIL.Motif.App.ViewModels.WorkspaceBaseline(
                    true, "Captured", "Saved", "Today", "Not held", null);
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
                Assert.Contains("These words haven't been parsed since the last Refresh.", text);
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Parse all words");
                Assert.Contains("No warning summary is available.", text);
                Assert.DoesNotContain("0 of 0", text);
                Assert.DoesNotContain("words hit the step limit", text);
                Assert.Equal(Avalonia.Media.FontWeight.Normal, page.GetVisualDescendants().OfType<TextBlock>()
                    .Single(item => item.Text == "No warning summary is available.").FontWeight);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OverviewSaysWhenTheDefaultSelectionHasNotResolvedAgainstTheBaseline()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.PageModel<OverviewPageModel>().Overview = SampleOverview() with
                {
                    SelectionResolved = false,
                    SelectionWordCount = 0,
                    SelectionTextCount = 1,
                    SelectionAddedWordCount = 0,
                    TextOccurrenceCount = 0,
                };
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var page = Assert.Single(window.GetLogicalDescendants().OfType<OverviewPage>());
                var counts = page.GetVisualDescendants().OfType<UniformGrid>()
                    .Single(item => item.Classes.Contains("overviewCounts"));
                var selectionCell = Assert.IsAssignableFrom<StackPanel>(counts.Children[0]);
                var selectionCount = Assert.IsAssignableFrom<TextBlock>(selectionCell.Children[0]);
                Assert.Equal("not resolved", selectionCount.Text);
                Assert.True(selectionCount.IsEffectivelyVisible);

                var unresolvedNotice = Assert.Single(page.GetVisualDescendants().OfType<CopyableTextBlock>(), item =>
                    item.Text?.Contains("Selection could not be resolved", StringComparison.Ordinal) == true);
                Assert.True(unresolvedNotice.IsEffectivelyVisible);
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

            workspace.Context.ProjectPath = @"C:\projects\one.fwdata";
            workspace.CurrentPage = WorkspacePage.Timing;
            var pickedWords = Assert.Single(window.GetLogicalDescendants().OfType<TextBox>(), input =>
                AutomationProperties.GetName(input) == "Words picked by hand");
            pickedWords.Text = "motifa";
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var pickWords = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Pick words");
            Assert.True(pickWords.IsEffectivelyVisible);
            Assert.True(pickWords.IsEffectivelyEnabled);
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
            var fake = (FakeCommandClient)workspace.Context.Commands;
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

                var warning = new GrammarWarning(
                    GrammarDiagnosticLevel.Warning, "Entry",
                    [new GrammarWarningPart("lex entry", GrammarWarningPartRole.Object,
                        ObjectId: "entry-1", FieldWorksKind: "LexEntry",
                        FieldWorksLink: "silfw://motif.test/project/entry-1")],
                    [new GrammarWarningPart("dropped", GrammarWarningPartRole.Text)],
                    "warning: lex entry: dropped")
                {
                    Group = "Dropped item",
                    Code = "fwdata.dropped-item",
                    Guidance = "Restore the missing item in FieldWorks.",
                };
                const string projectPath = @"C:\projects\aweti.fwdata";
                fake.CheckGrammarCompletesWith(new GrammarCheckResponse([warning], HasBaseline: true));
                var grammarModel = workspace.PageModel<WarningsPageModel>().Grammar;
                grammarModel.LoadStored(projectPath, new GrammarCheckResponse([], HasBaseline: true));
                workspace.CurrentPage = WorkspacePage.Warnings;
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var checkAgain = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Check the grammar again" &&
                    button.IsEffectivelyVisible);
                Assert.True(checkAgain.IsEffectivelyEnabled);
                ClickButton(window, checkAgain);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(projectPath, Assert.Single(fake.CheckGrammarRequests).ProjectPath);
                grammarModel.Warnings.SelectGroupCommand.Execute(
                    Assert.Single(grammarModel.Warnings.WarningGroups));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.Text == "Restore the missing item in FieldWorks.");

                var grammar = window.GetVisualDescendants().OfType<DataGrid>()
                    .Single(grid => AutomationProperties.GetName(grid) == "Grammar warnings");
                AssertSelectableCells(grammar);
                Assert.Contains(grammar.GetVisualDescendants().OfType<HyperlinkButton>(), link =>
                    link.Classes.Contains("warningObjectLink") && link.IsEffectivelyVisible);
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
                        {
                            ProjectStanding = ProjectStanding.Approved,
                            MissedApproved =
                            [
                                new ParserReading([new ParserReadingMorph("kitabu", "book", "n", null, false, null)])
                                {
                                    StoredAnalysisId = "approved-kitabu",
                                    StoredAnalysisOpinion = ReadingGrade.Approved,
                                },
                            ],
                        },
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
                Assert.Equal("1 word: Approved in FieldWorks, PanGloss found no parse",
                    AutomationProperties.GetName(lost));
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
    public async Task TextsMatrixShowsAssessmentWordsWithoutIdentifiers()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                var page = workspace.PageModel<TextsPageModel>();
                var textId = Guid.Parse("55555555-5555-4555-8555-555555555555");
                fake.ListTextWordsCompletesWith(new TextWordsResponse(
                    [new TextWord("motifa", null,
                        [new WordOccurrence(textId, "Alpha", 1, "motifa.", "unanalysed", null)], [], [])],
                    [new TextLines(textId, "Alpha",
                        [new TextLine(1, [new TextToken("motifa", "motifa", null, null)
                            { OccurrenceIndex = 0 }])])],
                    HasBaseline: true));
                await page.Words.SetProjectAsync(@"C:\projects\one.fwdata");
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

                workspace.Context.OpenTexts(TextsTab.Matrix);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<ComparePanel>());
                Assert.True(panel.IsEffectivelyVisible);

                Assert.Equal(4, workspace.Assess.Words.TotalCount);
                var rows = workspace.Assess.Words.Rows.Cast<AssessWordRowViewModel>().ToList();
                Assert.Equal("motif- = first gloss", Assert.Single(rows[0].Readings).Text);
                Assert.True(rows[0].HasTryWordLink);
                Assert.Contains("INCOMPLETE — parsing did not finish (step limit)", rows[1].Detail);
                Assert.Contains("Morphology evidence unavailable.", rows[2].Detail);
                Assert.Contains("Morphology evidence unavailable: invalid shape.", rows[3].Detail);

                var words = panel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Words in the chosen cells");
                Assert.Equal(4, words.ItemCount);
                Assert.Equal(["motifa", "motifb", "motifc", "motifd"],
                    workspace.Assess.Compare.Words.Select(word => word.Word));
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("11111111-1111", StringComparison.Ordinal) == true);

                var open = panel.GetLogicalDescendants().OfType<HyperlinkButton>().Single(button =>
                    AutomationProperties.GetName(button) == "Analyze motifa in its texts");
                Assert.True(open.Command?.CanExecute(open.CommandParameter));
                open.Command!.Execute(open.CommandParameter);
                var selected = workspace.PageModel<TextsPageModel>().ResultsInText.SelectedToken;
                Assert.NotNull(selected);
                Assert.Equal("motif-", Assert.Single(selected.Readings).Morphs.Single().Form);
                window.UpdateLayout();

                Assert.True(selected.IsCardOpen);
                var resultsPanel = Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>());
                var selectionBoxes = resultsPanel.GetLogicalDescendants().OfType<CheckBox>()
                    .Where(checkBox => checkBox.IsEffectivelyVisible &&
                        (AutomationProperties.GetName(checkBox) ?? string.Empty)
                        .StartsWith("Select ", StringComparison.Ordinal))
                    .ToArray();
                Assert.Single(selectionBoxes);
                var strip = resultsPanel.GetLogicalDescendants().OfType<Border>()
                    .Single(control => control.Name == "WordStrip" && ReferenceEquals(control.Tag, selected));
                var cardPopup = Assert.Single(resultsPanel.GetLogicalDescendants().OfType<Popup>(), popup => popup.IsOpen);
                Assert.Same(strip, cardPopup.PlacementTarget);
                Dispatcher.UIThread.RunJobs();
                cardPopup.Child!.UpdateLayout();
                var cardLinks = cardPopup.Child.GetVisualDescendants().OfType<HyperlinkButton>().ToArray();
                Assert.True(cardLinks.Length > 0,
                    $"Card context={cardPopup.Child.DataContext?.GetType().Name ?? "null"}; " +
                    $"reading link={selected.Readings.Single().Morphs.Single().Link}; " +
                    $"morpheme rows={cardPopup.Child.GetLogicalDescendants().OfType<MorphemeRow>().Count()}; " +
                    $"card links={string.Join(", ", cardLinks.Select(button => AutomationProperties.GetName(button)))}.");
                var morphLink = Assert.Single(cardLinks,
                    button => AutomationProperties.GetName(button) == "Open the entry for motif- in FieldWorks");
                Assert.Equal("FW ↗", morphLink.Content);
                Assert.True(morphLink.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(1, morphLink.Opacity);
                Assert.True(morphLink.IsHitTestVisible);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("11111111-1111", StringComparison.Ordinal) == true);
                workspace.CurrentPage = WorkspacePage.Review;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.False(cardPopup.IsOpen);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void MatrixOffersOpinionsForAnAddedWordWithOneChosenAnalysis()
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
                    new SelectionProjection([], []), [], "1 word assessed")
                {
                    Words =
                    [
                        new AssessmentWordResult("pasted-word", "analysed", false, "Search completed", 1, null)
                        {
                            ProjectStanding = ProjectStanding.Approved,
                            Morphology = new ParseWordEvidence(
                                ParseMorphEvidence.Schema, 0, "pasted-word", 1, false, false, false,
                                [new ParseAnalysis([new("11111111-1111-1111-1111-111111111111",
                                    "22222222-2222-2222-2222-222222222222", null, null)])], []),
                        },
                    ],
                };
                workspace.Context.OpenTexts(TextsTab.Matrix);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<ComparePanel>());
                var word = Assert.Single(workspace.Assess.Compare.Words);
                word.IsChecked = true;
                var chooser = Assert.Single(panel.GetLogicalDescendants().OfType<ComboBox>(), candidate =>
                    AutomationProperties.GetName(candidate) == "Analysis for pasted-word");
                Assert.Null(word.SelectedReading);
                chooser.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.Same(chooser.SelectedItem, word.SelectedReading);

                foreach (var kind in new[] { ChangeKinds.Approve, ChangeKinds.Reject, ChangeKinds.Candidate })
                {
                    var button = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), candidate =>
                        Equals(candidate.CommandParameter, kind));
                    Assert.Equal(kind, button.CommandParameter);
                    Assert.True(button.Command?.CanExecute(button.CommandParameter));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FixFirstCollapsesToItsCountAndHighlightsTheFocusedWordWithItsReasonVisible()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                var missed = new ParserReading([new ParserReadingMorph("o-", "up", "v", null, false, null)]);
                var priority = new FixFirstPriority(FixFirstCategory.ApprovedNoParse, 1,
                    "Approved × No parse", "Expected o- up, not built.");
                workspace.Assess.Result = new AssessCommandResponse(
                    new BaselineCaptureResponse(
                        new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                            "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
                        "project.fwdata", DateTimeOffset.UtcNow, false, false),
                    new SelectionProjection([], []), [], "1 word assessed")
                {
                    Words =
                    [
                        new AssessmentWordResult("motifa", "no-analysis", false, "Search completed", 1, null)
                        {
                            ProjectStanding = ProjectStanding.Approved,
                            OccurrenceCount = 3,
                            MissedApproved = [missed],
                            FixFirst = priority,
                        },
                    ],
                };
                workspace.Context.OpenTexts(TextsTab.Matrix);
                window.Show();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<ComparePanel>());
                var list = panel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(control => AutomationProperties.GetName(control) == "Words to check first");
                var disclosure = panel.GetLogicalDescendants().OfType<Expander>()
                    .Single(control => ReferenceEquals(control.Content, list));
                disclosure.IsExpanded = false;
                window.UpdateLayout();

                var count = panel.GetLogicalDescendants().OfType<CopyableTextBlock>()
                    .Single(text => AutomationProperties.GetName(text) == "Fix these first count");
                Assert.Equal("1", count.Text);
                Assert.True(count.IsEffectivelyVisible);
                Assert.False(list.IsEffectivelyVisible);

                disclosure.IsExpanded = true;
                window.UpdateLayout();
                var rowButton = panel.GetLogicalDescendants().OfType<Button>()
                    .Single(button => button.Classes.Contains("fixFirstWord"));
                var explanation = panel.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == priority.Explanation);
                Assert.Equal(TextWrapping.NoWrap,
                    panel.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == priority.Explanation).TextWrapping);

                rowButton.Command!.Execute(rowButton.CommandParameter);
                window.UpdateLayout();

                Assert.Contains("focused", rowButton.Classes);
                Assert.True(Application.Current!.TryGetResource("Intent.Selected.Fill", ThemeVariant.Light, out var selectedFill));
                Assert.Equal(selectedFill, rowButton.Background);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnalyzeTextsSwitchesBetweenTheReaderAndWordList()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                var page = workspace.PageModel<TextsPageModel>();
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                var textId = Guid.Parse("11111111-1111-1111-1111-111111111111");
                var secondTextId = Guid.Parse("22222222-2222-2222-2222-222222222222");
                var stored = new ProjectAnalysis("analysis-1",
                    [new ParserReadingMorph("motif-", "book", "n", null, false, null)]);
                fake.ListTextsCompletesWith(new TextInventoryResponse(
                    [new TextChoiceSummary(textId, "Alpha"), new TextChoiceSummary(secondTextId, "Beta")],
                    HasBaseline: true));
                fake.ListTextWordsCompletesWith(new TextWordsResponse(
                    [new TextWord("kitabu", null,
                        [new WordOccurrence(textId, "Alpha", 1, "kitabu.", "approved", stored)], [stored], [])],
                    [new TextLines(textId, "Alpha",
                        [new TextLine(1, [new TextToken("kitabu", "kitabu", null, "approved")
                            { Analysis = stored }])])],
                    HasBaseline: true));
                await workspace.Selection.SetProjectAsync(@"C:\projects\one.fwdata");
                await page.Words.SetProjectAsync(@"C:\projects\one.fwdata");
                workspace.Assess.Result = AssessedWords(
                    new AssessmentWordResult("kitabu", "no-analysis", false, "Search completed", 1, null));
                workspace.Context.OpenTexts(TextsTab.AnalyzeTexts);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var readText = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Show the text reader");
                var wordList = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Show the word list");
                Assert.True(readText.IsEffectivelyVisible);
                Assert.True(wordList.IsEffectivelyVisible);
                Assert.Equal(AnalyzeTextsView.WordList, wordList.CommandParameter);
                var textChooser = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>(), list =>
                    AutomationProperties.GetName(list) == "Texts to analyze");
                var secondText = Assert.Single(textChooser.GetLogicalDescendants().OfType<CheckBox>(), checkBox =>
                    Equals(checkBox.Content, "Beta"));
                HeadlessClick.Click(window, secondText, "Beta");
                Assert.True(secondText.IsChecked);
                Assert.Contains(secondTextId, workspace.Selection.ChosenTextIds);
                var pageView = Assert.Single(window.GetLogicalDescendants().OfType<TextsPage>());
                var readerHost = pageView.FindControl<ContentControl>("AnalyzeReaderHost");
                var wordListHost = pageView.FindControl<ContentControl>("WordListHost");
                Assert.NotNull(readerHost);
                Assert.NotNull(wordListHost);
                Assert.True(readerHost!.IsEffectivelyVisible);
                Assert.False(wordListHost!.IsEffectivelyVisible);

                ClickButton(window, wordList);
                window.UpdateLayout();
                Assert.Equal(AnalyzeTextsView.WordList, page.AnalyzeView);
                Assert.True(wordListHost.IsEffectivelyVisible);
                Assert.False(readerHost.IsEffectivelyVisible);
                Assert.Equal(1, Assert.Single(wordListHost.GetLogicalDescendants().OfType<ListBox>(), list =>
                    AutomationProperties.GetName(list) == "Words to test").ItemCount);
                Assert.Contains(wordListHost.GetLogicalDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "kitabu");
                var handOff = Assert.Single(wordListHost.GetLogicalDescendants().OfType<Button>(), button =>
                    Equals(button.Content, "AI Handoff"));
                Assert.Same(page.Words.HandOffCheckedWordsCommand, handOff.Command);

                ClickButton(window, readText);
                window.UpdateLayout();
                Assert.False(wordListHost.IsEffectivelyVisible);
                Assert.True(readerHost.IsEffectivelyVisible);
                var lines = Assert.Single(readerHost.GetLogicalDescendants().OfType<ItemsControl>(), control =>
                    AutomationProperties.GetName(control) == "Text lines and parser results");
                Assert.Equal(1, lines.ItemCount);
                Assert.Contains(readerHost.GetLogicalDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "kitabu");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ListsShowSeparateAiHandoffsForTheListAndItsTickedWords()
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
                    new SelectionProjection([], []), [], "1 word assessed")
                {
                    Words =
                    [
                        new AssessmentWordResult("dogs", "no-analysis", false, "Search completed", 1, null)
                        {
                            ProjectStanding = ProjectStanding.Approved,
                        },
                    ],
                };
                var page = workspace.PageModel<TextsPageModel>();
                page.TextsLists.SelectListCommand.Execute(page.TextsLists.Lists.Single(list =>
                    list.Name == "Approved, parsed differently"));
                workspace.Context.OpenTexts(TextsTab.Lists);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<TextsListsPanel>());
                var namedLists = panel.GetLogicalDescendants().OfType<Button>()
                    .Where(button => AutomationProperties.GetName(button)?.StartsWith(
                        "Open the ", StringComparison.Ordinal) == true)
                    .ToArray();
                Assert.Equal(7, namedLists.Length);
                var parsedDifferently = Assert.Single(namedLists, button =>
                    AutomationProperties.GetName(button) == "Open the Approved, parsed differently word list");
                ClickButton(window, parsedDifferently);
                Assert.Equal("Approved, parsed differently", page.TextsLists.SelectedList?.Name);
                Assert.True(Assert.Single(panel.GetLogicalDescendants().OfType<ListBox>(), list =>
                    AutomationProperties.GetName(list) == "Words in the selected list").IsEffectivelyVisible);

                var listHandoff = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "AI Handoff for the whole selected list");
                var checkedHandoff = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "AI Handoff for ticked words in the selected list");

                Assert.Equal("AI Handoff", listHandoff.Content);
                Assert.Equal("AI Handoff", checkedHandoff.Content);
                Assert.Same(page.TextsLists.HandOffListCommand, listHandoff.Command);
                Assert.Same(page.TextsLists.HandOffCheckedWordsCommand, checkedHandoff.Command);

                var changedTab = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "What changed tab");
                ClickButton(window, changedTab);
                window.UpdateLayout();
                Assert.Equal(TextsTab.WhatChanged, page.Tab);
                var pageView = Assert.Single(window.GetLogicalDescendants().OfType<TextsPage>());
                Assert.True(pageView.FindControl<ContentControl>("DifferenceHost")!.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ClickingAddWordsHeaderOpensThePastedWordsInput()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                workspace.PageModel<TextsPageModel>().ShowTabCommand.Execute(TextsTab.AnalyzeTexts);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var expander = window.GetLogicalDescendants().OfType<Expander>()
                    .Single(control => Equals(control.Header, "Add words"));
                Assert.False(expander.IsExpanded);
                var center = expander.TranslatePoint(
                    new Point(expander.Bounds.Width / 2, expander.Bounds.Height / 2), window)!.Value;
                window.MouseMove(center);
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                window.UpdateLayout();

                Assert.True(expander.IsExpanded);
                Assert.True(window.GetLogicalDescendants().OfType<TextBox>().Single(textBox =>
                    AutomationProperties.GetName(textBox) == "Words to analyze, one per line").IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnalyzeTextsSelectionPanelMeasuresItsResolvedComponentWidth()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                window.Show();
                window.UpdateLayout();

                var page = Assert.Single(window.GetLogicalDescendants().OfType<TextsPage>());
                var selectionHost = Assert.IsType<ContentControl>(page.FindControl<ContentControl>("SelectionHost"));
                var variant = window.ActualThemeVariant;
                Assert.True(Application.Current!.TryGetResource(
                    "Component.TextsPage.SelectionPanelWidth", variant, out var resolvedWidth));

                Assert.Equal(Assert.IsType<double>(resolvedWidth), selectionHost.Bounds.Width);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TextsPageShowsAssessmentRefusalsAndDetails()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.Context.OpenTexts(TextsTab.Matrix);
                workspace.Assess.Refusal = new Refusal(
                    "assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built.",
                    new Dictionary<string, string>
                    {
                        ["parserPath"] = OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss",
                    });
                window.Show();
                window.UpdateLayout();

                var refusal = Assert.Single(window.GetLogicalDescendants().OfType<CopyableTextBlock>(),
                    block => block.Text == workspace.Assess.ShownRefusal!.Sentence);
                Assert.True(refusal.IsEffectivelyVisible);
                Assert.Contains(window.GetLogicalDescendants().OfType<Expander>(),
                    expander => Equals(expander.Header, "Details") && expander.IsEffectivelyVisible);
                Assert.Contains("parserPath: " +
                    (OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss"),
                    workspace.Assess.ShownRefusal!.Details);
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
                    AutomationProperties.GetName(button) == "Drag all AI Handoff files" && button.Focusable);
                var handoffNames = panel.GetLogicalDescendants()
                    .OfType<Control>()
                    .Select(control => AutomationProperties.GetName(control))
                    .OfType<string>()
                    .Where(name => name.Contains("Handoff", StringComparison.Ordinal));
                Assert.All(handoffNames, name => Assert.True(name.Contains("AI Handoff", StringComparison.Ordinal), name));
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Write the AI Handoff folder");

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
    public void CollectionNoticeAppearsInAnalyzeTextsAndLists()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                var changes = workspace.Context.Changes;
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                await changes.OpenProjectAsync(@"C:\projects\one.fwdata");
                fake.PendingPutResponse = new PendingChangesSnapshot("draft/one", "revision/one", [], [])
                {
                    ReplacedChangeId = "older",
                };
                await changes.PutAsync(new ChangeIntent("newer", "reject", "wordform/one", "motifa"));
                var notice = Assert.IsType<string>(changes.CollectionNotice);
                workspace.Assess.Result = AssessedWords();

                window.Show();
                window.ApplyTemplate();
                workspace.Context.OpenTexts(TextsTab.AnalyzeTexts);
                window.UpdateLayout();
                var analyzePanel = Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>());
                Assert.Contains(analyzePanel.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    block.Text == notice && block.IsEffectivelyVisible);

                workspace.Context.OpenTexts(TextsTab.Lists);
                window.UpdateLayout();
                var listsPanel = Assert.Single(window.GetLogicalDescendants().OfType<TextsListsPanel>());
                Assert.Contains(listsPanel.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    block.Text == notice && block.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(5));
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
                Avalonia.Input.Platform.IClipboard clipboard = window.Clipboard
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

    private static void ClickButton(Window window, Button button)
    {
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
    }

    private static AssessCommandResponse AssessedWords(params AssessmentWordResult[] words) => new(
        new BaselineCaptureResponse(
            new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
            "project.fwdata", DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], $"{words.Length} words assessed")
    {
        Words = words,
    };

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
    private static void ShowEveryStage(MainWindow window, WorkspaceShellViewModel workspace)
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

    private static (WorkspaceShellViewModel Workspace, MainWindow Window, FakeDragSource DragSource)
        NewComposedWindow(TechDemoNoticeViewModel? techDemoNotice = null)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var dragSource = new FakeDragSource();
        var window = new MainWindow();
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new FakeFolderPicker(), dragSource,
            fake, clipboard: new AvaloniaClipboard(window), techDemoNotice: techDemoNotice);

        window.Compose(workspace);
        return (workspace, window, dragSource);
    }

    private static OverviewResponse SampleOverview() => new(
        "Aweti", DateTimeOffset.Parse("2026-09-24T11:02:00Z"), DateTimeOffset.Parse("2026-09-24T10:58:00Z"),
        125, 3, 2, 313, 812, 38, 167, "assessment-1", DateTimeOffset.Parse("2026-09-24T11:04:00Z"), 130,
        "grammar-fingerprint", "selection-fingerprint",
        new OverviewTextCoverage(41, 23, 55, 6, 313, 183)
        {
            OccurrenceCoveragePercent = 58.46,
        },
        new OverviewAccuracy(18, 39, 5, 55, 2, 4, 9, 18)
        {
            ApprovedWordsNoMatch = 2,
            ApprovedWordsNoParse = 3,
            ApprovedWordsUnknown = 14,
            ApprovedWordsSkipped = 2,
        },
        new OverviewTiming(8.4, 123.5, [new SlowWordTiming("miboko", 1000)], 55),
        new OverviewWarningsSummary(6, 1, "environment failed validation", 4)
        {
            WarningCount = 1,
            InformationCount = 5,
        })
    {
        SelectionResolved = true,
        WordCoveragePercent = 32.8,
        ProjectFileName = "aweti.fwdata",
    };

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class MemoryTechDemoNoticePreferences : ITechDemoNoticePreferences
    {
        public bool HasSeenTechDemoNotice { get; private set; }

        public void MarkTechDemoNoticeSeen() => HasSeenTechDemoNotice = true;
    }

    private sealed class SucceedingUriLauncher : IUriLauncher
    {
        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(true);
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
