using System.Text.Json;
using SIL.Motif.Commands.Queries;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Chrome;
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
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Composes <see cref="MainWindow"/> from a <see cref="WorkspaceShellViewModel"/> built over fakes, and
/// checks what a headless platform can actually verify: every panel is attached and bound to its own
/// child view model, every input and button shown on each page carries an accessible name (its own explicit
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
    public void ManyStoppedWordsStayScrollableWithoutCoveringTheWindow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = workspace.Assess.Trace.ParseProgress;
            var running = progress.TrackAsync(() => finish.Task);
            try
            {
                window.Show();
                progress.Report(new AssessmentProgress(AssessmentStage.Parsing, 30, 40, "Parsing next")
                {
                    CurrentWord = "next", PerWordLimitMs = 1000,
                    StoppedWords = Enumerable.Range(0, 30).Select(index =>
                        new SIL.Motif.Contract.Jobs.StoppedParseWord($"stopped-{index}", "TIMEOUT", 1000)).ToArray(),
                });
                window.FindInWindowOrWorkspace<Button>("StatusParseProgress")!.Flyout!.ShowAt(window.FindInWindowOrWorkspace<Button>("StatusParseProgress")!);
                var stopped = window.GetVisualDescendants().OfType<Expander>().Single(expander =>
                    AutomationProperties.GetAutomationId(expander) == SIL.Motif.App.AutomationIds.ParseStoppedWords);
                stopped.IsExpanded = true;
                PageScreenshots.Settle(window);
                var scroll = Assert.Single(stopped.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.True(scroll.Bounds.Height < window.Bounds.Height / 2);
                scroll.ScrollToEnd();
                PageScreenshots.Settle(window);
                Assert.True(scroll.Offset.Y > 0);
            }
            finally
            {
                finish.SetResult(true);
                await running;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void ReportAProblemDuringAStalledParseOpensAReviewPreview()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var launcher = new RecordingParseLauncher();
            var clock = new SIL.Motif.Tests.TestFixtures.FixedClock(DateTimeOffset.UtcNow);
            var (workspace, window, _) = NewComposedWindow(launcher: launcher, clock: clock);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = workspace.Assess.Trace.ParseProgress;
            var running = progress.TrackAsync(() => finish.Task);
            try
            {
                window.Show();
                progress.Report(new AssessmentProgress(AssessmentStage.Parsing, 0, 1, "Parsing held")
                    { CurrentWord = "held", PerWordLimitMs = 1000 });
                clock.Advance(TimeSpan.FromSeconds(12));
                progress.Report(new AssessmentProgress(AssessmentStage.Parsing, 0, 1, "Parsing held")
                    { CurrentWord = "held", PerWordLimitMs = 1000 });
                PageScreenshots.Settle(window);
                window.FindInWindowOrWorkspace<Button>("StatusParseProgress")!.Flyout!.ShowAt(window.FindInWindowOrWorkspace<Button>("StatusParseProgress")!);
                PageScreenshots.Settle(window);
                var report = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Report a problem with parsing");
                Assert.True(report.IsEffectivelyVisible);
                ClickButton(window, report);
                var preview = Assert.IsType<ProblemReportPreviewWindow>(window.CurrentProblemReportPreview);
                Assert.Contains("Operation: measure words", preview.Model.ReportText, StringComparison.Ordinal);
                Assert.Null(launcher.Opened);
                preview.Close();
            }
            finally
            {
                finish.SetResult(true);
                await running;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(20));
    }

    private sealed class RecordingParseLauncher : IUriLauncher
    {
        public Uri? Opened { get; private set; }
        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Opened = uri;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task F1OpensHelpForTheCurrentPage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            workspace.Context.Diagnostics = true;
            workspace.CurrentPage = WorkspacePage.Diagnostics;
            try
            {
                window.Show();
                window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var helpButton = window.FindInWindowOrWorkspace<Button>("HelpButton");
                Assert.NotNull(helpButton);
                Assert.True(helpButton.Flyout?.IsOpen);
                var helpView = Assert.IsType<HelpPopupView>(Assert.IsType<Flyout>(helpButton.Flyout).Content);
                Assert.Equal("Diagnostics", helpView.FindControl<TextBlock>("HelpTitle")?.Text);
                var helpDescription = helpView.FindControl<TextBlock>("HelpDescription");
                Assert.False(helpDescription?.IsVisible);
                var markdownRenderer = Assert.Single(helpView.GetVisualDescendants().OfType<MarkdownRenderer>());
                const string expectedSentence = "Opening the page does not start a parse or a grammar check.";
                var projectionCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                bool DiagnosticsIsRendered()
                {
                    if (markdownRenderer.RenderedTextProjection is not { } projection) return false;
                    var text = string.Join("\n", projection.Buffers.Select(buffer => buffer.Text.ToString()));
                    return text.Contains(expectedSentence, StringComparison.Ordinal);
                }
                markdownRenderer.PropertyChanged += (_, changed) =>
                {
                    if (changed.Property == MarkdownRenderer.RenderedTextProjectionProperty && DiagnosticsIsRendered())
                        projectionCommitted.TrySetResult();
                };
                if (DiagnosticsIsRendered()) projectionCommitted.TrySetResult();
                await projectionCommitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var renderedTextProjection = Assert.IsType<MarkdownTextProjection>(
                    markdownRenderer.RenderedTextProjection);
                var renderedText = string.Join("\n", renderedTextProjection.Buffers.Select(buffer => buffer.Text.ToString()));
                Assert.Contains(expectedSentence, renderedText);
                Assert.DoesNotContain(renderedTextProjection.Buffers,
                    buffer => buffer.Text.ToString().Trim() == "Diagnostics");
                var help = Assert.IsType<HelpPopupViewModel>(helpView.DataContext);
                Assert.Equal(help.Title, helpView.FindControl<TextBlock>("HelpTitle")?.Text);
                Assert.Equal(help.Description, helpDescription?.Text);
                Assert.Contains(expectedSentence, help.Markdown);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void WindowChromeKeepsNativeCaptionControlsAndMarksTheTopBarAsDraggable()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window, _) = NewComposedWindow();
            try
            {
                var topBar = Assert.IsType<Border>(window.FindInWindowOrWorkspace<Border>("TopBar"));
                var projectMenu = Assert.IsType<Menu>(window.FindInWindowOrWorkspace<Menu>("MainMenu"));
                var chip = Assert.IsType<Button>(window.FindInWindowOrWorkspace<Button>("ProjectChip"));
                var refresh = Assert.IsType<Button>(window.FindInWindowOrWorkspace<Button>("RefreshIconButton"));

                Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(topBar));
                Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(projectMenu));
                Assert.All(new[] { chip, refresh }, button =>
                    Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(button)));
                Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(),
                    window.ExtendClientAreaToDecorationsHint);
                Assert.Equal(OperatingSystem.IsWindows(), topBar.Classes.Contains("windowsChrome"));
                Assert.Equal(OperatingSystem.IsMacOS(), topBar.Classes.Contains("macChrome"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RecentProjectSummaryDoesNotMoveFileMenuActionsWhenItLoads()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                await workspace.RefreshKnownProjectsAsync();
                window.Show();
                window.OpenFileMenu();
                PageScreenshots.Settle(window);
                var configure = Assert.IsType<MenuItem>(window.FindInWindowOrWorkspace<MenuItem>("ConfigureMenuItem"));
                var before = BoundsInTopLevel(configure);

                const string projectName = "Recent";
                workspace.Project.KnownProjects.Add(new KnownProjectSummary(
                    Path.Combine(Path.GetTempPath(), projectName + ".fwdata"), DateTimeOffset.UtcNow));
                PageScreenshots.Settle(window);

                Assert.Contains(window.RecentProjectItems, item =>
                    AutomationProperties.GetName(item) == "Open " + projectName);
                var after = BoundsInTopLevel(configure);
                Assert.Equal((before.X, before.Y), (after.X, after.Y));
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void SidebarFooterKeepsHelpAndSettingsButtonsDiscoverableAndWorking()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            window.Width = 1280;
            try
            {
                window.Show();
                window.UpdateLayout();
                var sidebar = Assert.IsType<Border>(window.FindInWindowOrWorkspace<Border>("Sidebar"));
                var footer = Assert.IsType<StackPanel>(window.FindInWindowOrWorkspace<StackPanel>("SidebarFooter"));
                var utilities = Assert.IsType<StackPanel>(window.FindInWindowOrWorkspace<StackPanel>("SidebarUtilities"));
                var help = Assert.IsType<Button>(window.FindInWindowOrWorkspace<Button>("HelpButton"));
                var settings = Assert.IsType<Button>(window.FindInWindowOrWorkspace<Button>("SettingsButton"));

                Assert.Contains(help.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, sidebar));
                Assert.Contains(settings.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, sidebar));
                Assert.Contains(help.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, footer));
                Assert.Contains(settings.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, footer));
                Assert.Equal(AutomationIds.HelpButton, AutomationProperties.GetAutomationId(help));
                Assert.Equal(AutomationIds.SettingsButton, AutomationProperties.GetAutomationId(settings));
                Assert.True(window.FindInWindowOrWorkspace<TextBlock>("SidebarSettingsLabel")!.IsEffectivelyVisible);
                Assert.Equal("Help for the current page (F1)", ToolTip.GetTip(help));
                Assert.Equal("Settings (Ctrl+,)", ToolTip.GetTip(settings));

                window.Width = 960;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(workspace.IsSidebarCollapsed);
                Assert.Equal(Avalonia.Layout.Orientation.Vertical, utilities.Orientation);
                Assert.True(help.IsEffectivelyVisible);
                Assert.False(window.FindInWindowOrWorkspace<TextBlock>("SidebarHelpLabel")?.IsEffectivelyVisible ?? true);
                Assert.False(window.FindInWindowOrWorkspace<TextBlock>("SidebarSettingsLabel")?.IsEffectivelyVisible ?? true);
                Assert.True(settings.IsEffectivelyVisible);

                var before = workspace.CurrentPage;
                ClickButton(window, settings);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                ClickButton(window, SettingsTestAccess.Page(window).FindControl<Button>("SettingsClose")!);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(before, workspace.CurrentPage);
                ClickButton(window, help);
                Dispatcher.UIThread.RunJobs();
                Assert.True(help.Flyout?.IsOpen);
            }
            finally
            {
                window.Close();
            }
        });
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
                workspace.Context.OpenPage(WorkspacePage.Texts);
                workspace.PageModel<TextsPageModel>().ResultsInText.OpenPanGlossGuideCommand.Execute(null);
                window.UpdateLayout();

                var helpButton = Assert.IsType<Button>(window.FindInWindowOrWorkspace<Button>("HelpButton"));
                Assert.True(helpButton.Flyout?.IsOpen);
                var help = Assert.IsType<HelpPopupViewModel>(
                    Assert.IsType<HelpPopupView>(Assert.IsType<Flyout>(helpButton.Flyout).Content).DataContext);
                Assert.Equal("PanGloss", help.Title);
                Assert.Equal("In Texts, PanGloss runs the project's grammar on selected texts and compares its analyses with what FieldWorks stores.",
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
                workspace.Context.OpenPage(WorkspacePage.Texts);
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
                var readMenu = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Mark read or unread");
                Assert.False(readMenu.IsEffectivelyVisible);
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
            using var closer = window.CloseOnExit();
            workspace.Context.Diagnostics = true;
            window.EnsureAllPageViews();
            window.RealizeLazyContent();

            Assert.Same(workspace, window.DataContext);
            Assert.Equal("Motif (tech demo)", window.Title);
            Assert.Same(workspace.PageModel<ProgressPageModel>(),
                window.PageView<ProgressPage>().DataContext);
            Assert.Same(workspace.PageModel<DiagnosticsPageModel>(),
                window.PageView<DiagnosticsPage>().DataContext);
            // The frame attaches only the current page's side panel, which holds the Texts selection.
            workspace.CurrentPage = WorkspacePage.Texts;
            Assert.Same(workspace.Selection, Assert.Single(window.GetLogicalDescendants().OfType<SourcesPanel>()).Selection);
            Assert.Same(workspace.PageModel<TextsPageModel>().Words, Assert.Single(window.GetLogicalDescendants().OfType<SourcesPanel>()).Words);
            Assert.Same(workspace.PageModel<TextsPageModel>().ResultsInText,
                Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>()).InText);
            Assert.Same(workspace.PageModel<TextsPageModel>().Words, workspace.PageModel<WordsPageModel>().Words);
            Assert.Contains(window.GetLogicalDescendants().OfType<HandoffPanel>(),
                panel => ReferenceEquals(workspace.PageModel<SettingsPageModel>().Handoff.Handoff, panel.Handoff));
        });
    }

    [Fact]
    public void FirstRunNoticeIncludesTheFieldWorksBackupReminder()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window, _) = NewComposedWindow(PageScreenshots.FirstRunNotice());
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var note = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == TechDemoNoticeViewModel.NoticeText);
                Assert.True(note.IsEffectivelyVisible, "The backup reminder is hidden.");
                Assert.Contains("back up FieldWorks", note.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TechDemoPromiseAndProblemReportRemainVisibleInTheStatusBar()
    {
        _avalonia.Invoke(() =>
        {
            var notice = new TechDemoNoticeViewModel(new MemoryTechDemoNoticePreferences());
            var (_, window, _) = NewComposedWindow(notice);
            try
            {
                window.Show();
                window.UpdateLayout();

                var status = Assert.IsType<Border>(window.FindInWindowOrWorkspace<Border>("StatusBar"));
                Assert.True(status.IsEffectivelyVisible);
                Assert.Contains(status.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text?.StartsWith("Tech demo · v", StringComparison.Ordinal) == true);
                Assert.Contains(status.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Report a problem");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheFirstRunTechDemoNoticeShowsAboveThePageUntilItIsAcknowledged()
    {
        _avalonia.Invoke(() =>
        {
            var notice = new TechDemoNoticeViewModel(new MemoryTechDemoNoticePreferences());
            var (_, window, _) = NewComposedWindow(notice);
            try
            {
                window.Show();
                window.UpdateLayout();

                var banner = Assert.IsType<Border>(window.FindInWindowOrWorkspace<Border>("TechDemoNotice"));
                Assert.True(banner.IsEffectivelyVisible);
                Assert.Contains(TechDemoNoticeViewModel.NoticeText,
                    banner.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                var pageHost = Assert.IsType<Panel>(window.FindInWindowOrWorkspace<Panel>("PageHost"));
                Assert.True(banner.TranslatePoint(new Point(0, banner.Bounds.Height), window)!.Value.Y <=
                    pageHost.TranslatePoint(new Point(0, 0), window)!.Value.Y + 0.5, "The notice sits above the page.");
                var gotIt = Assert.Single(banner.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Acknowledge the tech demo notice");
                Assert.True(gotIt.IsEffectivelyVisible);

                gotIt.Command!.Execute(gotIt.CommandParameter);
                window.UpdateLayout();
                Assert.False(banner.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ProgressHidesStaleHealthFactsAndShowsARefreshRefusalOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                const string projectPath = @"C:\projects\aweti.fwdata";
                var fake = (FakeCommandClient)workspace.Context.Commands;
                var baselineSave = new DateTimeOffset(2026, 3, 2, 9, 15, 0, TimeSpan.Zero);
                var laterSave = baselineSave.AddHours(1);
                var baselineToken = new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                    "2026-03-02T09:15:00Z", "sha256:" + new string('b', 64));
                fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(baselineToken, baselineSave, false)
                {
                    ProjectLastWriteUtc = laterSave,
                });
                var overview = SampleOverview() with { IsStale = true };
                fake.OverviewCompletesWith(overview);
                fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow,
                    laterSave, EvidenceFreshness.Stale, null, null, null, null, null));
                await workspace.Context.OpenProjectAsync(projectPath);
                Assert.Empty(fake.AssessRequests);
                Assert.Equal(ProjectFreshness.SavedSince, workspace.Freshness);
                await workspace.Context.Setup!.SkipCommand.ExecuteAsync(null);
                Assert.False(workspace.Context.Setup.IsOpen);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                Assert.Same(overview, workspace.PageModel<ProgressPageModel>().Health.Overview);
                Assert.False(workspace.Context.Health.HasWarnings);
                Assert.False(workspace.Context.Health.HasMedianWork);
                Assert.Equal(2, fake.OverviewRequests.Count);
                Assert.All(fake.OverviewRequests, request => Assert.Equal(projectPath, request.ProjectPath));
                Assert.DoesNotContain("opened ", text, StringComparison.Ordinal);
                Assert.DoesNotContain("last FieldWorks save", text, StringComparison.Ordinal);
                var cardForText = ProjectCardAccess.Open(window);
                var withCard = text + "\n" + string.Join("\n", cardForText.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                ProjectCardAccess.Close(window);
                Assert.Contains("FieldWorks saved", withCard);
                Assert.Contains("Baseline of", withCard);
                Assert.Contains("saved", withCard);
                Assert.DoesNotContain("FieldWorks has changed since the Baseline behind these numbers.", withCard);
                Assert.True(workspace.FreshnessIsStale);
                var card = ProjectCardAccess.Open(window);
                var freshnessLabel = card.GetVisualDescendants().OfType<CopyableTextBlock>().Single(block =>
                    AutomationProperties.GetAutomationId(block) == AutomationIds.FreshnessLabel);
                var freshnessDetail = card.GetVisualDescendants().OfType<CopyableTextBlock>().Single(block =>
                    AutomationProperties.GetAutomationId(block) == AutomationIds.FreshnessDetail);
                Assert.Equal(workspace.FreshnessLabel, freshnessLabel.Text);
                Assert.Equal(workspace.FreshnessDetail, freshnessDetail.Text);
                ProjectCardAccess.Close(window);
                workspace.Context.PublishEvidence(new WorkspaceEvidence(new AssessCommandResponse(
                    new BaselineCaptureResponse(
                        new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                            "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
                        projectPath, DateTimeOffset.UtcNow, false, false),
                    new SelectionProjection([], []), [], "summary"), DateTimeOffset.UtcNow, WasRerun: false));
                PageScreenshots.Settle(window);
                workspace.Baseline.FieldWorksHeldProject = true;
                workspace.Baseline.ShownRefusal = WindowRefusal.Plain("FieldWorks still has the project open.");
                window.UpdateLayout();
                var noticeBand = window.FindInWindowOrWorkspace<Border>("NoticeBand")!;
                Assert.True(noticeBand.IsEffectivelyVisible);
                Assert.Equal(28, noticeBand.Bounds.Height);
                Assert.Equal(workspace.Baseline.ShownRefusal.Sentence,
                    window.FindInWindowOrWorkspace<TextBlock>("NoticeSummary")!.Text);
                PageScreenshots.Settle(window);
                Assert.True(noticeBand.IsEffectivelyVisible,
                    $"Refusal after settling: {workspace.Baseline.ShownRefusal?.Sentence ?? "none"}");
                var detailsButton = window.FindInWindowOrWorkspace<Button>("NoticeDetailsButton")!;
                ClickButton(window, detailsButton);
                PageScreenshots.Settle(window);
                Assert.True(detailsButton.Flyout!.IsOpen);
                var detail = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(detailsButton.Flyout).Content);
                var refusalText = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                Assert.Contains("Refresh refused", refusalText);
                Assert.Contains("FieldWorks still has the project open.", refusalText);
                Assert.Equal("Refresh refused", workspace.FreshnessLabel);
                Assert.Empty(workspace.FreshnessDetail);
                Assert.DoesNotContain("FieldWorks holds this project open right now.", refusalText);
                var visibleText = detail.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(item => item.IsEffectivelyVisible).Select(item => item.Text).ToArray();
                Assert.Equal(1, visibleText.Count(item => item?.Contains(
                    "FieldWorks still has the project open.", StringComparison.Ordinal) == true));
                Assert.DoesNotContain("FieldWorks holds this project open right now.", visibleText);
                Assert.Empty(fake.AssessRequests);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ProgressWithoutStoredAssessmentDoesNotShowZeroesAsResults()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.Context.Baseline = new SIL.Motif.App.ViewModels.WorkspaceBaseline(
                    true, "Captured", "Saved", "Today", "Not held", null);
                workspace.Context.Health.Overview = SampleOverview() with
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

                var page = window.PageView<ProgressPage>();
                var text = string.Join("\n", page.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                Assert.True(workspace.Context.NeedsAssessment);
                var card = ProjectCardAccess.Open(window);
                Assert.Contains(card.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Parse all words");
                ProjectCardAccess.Close(window);
                Assert.False(workspace.Context.Health.HasWarnings);
                Assert.False(workspace.Context.Health.HasMedianWork);
                Assert.DoesNotContain("0 of 0", text);
                Assert.DoesNotContain("words hit the parser work limit", text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ProgressHidesCountsWhenTheDefaultSelectionHasNotResolvedAgainstTheBaseline()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                fake.DefaultSelectionCompletesWith(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-1111-1111-1111-111111111111")], [], string.Empty, string.Empty,
                    1000, SIL.Motif.Contract.Assess.ParserWorkLimit.Default));
                workspace.Context.ProjectPath = @"C:\projects\one.fwdata";
                workspace.Context.Baseline = new WorkspaceBaseline(true, string.Empty, string.Empty,
                    string.Empty, string.Empty, null);
                await workspace.Context.Setup!.ProjectOpenedAsync(@"C:\projects\one.fwdata");
                workspace.Context.Health.Overview = SampleOverview() with
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

                Assert.False(workspace.Context.Health.HasSelectionSummary);
                Assert.Equal(string.Empty, workspace.Context.Health.SelectionSummary);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void EveryInputAndButtonHasAnAccessibleName()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            using var closer = window.CloseOnExit();
            workspace.Context.Diagnostics = true;
            window.Show();
            foreach (var page in PageRegistry.Entries.Select(entry => entry.Page))
            {
                workspace.CurrentPage = page;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var controls = window.GetLogicalDescendants().OfType<Control>()
                    .Where(control => control.IsAttachedToVisualTree() && control.IsEffectivelyVisible && control is
                        Button or HyperlinkButton or CheckBox or ComboBox or TextBox or NumericUpDown or DataGrid)
                    .ToList();
                Assert.NotEmpty(controls);
                foreach (var control in controls)
                    Assert.False(string.IsNullOrWhiteSpace(EffectiveAccessibleName(control)),
                        $"{page}: {control.GetType().Name} (content '{(control as ContentControl)?.Content}') has no accessible name. " +
                        string.Join(" <- ", control.GetLogicalAncestors().OfType<Control>().Select(parent => $"{parent.GetType().Name}#{parent.Name}")));
            }
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
                workspace.Context.Diagnostics = true;
                window.Show();
                var inspected = 0;
                foreach (var page in PageRegistry.Entries.Select(entry => entry.Page))
                {
                    workspace.CurrentPage = page;
                    window.UpdateLayout();

                    var readOnlyText = window.GetVisualDescendants().OfType<TextBlock>()
                        .Where(text => text.IsEffectivelyVisible)
                        .Where(text => !IsWithinInteractiveControl(text))
                        .Where(text => !string.IsNullOrEmpty(text.Text))
                        .ToList();

                    inspected += readOnlyText.Count;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowCloseKeepsAnUnsavedComment(bool existing)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            var fake = (FakeCommandClient)workspace.Context.Commands;
            var card = new ProposalCard("p/one", "One", "proposed", ProposalGroup.WaitingForYou, false, [],
                TrialStanding.NotTried, null, []);
            fake.ProposalStandingsCompleteWith(new ProposalStandings([card], 0));
            var token = new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64));
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([], HasBaseline: true));
            fake.DefaultSelectionResponseIs(new DefaultSelectionResponse(null, SetupSkipped: true));
            fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
            var comment = new CommentView(1, "agent", "Agent", "", "Old", null, "now", null, false, null);
            fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                new ProposalDetailProjection(request.ProposalId, "proposed", "Label", null, null, [],
                    Comments: existing ? [comment] : [])));
            await workspace.SetProjectAsync(@"C:\projects\one.fwdata", captureFreshProject: false);
            var page = workspace.PageModel<ProposalsPageModel>();
            await page.DetailReading;
            if (!existing) page.AddCommentCommand.Execute(null);
            var editor = Assert.Single(page.Comments);
            if (existing) editor.EditCommand.Execute(null);
            editor.Document.Paragraphs[0].Runs[0].Text += " typed";
            var pending = new TaskCompletionSource<CommandOutcome<ProposalDetailProjection>>();
            fake.ShowProposalHandler = (_, _) => pending.Task;
            await workspace.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);
            window.Show();
            window.Close();
            Assert.True(window.IsVisible);
            Assert.True(editor.IsSwitchRefused);
            pending.SetResult(CommandOutcome<ProposalDetailProjection>.Success(
                new ProposalDetailProjection("p/one", "proposed", "Label", null, null, [],
                    Comments: existing ? [comment] : [])));
            await page.DetailReading;
            Assert.Contains(editor, page.Comments);
            fake.ProposalStandingsCompleteWith(new ProposalStandings([], 1));
            fake.ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                new ProposalDetailProjection("p/one", "rejected", "Label", null, null, [],
                    Comments: existing ? [comment] : [])));
            await workspace.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);
            await page.DetailReading;
            Assert.Equal("p/one", page.SelectedProposal?.ProposalId);
            Assert.Contains(editor, page.Comments);
            Assert.False(editor.SaveCommand.CanExecute(null));
            window.Close();
            Assert.True(window.IsVisible);
            editor.CancelCommand.Execute(null);
            window.Close();
            Assert.False(window.IsVisible);
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void DiagnosticsRendersSelectableWarningTextAndRecordedFieldWorksLinks()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            var fake = (FakeCommandClient)workspace.Context.Commands;
            var warning = new GrammarWarning(
                GrammarDiagnosticLevel.Warning, "Entry",
                [new GrammarWarningPart("lex entry", GrammarWarningPartRole.Object,
                    ObjectId: "entry-1", FieldWorksKind: "LexEntry",
                    FieldWorksLink: "silfw://motif.test/project/entry-1") { FieldWorksTool = "lexiconEdit" }],
                [], "warning: lex entry: dropped")
            {
                Group = "Dropped item",
                Code = "fwdata.dropped-item",
                Description = "PanGloss reports a dropped item.",
            };
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([warning], HasBaseline: true));
            workspace.Context.Diagnostics = true;
            try
            {
                await workspace.SetProjectAsync(@"C:\projects\aweti.fwdata", captureFreshProject: false);
                workspace.Context.OpenDiagnostics(DiagnosticsTab.Warnings);
                window.Show();
                PageScreenshots.Settle(window);
                var list = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(),
                    item => item.Name == "DiagnosticWarningRows");
                var link = Assert.Single(list.GetVisualDescendants().OfType<HyperlinkButton>());
                Assert.Equal(new Uri("silfw://motif.test/project/entry-1"), link.NavigateUri);
                var message = Assert.Single(list.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == "PanGloss reports a dropped item.");
                message.SelectAll();
                Assert.Equal(message.Text, message.SelectedText);
                Assert.Empty(fake.AssessRequests);
                Assert.Empty(fake.CheckGrammarRequests);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void CopyableTextStillLetsClickableParentsReceivePointerEvents()
    {
        _avalonia.Invoke(() =>
        {
            var text = new CopyableTextBlock { Text = "select or click me" };
            var parent = new Border { Child = text };
            var window = new MotifTestWindow { Content = parent };
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
            Assert.Equal("entry", Assert.IsType<TextBlock>(link.Content).Text);
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

    [Theory]
    [InlineData("PhEnvironment", "EnvironmentEdit", "Open this item in Environments")]
    [InlineData("MoForm", "lexiconEdit", "Open this item in Lexicon Edit")]
    [InlineData("PhRegularRule", "PhonologicalRuleEdit", "Open this item in Phonological Rules")]
    [InlineData("MoInflAffixSlot", "posEdit", "Open this item in Category Edit")]
    [InlineData(null, null, "Open this item in FieldWorks")]
    public void AGrammarWarningLinkNamesTheToolItOpens(string? kind, string? tool, string tip)
    {
        _avalonia.Invoke(() =>
        {
            var block = new GrammarWarningPartsBlock
            {
                Parts =
                [
                    new GrammarWarningPart("e2", GrammarWarningPartRole.Object, "id", kind, "silfw://localhost/link")
                        { FieldWorksTool = tool },
                ],
            };

            Assert.Equal(tip, ToolTip.GetTip(Assert.Single(block.Children.OfType<HyperlinkButton>())));
        });
    }

    [Theory]
    [InlineData("https://example.org/item")]
    [InlineData("not a uri")]
    public void AWarningWithAnUnverifiedTargetKeepsItsNameWithoutALink(string target)
    {
        _avalonia.Invoke(() =>
        {
            var block = new GrammarWarningPartsBlock
            {
                Parts = [new GrammarWarningPart("entry", GrammarWarningPartRole.Object, "id", "lex entry", target)],
            };
            Assert.Empty(block.Children.OfType<HyperlinkButton>());
            Assert.Equal("entry", Assert.Single(block.Children.OfType<CopyableTextBlock>()).Text);
        });
    }

    [Fact]
    public void UnavailableGrammarWarningLinksKeepTheSubjectNameReadable()
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

            Assert.Equal("entry", Assert.IsType<CopyableTextBlock>(Assert.Single(block.Children)).Text);
        });
    }

    [Fact]
    public void SwitchingTheThemeVariantWithARefusalAndAnInProgressStateBoundRaisesNoException()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            using var closer = window.CloseOnExit();
            ((IProgress<AssessmentProgress>)workspace.Assess).Report(
                new AssessmentProgress(AssessmentStage.Parsing, 1, 2, "Parsing the Selection..."));
            workspace.Assess.Refusal = new Refusal(
                "assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built.");
            workspace.PageModel<SettingsPageModel>().Handoff.Handoff.Refusal = new Refusal(
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
    public async Task WordsAndReaderShowAssessmentEvidenceWithoutIdentifiers()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
                var page = workspace.PageModel<TextsPageModel>();
                var textId = Guid.Parse("55555555-5555-4555-8555-555555555555");
                var captured = new TextWordsResponse(
                    [new TextWord("motifa", null,
                        [new WordOccurrence(textId, "Alpha", 1, "motifa.", "unanalysed", null)], [], [])],
                    [new TextLines(textId, "Alpha",
                        [new TextLine(1, [new TextToken("motifa", "motifa", null, null)
                            { OccurrenceIndex = 0 }])])],
                    HasBaseline: true);
                fake.ListTextsCompletesWith(new TextInventoryResponse(
                    [new TextChoiceSummary(textId, "Alpha")], HasBaseline: true));
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
                            "INCOMPLETE — parsing did not finish (work limit)", 700, "partial")
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

                var assessment = CapturedAssessment(workspace.Assess.Result!);
                await using var fixture = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake,
                    workspace.Context, captured, assessment, assessment.Words.Select(word => word.Word).ToArray());
                workspace.Assess.Result = assessment;
                await workspace.Context.EvidencePublication;
                workspace.Context.OpenPage(WorkspacePage.Words);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<TextWordsPanel>());
                Assert.True(panel.IsEffectivelyVisible);

                Assert.Equal(4, workspace.Assess.Words.TotalCount);
                var rows = workspace.Assess.Words.Rows.Cast<AssessWordRowViewModel>().ToList();
                Assert.Equal("motif- = first gloss", Assert.Single(rows[0].Readings).Text);
                Assert.True(rows[0].HasTryWordLink);
                Assert.Contains("INCOMPLETE — parsing did not finish (work limit)", rows[1].Detail);
                Assert.Contains("Morphology evidence unavailable.", rows[2].Detail);
                Assert.Contains("Morphology evidence unavailable: invalid shape.", rows[3].Detail);

                var words = panel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == MotifAppStrings.WordsToTestAccessibleName);
                Assert.Equal(5, words.ItemCount);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("11111111-1111", StringComparison.Ordinal) == true);

                workspace.Context.OpenWord("motifa");
                await page.ResultsInText.SelectionRefresh;
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                var selected = page.ResultsInText.SelectedToken;
                Assert.NotNull(selected);
                Assert.Equal("motif-", Assert.Single(selected.Readings).Morphs.Single().Form);
                window.UpdateLayout();

                Assert.True(selected.IsCardOpen);
                var resultsPanel = Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>());
                CheckBox[] SelectionBoxes() => resultsPanel.GetVisualDescendants().OfType<CheckBox>()
                    .Where(checkBox => checkBox.IsEffectivelyVisible &&
                        (AutomationProperties.GetName(checkBox) ?? string.Empty)
                        .StartsWith("Select ", StringComparison.Ordinal))
                    .ToArray();
                Assert.Empty(SelectionBoxes());
                workspace.PageModel<TextsPageModel>().ResultsInText.ChooseWordsCommand.Execute(null);
                PageScreenshots.Settle(window);
                var strip = resultsPanel.GetVisualDescendants().OfType<SIL.Motif.App.Controls.WordPresentation.WordStripToken>()
                    .Single(control => control.Name == "WordStrip" && ResultsInTextPanel.TokenOf(control)?.Occurrence == selected.Occurrence);
                Assert.True(strip.GetVisualDescendants().OfType<Border>().Any(control =>
                    control.Classes.Contains("selectionGlyph") && control.IsEffectivelyVisible) || SelectionBoxes().Length == 1);
                strip.FocusWord();
                PageScreenshots.Settle(window);
                Assert.Single(SelectionBoxes());
                Assert.DoesNotContain(resultsPanel.GetLogicalDescendants().OfType<Popup>(), popup => popup.IsOpen);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var card = Assert.Single(resultsPanel.GetVisualDescendants()
                    .OfType<SIL.Motif.App.Controls.WordPresentation.WordCard>(), control => control.IsEffectivelyVisible);
                Assert.Same(selected, card.DataContext);
                Assert.True(card.TranslatePoint(new Point(0, 0), strip)!.Value.Y >= strip.Bounds.Height);
                var cardLinks = card.GetVisualDescendants().OfType<HyperlinkButton>().ToArray();
                Assert.True(cardLinks.Length > 0,
                    $"Card context={card.DataContext?.GetType().Name ?? "null"}; " +
                    $"reading link={selected.Readings.Single().Morphs.Single().Link}; " +
                    $"morpheme panels={card.GetLogicalDescendants().OfType<SIL.Motif.App.Controls.WordPresentation.MorphemePanel>().Count()}; " +
                    $"card links={string.Join(", ", cardLinks.Select(button => AutomationProperties.GetName(button)))}.");
                var morphLink = Assert.Single(cardLinks,
                    button => AutomationProperties.GetName(button) == "Edit \"motif-\" in FieldWorks");
                Assert.Equal("Lexicon Edit ↗", morphLink.Content);
                Assert.True(morphLink.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(1, morphLink.Opacity);
                Assert.True(morphLink.IsHitTestVisible);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("11111111-1111", StringComparison.Ordinal) == true);
                workspace.CurrentPage = WorkspacePage.Proposals;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.False(selected.IsCardOpen);
                Assert.Empty(fake.PendingPutRequests);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TextsAndWordsShareTheSelectionWhileNavigationChangesPages()
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
                var captured = new TextWordsResponse(
                    [new TextWord("kitabu", null,
                        [new WordOccurrence(textId, "Alpha", 1, "kitabu.", "approved", stored)], [stored], [])],
                    [new TextLines(textId, "Alpha",
                        [new TextLine(1, [new TextToken("kitabu", "kitabu", null, "approved")
                            { Analysis = stored }])])],
                    HasBaseline: true);
                workspace.Assess.Result = AssessedWords(
                    new AssessmentWordResult("kitabu", "no-analysis", false, "Search completed", 1, null));
                var assessment = CapturedAssessment(workspace.Assess.Result!);
                captured = captured with { Texts = [.. captured.Texts, new TextLines(secondTextId, "Beta", [])] };
                await using var fixture = await WorkspaceContextTests.OpenCapturedSelectionAsync(fake,
                    workspace.Context, captured, assessment);
                workspace.Assess.Result = assessment;
                await workspace.Context.EvidencePublication;
                workspace.Context.OpenPage(WorkspacePage.Texts);
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);

                var textChooser = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>(), list =>
                    AutomationProperties.GetName(list) == "Texts to analyze");
                var secondText = Assert.Single(textChooser.GetLogicalDescendants().OfType<CheckBox>(), checkBox =>
                    AutomationProperties.GetName(checkBox) == "Beta");
                HeadlessClick.Click(window, secondText, "Beta");
                Assert.False(secondText.IsChecked);
                await workspace.Context.EvidencePublication;
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                HeadlessClick.Click(window, secondText, "Beta");
                Assert.True(secondText.IsChecked);
                await workspace.Context.EvidencePublication;
                Assert.Contains(secondTextId, workspace.Selection.ChosenTextIds);
                await page.ResultsInText.SelectionRefresh;
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                var pageView = window.PageView<TextsPage>();
                var readerHost = pageView.FindControl<ContentControl>("ReaderHost")!;
                Assert.True(readerHost.IsEffectivelyVisible);
                var lines = Assert.Single(readerHost.GetLogicalDescendants().OfType<ItemsControl>(), control =>
                    AutomationProperties.GetName(control) == "Text lines and parser results");
                Assert.Equal(1, lines.ItemCount);
                Assert.Contains(readerHost.GetLogicalDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "kitabu");

                workspace.CurrentPage = WorkspacePage.Words;
                PageScreenshots.Settle(window);
                var words = Assert.Single(window.GetVisualDescendants().OfType<TextWordsPanel>());
                Assert.Same(page.Words, words.Words);
                Assert.Equal(1, Assert.Single(words.GetLogicalDescendants().OfType<ListBox>(), list =>
                    AutomationProperties.GetName(list) == MotifAppStrings.WordsToTestAccessibleName).ItemCount);
                await page.Words.SettleVisibleDetailsAsync();
                PageScreenshots.Settle(window);
                Assert.Contains(words.GetLogicalDescendants().OfType<CopyableTextBlock>(), text => text.Text == "kitabu");

                workspace.CurrentPage = WorkspacePage.Texts;
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                Assert.True(readerHost.IsEffectivelyVisible);

            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void HandoffKeepsTheRequestedWordsInSettings()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, _) = NewComposedWindow();
            ((FakeCommandClient)workspace.Context.Commands).CurrentBaselineCompletesWith(
                new CurrentBaselineResponse(null, null, false));
            try
            {
                await workspace.SetProjectAsync(@"C:\projects\one.fwdata", captureFreshProject: false);
                window.Show();
                workspace.Context.HandOff(["dogs", "cats"]);
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                var handoff = workspace.PageModel<SettingsPageModel>().Handoff.Handoff;
                Assert.Equal(["dogs", "cats"], handoff.ChosenWords);
                Assert.Contains(window.GetLogicalDescendants().OfType<HandoffPanel>(), panel =>
                    ReferenceEquals(panel.Handoff, handoff));
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(30));
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
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var expander = window.GetLogicalDescendants().OfType<Expander>()
                    .Single(control => Equals(control.Header, MotifAppStrings.AddWords));
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
    public void AnalyzeTextsSourcesPanelMeasuresItsResolvedComponentWidth()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window, _) = NewComposedWindow();
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Show();
                window.UpdateLayout();

                var page = window.PageView<TextsPage>();
                var selectionHost = Assert.IsType<SourcesPanel>(page.SidePanel);
                var variant = window.ActualThemeVariant;
                Assert.True(Application.Current!.TryGetResource(
                    "Component.Shell.SidePanelWidth", variant, out var resolvedWidth));

                var frame = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                Assert.Equal(Assert.IsType<double>(resolvedWidth), frame.FindControl<Border>("SidePanelBorder")!.Bounds.Width);
                Assert.InRange(selectionHost.Bounds.Width, 1, Assert.IsType<double>(resolvedWidth));
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
                workspace.Context.OpenPage(WorkspacePage.Texts);
                workspace.Assess.Refusal = new Refusal(
                    "assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built.",
                    new Dictionary<string, string>
                    {
                        ["parserPath"] = OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss",
                    });
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var textsPage = window.PageView<TextsPage>();
                Assert.True(textsPage.IsEffectivelyVisible);
                Assert.Single(textsPage.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    block => block.Text == workspace.Assess.ShownRefusal!.Sentence && block.IsEffectivelyVisible);
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
                    new HandoffFileViewModel("parse-results.json", @"C:\handoff\parse-results.json"),
                };
                foreach (var file in files) workspace.PageModel<SettingsPageModel>().Handoff.Handoff.Files.Add(file);
                workspace.PageModel<SettingsPageModel>().Handoff.Handoff.State = RunState.Completed;
                SettingsTestAccess.Model(window).ShowHandoffByCopyingFiles = true;
                workspace.CurrentPage = WorkspacePage.Settings;

                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var panel = Assert.Single(window.GetLogicalDescendants().OfType<HandoffPanel>(),
                    item => item.IsEffectivelyVisible);
                var tiles = panel.GetLogicalDescendants().OfType<Border>()
                    .Where(tile => AutomationProperties.GetName(tile)?.StartsWith("Drag ", StringComparison.Ordinal)
                        == true)
                    .ToList();
                Assert.Equal(files.Length, tiles.Count);
                foreach (var file in files)
                    Assert.Contains(tiles, tile => AutomationProperties.GetName(tile) == file.DragAccessibleName);

                Assert.Contains(panel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.DragAllHandoffFiles && button.Focusable);
                var handoffNames = panel.GetLogicalDescendants()
                    .OfType<Control>()
                    .Select(control => AutomationProperties.GetName(control))
                    .OfType<string>()
                    .Where(name => name.Contains("Handoff", StringComparison.Ordinal));
                Assert.All(handoffNames, name => Assert.True(name.Contains("AI Handoff", StringComparison.Ordinal), name));
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Write the AI Handoff folder");

                var tile = tiles.Single(item =>
                    AutomationProperties.GetName(item) == files[2].DragAccessibleName);
                var tileText = tile.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Single(text => text.Text == files[2].RelativePath);
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
    public void CollectionNoticeAppearsInAnalyzeTexts()
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
                workspace.Context.OpenPage(WorkspacePage.Texts);
                window.UpdateLayout();
                var analyzePanel = Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>());
                Assert.Contains(analyzePanel.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
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
                workspace.PageModel<SettingsPageModel>().Handoff.Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
                workspace.PageModel<SettingsPageModel>().Handoff.Handoff.PastedHeader = "pasted header text";
                workspace.PageModel<SettingsPageModel>().Handoff.Handoff.State = RunState.Completed;
                workspace.CurrentPage = WorkspacePage.Settings;

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

    private static Rect BoundsInTopLevel(Control control)
    {
        var topLevel = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        var position = control.TranslatePoint(new Point(), topLevel);
        Assert.NotNull(position);
        return new Rect(position!.Value, control.Bounds.Size);
    }

    private static AssessCommandResponse CapturedAssessment(AssessCommandResponse response) => response with
    {
        InvocationId = "fixture/invocation",
        Measurements = [new ProducedAssessmentReference("fixture/assessment", "ParseTime", "fixture/invocation")],
        Words = response.Words.Select(word => word with
        {
            Origin = new WordMeasurementOrigin("fixture/assessment", "fixture/invocation", DateTimeOffset.UtcNow),
        }).ToArray(),
    };

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
        || visual.FindAncestorOfType<TextBox>(includeSelf: false) is not null
        || visual.FindAncestorOfType<ComboBox>(includeSelf: false) is not null
        || visual.FindAncestorOfType<TabItem>(includeSelf: false) is not null
        || visual.FindAncestorOfType<MenuItem>(includeSelf: false) is not null;

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
                foreach (var page in workspace.VisiblePages.Select(entry => entry.Page).Where(workspace.Context.CanOpenPage).Reverse())
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
        NewComposedWindow(TechDemoNoticeViewModel? techDemoNotice = null, IUriLauncher? launcher = null, TimeProvider? clock = null)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var dragSource = new FakeDragSource();
        var window = new MainWindow(false, launcher);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new FakeProjectPicker()),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection, clock),
            new FakeFolderPicker(), dragSource,
            fake, clipboard: new AvaloniaClipboard(window), techDemoNotice: techDemoNotice);

        window.Compose(workspace);
        workspace.Context.Diagnostics = true;
        return (workspace, window, dragSource);
    }

    private static OverviewResponse SampleOverview() => new(
        "Aweti", DateTimeOffset.Parse("2026-09-24T11:02:00Z"), DateTimeOffset.Parse("2026-09-24T10:58:00Z"),
        125, 3, 2, 313, 812, 38, 167, "assessment-1", DateTimeOffset.Parse("2026-09-24T11:04:00Z"), 130,
        "grammar-fingerprint", "selection-fingerprint",
        new OverviewTextCoverage(41, 23, 55, 6, 313, 183)
        {
            SameWords = 18,
            DifferentWords = 23,
            OccurrenceCoveragePercent = 58.46,
        },
        new OverviewAccuracy(18, 39, 5, 55, 2, 4, 9, 18)
        {
            ApprovedWordsNoMatch = 2,
            ApprovedWordsNoParse = 3,
            ApprovedWordsUnknown = 14,
            ApprovedWordsSkipped = 2,
        },
        new OverviewTiming(8.4, 123.5, [new SlowWordTiming("miboko", 1000) { ParserWork = 10_000 }], 55)
        {
            Work = new WordWorkSummary(120, 24_000, 84, 1_235),
        },
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
