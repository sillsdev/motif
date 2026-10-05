using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using WordRow = SIL.Motif.App.Views.WordRow;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Saves the window in the states a person only meets by moving the pointer, pressing a key or opening a menu:
/// hovers, tooltips, menus, the word card, expanded rows, focus rings, pressed and disabled buttons, the Help
/// pop-up, refusals, a Parse all words in progress and Try a Word's expanded steps. Each state is reached through
/// the pointer, the keyboard or the page's own commands, and the capture waits on what the state shows, such as a
/// tooltip being open, never on time. Like <see cref="PageScreenshots"/>, it runs only when <c>MOTIF_SCREENSHOTS</c>
/// names a folder, and it writes <c>state-&lt;page&gt;-&lt;state&gt;-&lt;width&gt;-&lt;theme&gt;.png</c> plus a
/// <c>states.txt</c> listing what each picture shows.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class StateScreenshots(ITestOutputHelper output)
{
    private static readonly (string Name, ThemeVariant Variant)[] Themes =
        [("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark)];

    private static readonly int[] Widths = [1040, 1240];

    [ScreenshotFact]
    public void CaptureOpaqueEnvironmentOperands()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.CurrentPage = WorkspacePage.TryAWord;
                window.Height = 1500;
                foreach (var (name, operand) in new[]
                {
                    ("object", "{\"left\":\"word_edge\",\"right\":\"#\"}"),
                    ("debug-string", "\"[raw environment]\""),
                })
                {
                    var trace = workspace.Assess.Trace;
                    trace.Result = TraceWordViewModel.FromDiagnosticJson(ExpertTraceReadingTests.EnvironmentOperandDiagnostic(operand)).Result;
                    trace.WordToTry = trace.Result!.Word;
                    trace.SelectedStep = trace.Root!;
                    trace.IsExpert = true;
                    foreach (var (theme, variant) in Themes)
                    foreach (var width in Widths)
                    {
                        Application.Current!.RequestedThemeVariant = variant;
                        window.Width = width;
                        PageScreenshots.Settle(window);
                        var panel = window.GetVisualDescendants().OfType<ExpertTracePanel>().Single();
                        var section = panel.GetVisualDescendants().OfType<Expander>().Single(expander =>
                            AutomationProperties.GetName(expander) == "Expert environment notation");
                        section.IsExpanded = true;
                        PageScreenshots.Settle(window);
                        var token = Assert.Single(panel.GetVisualDescendants().OfType<TraceNotationToken>(), control =>
                            control.IsEffectivelyVisible && control.DataContext is EnvironmentToken);
                        Assert.Equal("Authored environment notation unavailable", ToolTip.GetTip(token));
                        PageScreenshots.Save(window, Path.Combine(folder, $"state-try-a-word-environment-{name}-{width}-{theme}.png"));
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [ScreenshotFact]
    public void CaptureExpertSwitchAndEnvironmentNotation()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.CurrentPage = WorkspacePage.TryAWord;
                var trace = workspace.Assess.Trace;
                trace.Result = TraceWordViewModel.FromDiagnosticJson(ExpertTraceReadingTests.NotationDiagnostic).Result;
                trace.WordToTry = trace.Result!.Word;
                trace.SelectedCandidate = Assert.Single(trace.Candidates);
                trace.ExpertWholeTree = false;
                trace.SelectedStep = trace.SelectedCandidate.Steps[3];
                var step = trace.SelectedStep;
                var environment = step.RecordedStep.FailureEvidence!.Environment!;
                foreach (var (theme, variant) in Themes)
                foreach (var width in Widths)
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    window.Width = width;
                    window.Height = 1500;
                    workspace.Context.OpenInspector(step.InspectSubject!, trace: trace.InspectorTrace, captured: step.Captured);
                    await workspace.Inspector.Loading;
                    foreach (var expert in new[] { false, true })
                    {
                        trace.IsExpert = expert;
                        PageScreenshots.Settle(window);
                        Assert.True(workspace.Inspector.IsOpen);
                        Assert.Equal(step.RecordedStep.StepId, trace.SelectedStep!.RecordedStep.StepId);
                        PageScreenshots.Save(window, Path.Combine(folder,
                            $"state-try-a-word-switch-{(expert ? "expert" : "plain")}-{width}-{theme}.png"));
                    }
                    workspace.Inspector.Close();
                    window.Height = 2400;
                    PageScreenshots.Settle(window);
                    var panel = window.GetVisualDescendants().OfType<ExpertTracePanel>().Single();
                    foreach (var expander in panel.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
                    PageScreenshots.Settle(window);
                    Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), block =>
                        block.IsEffectivelyVisible && block.Text == environment);
                    PageScreenshots.Save(window, Path.Combine(folder, $"state-try-a-word-environment-{width}-{theme}.png"));
                    var token = panel.GetVisualDescendants().OfType<TraceNotationToken>().Single(control =>
                        control.IsEffectivelyVisible && control.DataContext is EnvironmentToken { Raw: var raw } && raw == environment);
                    token.Focus(NavigationMethod.Tab);
                    PageScreenshots.Settle(window);
                    Assert.True(ToolTip.GetIsOpen(token));
                    PageScreenshots.Save(window, Path.Combine(folder, $"state-try-a-word-token-focus-{width}-{theme}.png"));
                    ToolTip.SetIsOpen(token, false);
                    window.Focus();
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [ScreenshotFact]
    public void CaptureEveryInteractiveState()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var notes = new List<string>();

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            FakeCommandClient? client = null;
            AssessCommandResponse? parsed = null;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (fake, assessment) =>
            {
                OverviewTimingScreenshots.ReadOverviewAndTiming(fake, assessment);
                client = fake;
                parsed = assessment with
                {
                    Measurements = [new ProducedAssessmentReference("assessment/one", "ParseTime", "assessment/one")],
                };
            });
            var stage = new Stage(workspace, window, client!, parsed!);
            try
            {
                foreach (var state in States())
                {
                    if (state.Setup is not null) await state.Setup(stage);
                    stage.CurrentState = $"{state.Page}/{state.Name}";
                    foreach (var (theme, variant) in Themes)
                    {
                        Application.Current!.RequestedThemeVariant = variant;
                        foreach (var width in Widths)
                        {
                            window.Width = width;
                            window.Height = state.Height;
                            PageScreenshots.Settle(window);
                            var note = await state.Show(stage);
                            PageScreenshots.AssertSceneHasExpectedErrorState(window, workspace,
                                $"{state.Page}/{state.Name}", state.ExpectedRefusalCode);
                            var file = $"state-{state.Page}-{state.Name}-{width}-{theme}.png";
                            Save(window, Path.Combine(folder, file));
                            notes.Add($"{file}\t{note}");
                            stage.Reset();
                        }
                    }
                    if (state.Teardown is not null) await state.Teardown(stage);
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(5));

        File.WriteAllLines(Path.Combine(folder, "states.txt"), notes);
        foreach (var note in notes) output.WriteLine(note);
    }

    [Fact]
    public void MissingInspectorTargetNamesItsState()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Stage.FindTarget(
            "inspector/inspector-open", "the recorded ja- morpheme", () => Enumerable.Empty<Control>().First()));

        Assert.Equal("State 'inspector/inspector-open' could not find the recorded ja- morpheme.", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    /// <summary>One interactive state: its page, how one picture reaches it, and any run it needs.</summary>
    private sealed record State(string Page, string Name, Func<Stage, Task<string>> Show)
    {
        public Func<Stage, Task>? Setup { get; init; }

        public Func<Stage, Task>? Teardown { get; init; }

        public string? ExpectedRefusalCode { get; init; }

        public int Height { get; init; } = 780;
    }

    private const string MatrixWords = "Words in the chosen cells";
    private const string ListWords = "Words in the selected list";

    private static Task ResetTraceView(Stage stage)
    {
        var trace = stage.Workspace.Assess.Trace;
        trace.IsExpert = false;
        trace.ExpertWholeTree = true;
        return Task.CompletedTask;
    }

    private static async Task SetupTryWordInspector(Stage stage)
    {
        await stage.TryTheSampleWord();
        stage.Workspace.Assess.Trace.IsExpert = true;
        PageScreenshots.Settle(stage.Window);
        await stage.Until(() => stage.Visible<Border>(border => border.Classes.Contains("inspectable") &&
            border.Tag is ParserReadingMorphViewModel { Form: "ja-" }).Any(), "the recorded ja- morpheme");
    }

    private static IEnumerable<State> States()
    {
        // The shell: menus and buttons in the top bar and the sidebar, on whichever page is behind them.
        yield return new("shell", "project-menu", stage => stage.OpenMenu(WorkspacePage.Overview,
            () => stage.Window.FindControl<Button>("ProjectMenuButton")!, "the project menu"));
        yield return new("shell", "open-recent", async stage =>
        {
            await stage.OpenMenu(WorkspacePage.Overview, () => stage.Window.FindControl<Button>("ProjectMenuButton")!, "the project menu");
            return await stage.OpenMenu(null, () => stage.Window.FindControl<Button>("OpenRecentButton")!, "Open recent");
        })
        {
            // Opening the project menu reads the known projects again, so the client is what lists them.
            Setup = stage =>
            {
                stage.Client.KnownProjectsListIs([.. new[] { "Sample", "Kiswahili", "Mbugwe" }.Select(name =>
                    new KnownProjectSummary($@"C:\Users\linguist\FieldWorks\Projects\{name}\{name}.fwdata",
                        PageScreenshots.CaptureStartAt.AddDays(-2)))]);
                return Task.CompletedTask;
            },
        };
        yield return new("shell", "help", stage => stage.OpenMenu(WorkspacePage.Overview,
            () => stage.Window.FindControl<Button>("HelpButton")!, "Help"));
        yield return new("shell", "help-texts", stage => stage.OpenMenu(WorkspacePage.Texts,
            () => stage.Window.FindControl<Button>("HelpButton")!, "Help on Texts"));
        yield return new("shell", "refresh-tooltip", stage => stage.Hover(WorkspacePage.Overview,
            () => stage.Named<Button>("Refresh the project"), "Refresh"));
        yield return new("shell", "sidebar-hover", stage => stage.Hover(WorkspacePage.Overview,
            () => stage.SidebarItem("Timing"), "the Timing sidebar entry"));
        yield return new("shell", "sidebar-focus", stage => stage.FocusFromKeyboard(WorkspacePage.Overview,
            () => stage.SidebarItem("Timing"), "the Timing sidebar entry"));
        yield return new("shell", "button-focus", stage => stage.FocusFromKeyboard(WorkspacePage.Overview,
            () => stage.Window.FindControl<Button>("HelpButton")!, "Help"));
        yield return new("shell", "button-pressed", stage => stage.Press(WorkspacePage.Overview,
            () => stage.Named<Button>("Refresh the project"), "Refresh"));

        // Overview.
        yield return new("overview", "tile-hover", async stage =>
        {
            await stage.Hover(WorkspacePage.Overview, () => stage.Named<Border>("Speed"), "the Speed tile");
            return await stage.Hover(null, () => stage.Named<Button>("Open slowest words in Timing"), "the Timing link");
        });
        yield return new("overview", "tile-focus", async stage =>
        {
            await stage.Hover(WorkspacePage.Overview, () => stage.Named<Border>("Speed"), "the Speed tile");
            return await stage.FocusFromKeyboard(WorkspacePage.Overview,
                () => stage.Named<Button>("Open slowest words in Timing"), "the Timing link");
        });
        yield return new("overview", "lookfirst-link-hover", stage => stage.Hover(WorkspacePage.Overview,
            () => stage.Visible<Button>(link => link.Classes.Contains("overviewLookFirstRow") &&
                link.DataContext is OverviewLookFirstRow).First(),
            "the first Look first row"));
        yield return new("overview", "lookfirst-link-focus", stage => stage.FocusFromKeyboard(WorkspacePage.Overview,
            () => stage.Visible<Button>(link => link.Classes.Contains("overviewLookFirstRow") &&
                link.DataContext is OverviewLookFirstRow).First(),
            "the first Look first row"));
        yield return new("overview", "history-expanded", stage => stage.Expand(WorkspacePage.Overview,
            () => stage.Named<Expander>("Project history"), "Project history"))
        { Height = 1000 };

        // Texts, Matrix.
        yield return new("matrix", "chip-hover", stage => stage.Hover(WorkspacePage.Texts,
            () => stage.Visible<FilterChip>(chip => chip.Label != "All words").First(), "the first preset filter chip", TextsTab.Matrix));
        yield return new("matrix", "cell-hover", stage => stage.Hover(WorkspacePage.Texts,
            () => stage.LargestCell(), "the largest Matrix cell", TextsTab.Matrix));
        yield return new("matrix", "cell-focus", stage => stage.FocusFromKeyboard(WorkspacePage.Texts,
            () => stage.LargestCell(), "the largest Matrix cell", TextsTab.Matrix));
        yield return new("matrix", "cell-selected", async stage =>
        {
            // A second click on a chosen cell clears it, so each picture starts from no cell chosen.
            stage.Workspace.Assess.Compare.ClearSelectionCommand.Execute(null);
            stage.Open(WorkspacePage.Texts, TextsTab.Matrix);
            var cell = stage.LargestCell();
            HeadlessClick.Click(stage.Window, cell, "the largest Matrix cell");
            await stage.Until(() => stage.Workspace.Assess.Compare.AnySelected, "the cell's word list");
            return $"Clicked the largest Matrix cell; the list shows {stage.Workspace.Assess.Compare.Words.Count} words.";
        })
        {
            Height = 1100,
            Teardown = stage => { stage.Workspace.Assess.Compare.ClearSelectionCommand.Execute(null); return Task.CompletedTask; },
        };
        yield return new("matrix", "link-hover", async stage =>
        {
            await stage.Hover(WorkspacePage.Texts, () => stage.RowBody(MatrixWords), "the first word row", TextsTab.Matrix);
            return await stage.Hover(null,
                () => stage.Visible<HyperlinkButton>(link => link.Content as string == "Open in text").First(), "Open in text");
        })
        { Height = 1100 };

        yield return new("matrix", "row-hover", stage => stage.Hover(WorkspacePage.Texts,
            () => stage.RowBody(MatrixWords), "the first word row", TextsTab.Matrix))
        { Height = 1100 };
        yield return new("matrix", "row-focus", stage => stage.FocusFromKeyboard(WorkspacePage.Texts,
            () => stage.RowBody(MatrixWords), "the first word row", TextsTab.Matrix))
        { Height = 1100 };
        yield return new("matrix", "row-opened", stage => stage.OpenRow(WorkspacePage.Texts, TextsTab.Matrix, MatrixWords))
        {
            Height = 1100,
            Teardown = stage => stage.CloseRows(),
        };

        // Texts, Analyze texts.
        yield return new("analyze", "word-hover", stage => stage.Hover(WorkspacePage.Texts,
            () => stage.Strip("hawajafika"), "the hawajafika word strip", TextsTab.AnalyzeTexts));
        yield return new("analyze", "word-focus", stage => stage.FocusFromKeyboard(WorkspacePage.Texts,
            () => stage.Strip("Sungura"), "the Sungura word strip", TextsTab.AnalyzeTexts));
        yield return new("analyze", "disapproved-tooltip", async stage =>
        {
            stage.InText.SelectedText = stage.InText.Texts.Single(text => text.Lines.SelectMany(line => line.Tokens)
                .Any(token => token.Form == "walikula"));
            return await stage.Hover(WorkspacePage.Texts,
                () => stage.Strip("walikula").GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("markChip") && border.IsEffectivelyVisible),
                "the Built anyway mark on walikula", TextsTab.AnalyzeTexts);
        })
        { Teardown = stage => { stage.InText.SelectedText = stage.InText.Texts.First(); return Task.CompletedTask; } };
        yield return new("analyze", "fix-menu", stage => stage.OpenMenu(WorkspacePage.Texts,
            () => stage.Strip("chakula").GetVisualDescendants().OfType<Button>()
                .First(button => AutomationProperties.GetName(button) == "Fix actions from the word strip"),
            "Fix on the chakula strip", TextsTab.AnalyzeTexts));
        yield return new("analyze", "select-menu", stage => stage.OpenMenu(WorkspacePage.Texts,
            () => stage.Named<Button>("Select words for actions"), "Select words", TextsTab.AnalyzeTexts));
        yield return new("analyze", "mark-read-menu", stage => stage.OpenMenu(WorkspacePage.Texts,
            () => stage.Named<Button>("Mark read or unread"), "Mark read", TextsTab.AnalyzeTexts));
        yield return new("analyze", "word-card", stage => stage.OpenCard("hawajafika"))
        { Teardown = stage => { stage.InText.CloseTokenCard(); return Task.CompletedTask; } };
        yield return new("analyze", "word-card-hover", async stage =>
        {
            await stage.OpenCard("alikula");
            var section = stage.Visible<Border>(border => border.Classes.Contains("wordCardSection") &&
                border.Classes.Contains("hoverReveal")).Last();
            return await stage.Hover(null, () => section, "the card's PanGloss section");
        })
        { Teardown = stage => { stage.InText.CloseTokenCard(); return Task.CompletedTask; } };
        yield return new("analyze", "word-card-fix-menu", async stage =>
        {
            await stage.OpenCard("alikula");
            return await stage.OpenMenu(null, () => stage.Named<Button>("Fix actions for this word"), "Fix on the word card");
        })
        { Teardown = stage => { stage.InText.CloseTokenCard(); return Task.CompletedTask; } };
        yield return new("analyze", "staged-hover", stage => stage.Hover(WorkspacePage.Texts,
            () => stage.Visible<Border>(border => border.Classes.Contains("stagedStrip")).First(), "the staged strip on chakula",
            TextsTab.AnalyzeTexts))
        {
            Setup = stage => stage.StageChakula(),
            Teardown = stage => stage.UnstageChakula(),
        };

        // Texts, Lists.
        yield return new("lists", "chip-hover", async stage =>
        {
            await stage.ChooseList();
            return await stage.Hover(null, () => stage.Visible<Button>(button => button.Classes.Contains("filterChip")).Last(),
                "the last list chip");
        });
        yield return new("lists", "row-expanded", async stage =>
        {
            await stage.ChooseList();
            return await stage.OpenRow(null, null, ListWords);
        })
        { Teardown = stage => stage.CloseRows() };
        yield return new("lists", "row-hover", async stage =>
        {
            await stage.ChooseList();
            return await stage.Hover(null, () => stage.RowBody("Words in the selected list"), "the first Lists word row");
        })
        { Height = 1100 };
        yield return new("lists", "disabled-hover", async stage =>
        {
            await stage.ChooseList();
            var button = stage.Visible<Button>(candidate =>
                AutomationProperties.GetAutomationId(candidate) == SIL.Motif.App.AutomationIds.HandOffCheckedWords).Single();
            return $"{(button.IsEffectivelyEnabled ? "Enabled" : "Disabled")} button. " +
                await stage.Hover(null, () => button, "Tick words first");
        });

        // Try a Word.
        yield return new("try-a-word", "tools-menu", stage => stage.OpenMenu(WorkspacePage.TryAWord,
            () => stage.Named<Button>("Try a Word tools"), "Try a Word tools"))
        { Setup = stage => stage.TryTheSampleWord() };
        yield return new("try-a-word", "recorded-step-focus", stage => stage.FocusFromKeyboard(WorkspacePage.TryAWord,
            () => stage.Named<ListBox>("Plain trace steps"), "the recorded trace"))
        { Setup = stage => stage.TryTheSampleWord() };
        yield return new("try-a-word", "steps-expanded", async stage =>
        {
            stage.Open(WorkspacePage.TryAWord);
            var expanders = stage.Visible<Expander>(expander => expander.FindAncestorOfType<DiagnosticPanel>() is not null).ToList();
            foreach (var expander in expanders) expander.IsExpanded = true;
            PageScreenshots.Settle(stage.Window);
            var more = stage.Visible<Expander>(expander => expander.FindAncestorOfType<DiagnosticPanel>() is not null &&
                !expander.IsExpanded).ToList();
            foreach (var expander in more) expander.IsExpanded = true;
            return $"Expanded {expanders.Count + more.Count} sections: " +
                string.Join(", ", expanders.Concat(more).Select(expander => expander.Header).Distinct());
        })
        { Height = 2600, Setup = stage => stage.TryTheSampleWord() };
        yield return new("try-a-word", "why-section", stage =>
        {
            stage.Open(WorkspacePage.TryAWord);
            var section = stage.Named<Border>("Recorded trace context");
            section.BringIntoView();
            PageScreenshots.Settle(stage.Window);
            Assert.True(section.IsEffectivelyVisible);
            var group = Assert.Single(stage.Workspace.PageModel<TryWordPageModel>().Trace.StopGroups);
            Assert.Equal("Recorded refusal", group.RuleText);
            return Task.FromResult("The Plain trace keeps its recorded context visible; the stopped attempt's rule is not recorded.");
        })
        { Setup = stage => stage.TryTheSampleWord() };
        yield return new("try-a-word", "expert-attempt", stage =>
        {
            stage.Open(WorkspacePage.TryAWord);
            var trace = stage.Workspace.Assess.Trace;
            trace.SelectedCandidate = Assert.Single(trace.ExpertAttempts);
            trace.ExpertWholeTree = false;
            Assert.NotEmpty(trace.ExpertEvents);
            trace.SelectedStep = trace.ExpertEvents.Last();
            trace.IsExpert = true;
            PageScreenshots.Settle(stage.Window);
            var panel = Assert.Single(stage.Visible<ExpertTracePanel>(_ => true));
            var events = Assert.Single(panel.GetVisualDescendants().OfType<ItemsControl>(), control =>
                AutomationProperties.GetName(control) == "Expert trace events");
            Assert.Equal(trace.ExpertEvents.Count, events.Items.Count);
            return Task.FromResult($"Expert view of attempt {trace.SelectedCandidate.AttemptId}: " +
                $"{trace.ExpertEvents.Count} recorded events.");
        })
        {
            Setup = stage => stage.TryTheSampleWord(),
            Teardown = stage => ResetTraceView(stage),
            Height = 1500,
        };
        yield return new("try-a-word", "expert-whole-tree", stage =>
        {
            stage.Open(WorkspacePage.TryAWord);
            var trace = stage.Workspace.Assess.Trace;
            trace.ExpertWholeTree = false;
            trace.ExpertWholeTree = true;
            trace.IsExpert = true;
            PageScreenshots.Settle(stage.Window);
            var panel = Assert.Single(stage.Visible<ExpertTracePanel>(_ => true));
            var events = Assert.Single(panel.GetVisualDescendants().OfType<ItemsControl>(), control =>
                AutomationProperties.GetName(control) == "Expert trace events");
            Assert.NotEmpty(trace.ExpertEvents);
            Assert.Equal(trace.ExpertEvents.Count, events.Items.Count);
            var visibleLabels = events.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && text.Bounds.Height > 0)
                .Select(text => text.Text).ToArray();
            Assert.All(trace.ExpertEvents, step => Assert.Contains(step.Type, visibleLabels));
            return Task.FromResult($"Expert view of the whole recorded tree: {trace.ExpertEvents.Count} events.");
        })
        {
            Setup = stage => stage.TryTheSampleWord(),
            Teardown = stage => ResetTraceView(stage),
            Height = 1500,
        };

        // The inspector, beside each page that opens it, and one step down its breadcrumb.
        yield return new("inspector", "from-analyze", async stage =>
        {
            await stage.OpenCard("hawajafika");
            return await stage.Inspect(() => stage.Visible<Border>(border => border.Classes.Contains("inspectable") &&
                border.Tag is ParserReadingMorphViewModel { Form: "fik" }).First(), "fik on the word card");
        })
        { Teardown = stage => { stage.InText.CloseTokenCard(); return Task.CompletedTask; } };
        yield return new("inspector", "breadcrumb-step", async stage =>
        {
            await stage.OpenCard("hawajafika");
            await stage.Inspect(() => stage.Visible<Border>(border => border.Classes.Contains("inspectable") &&
                border.Tag is ParserReadingMorphViewModel { Form: "wa-" }).First(), "wa- on the word card");
            return await stage.Inspect(() => stage.Named<InspectLink>("Inspect w-"), "its allomorph w-");
        })
        { Teardown = stage => { stage.InText.CloseTokenCard(); return Task.CompletedTask; } };
        yield return new("inspector", "inspector-open", async stage =>
        {
            stage.Open(WorkspacePage.TryAWord);
            return await stage.Inspect(() => stage.Visible<Border>(border => border.Classes.Contains("inspectable") &&
                border.Tag is ParserReadingMorphViewModel { Form: "ja-" }).First(), "the recorded ja- morpheme");
        })
        { Setup = SetupTryWordInspector, Teardown = stage => ResetTraceView(stage) };
        yield return new("inspector", "from-timing", stage => stage.Inspect(() =>
        {
            stage.Open(WorkspacePage.Timing);
            return stage.Visible<InspectLink>(link =>
                link.FindAncestorOfType<ItemsControl>() is { } list && AutomationProperties.GetName(list) == "Timing by rule").First();
        }, "the costliest rule"));

        // Timing.
        yield return new("timing", "rule-hover", stage => stage.Hover(WorkspacePage.Timing,
            () => stage.Visible<Button>(button => button.Classes.Contains("timingRuleRow")).First(), "the first rule row"));
        yield return new("timing", "filled", async stage =>
        {
            stage.Workspace.PageModel<TimingPageModel>().ShowKindCalls = false;
            await stage.ShowTimingWords();
            return "Timing with measured word time, kind shares, a selected rule, and its costliest words.";
        })
        { Height = 1600 };
        yield return new("timing", "calls-open", async stage =>
        {
            stage.Open(WorkspacePage.Timing);
            var timing = stage.Workspace.PageModel<TimingPageModel>();
            await stage.Until(() => timing.HasKindCalls, "Timing's per-kind calls");
            if (!timing.ShowKindCalls)
            {
                var calls = stage.Named<Button>("Show calls per kind");
                calls.Command!.Execute(calls.CommandParameter);
            }
            PageScreenshots.Settle(stage.Window);
            return "Timing with calls per kind disclosed.";
        });
        yield return new("timing", "selected-rule-inspector", async stage =>
        {
            stage.Workspace.PageModel<TimingPageModel>().ShowKindCalls = false;
            await stage.ShowTimingWords();
            var timing = stage.Workspace.PageModel<TimingPageModel>();
            await timing.ChooseRuleCommand.ExecuteAsync(timing.RuleRows[0].Row);
            await stage.Until(() => timing.CostliestRuleWordRows.Count > 0, "the selected rule's costliest words");
            PageScreenshots.Settle(stage.Window);
            return await stage.Inspect(() => stage.Visible<InspectLink>(link =>
                link.FindAncestorOfType<ItemsControl>() is { } list &&
                AutomationProperties.GetName(list) == "Timing by rule").First(), "the selected rule");
        })
        { Height = 1600 };
        yield return new("timing", "statistics-expanded", stage => stage.Expand(WorkspacePage.Timing,
            () => stage.Visible<Expander>(expander => expander.Header as string == "Detailed statistics").First(),
            "Detailed statistics"))
        { Height = 1300 };

        // Warnings.
        yield return new("warnings", "part-link-hover", stage => stage.HoverWarningPartLink());
        yield return new("warnings", "row-opened", stage => stage.OpenWarningRow("hc-stem-no-grammatical-category", named: true))
        { Height = 1100, Teardown = stage => stage.CloseWarningRow() };
        yield return new("warnings", "kat-row-opened",
            stage => stage.OpenWarningRow("conversion.unsegmentable-form", named: true))
        { Height = 1100, Teardown = stage => stage.CloseWarningRow() };
        yield return new("warnings", "unnamed-row-opened",
            stage => stage.OpenWarningRow("grammar.msa.no-rule-form-allomorphs", named: false))
        { Height = 1100, Teardown = stage => stage.CloseWarningRow() };

        // Review changes with one change staged and not yet checked, so Apply is disabled.
        yield return new("review", "disabled-apply-hover", async stage =>
        {
            stage.Open(WorkspacePage.Review);
            var apply = stage.Named<Button>("Apply to FieldWorks project");
            return $"{(apply.IsEffectivelyEnabled ? "Enabled" : "Disabled")} Apply. " +
                await stage.Hover(null, () => apply, "Apply to FieldWorks project");
        })
        { Setup = stage => stage.StageChakula(), Teardown = stage => stage.UnstageChakula() };

        // The word row on the pages beyond the Matrix and Lists: each list at rest, and one row opened.
        yield return new("timing", "word-rows", async stage =>
        {
            await stage.ShowTimingWords();
            Assert.Empty(stage.WordRows("timing-words"));
            return "Timing with a rule chosen and its costliest words.";
        })
        { Height = 1700 };
        yield return new("timing", "costliest-word-row-hover", async stage =>
        {
            await stage.ShowTimingWords();
            var row = stage.WordRows("timing-rule-words").First();
            return await stage.Hover(null, () => row.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("wordRowBody")), "a costliest word row");
        })
        { Height = 1700 };
        yield return new("overview", "slowest-summary", async stage =>
        {
            stage.Open(WorkspacePage.Overview);
            const string summaryName = "Slowest words and recorded times";
            await stage.Until(() => stage.Visible<TextBlock>(text =>
                AutomationProperties.GetName(text) == summaryName &&
                text.Text?.Contains("mwalimu", StringComparison.Ordinal) == true &&
                text.Text.Contains("hawajafika", StringComparison.Ordinal)).Any(),
                "the Speed tile's slowest-word summary");
            var text = stage.Named<TextBlock>(summaryName).Text ?? string.Empty;
            Assert.Contains("700 ms", text, StringComparison.Ordinal);
            Assert.Contains("48 ms", text, StringComparison.Ordinal);
            return $"The Speed tile keeps its slowest words compact: {text}.";
        })
        { Height = 1000 };
        yield return new("review", "word-rows", async stage =>
        {
            stage.Open(WorkspacePage.Review);
            await stage.Until(() => stage.WordRows("review").Any(), "the staged change's row");
            return "Review changes with chakula staged, as a word row with its staged arrow and Undo.";
        })
        { Setup = stage => stage.StageChakula(), Teardown = stage => stage.UnstageChakula() };
        yield return new("review", "word-row-hover", async stage =>
        {
            stage.Open(WorkspacePage.Review);
            await stage.Until(() => stage.WordRows("review").Any(), "the staged change's row");
            var row = stage.WordRows("review").First();
            return await stage.Hover(null, () => row.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("wordRowBody")), "the staged change's row");
        })
        { Height = 1000, Setup = stage => stage.StageChakula(), Teardown = stage => stage.UnstageChakula() };
        yield return new("review", "word-row-opened", async stage =>
        {
            stage.Open(WorkspacePage.Review);
            return await stage.OpenWordRow("review");
        })
        { Height = 1000, Setup = stage => stage.StageChakula(), Teardown = async stage =>
            {
                await stage.CloseWordRows();
                await stage.UnstageChakula();
            } };
        yield return new("analyze", "word-list", async stage =>
        {
            stage.ShowWordList();
            await stage.Until(() => stage.WordRows("word-list").Any(), "the Word list's rows");
            return "Analyze texts' Word list as word rows.";
        });
        yield return new("analyze", "word-list-row-opened", async stage =>
        {
            stage.ShowWordList();
            return await stage.OpenWordRow("word-list");
        })
        { Height = 1000, Teardown = stage => stage.CloseWordRows() };
        yield return new("what-changed", "word-rows", async stage =>
        {
            stage.Open(WorkspacePage.Texts, TextsTab.WhatChanged);
            await stage.Until(() => stage.WordRows("what-changed").Any(), "the chosen move's words");
            return "What changed after a second run in which kitabu lost its analysis, with that move chosen.";
        })
        { Height = 1100, Setup = stage => stage.ParseWithKitabuLost(), Teardown = stage => stage.ParseAgain() };
        yield return new("what-changed", "word-row-hover", async stage =>
        {
            stage.Open(WorkspacePage.Texts, TextsTab.WhatChanged);
            await stage.Until(() => stage.WordRows("what-changed").Any(), "the chosen move's words");
            var row = stage.WordRows("what-changed").First();
            return await stage.Hover(null, () => row.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("wordRowBody")), "a What changed word row");
        })
        { Height = 1100, Setup = stage => stage.ParseWithKitabuLost(), Teardown = stage => stage.ParseAgain() };

        // AI Handoff.
        yield return new("ai-handoff", "drag-tooltip", stage => stage.Hover(WorkspacePage.AiHandoff,
            () => stage.Visible<Button>(button =>
                AutomationProperties.GetAutomationId(button) == SIL.Motif.App.AutomationIds.DragAllHandoffFiles).Single(),
            "the drag-all button"));
        yield return new("ai-handoff", "question-hover", stage => stage.Hover(WorkspacePage.AiHandoff,
            () => stage.Visible<Button>(button => button.Classes.Contains("handoffQuestion")).First(), "the first question"));
        yield return new("ai-handoff", "handoff-md-expanded", stage => stage.Expand(WorkspacePage.AiHandoff,
            () => stage.Visible<Expander>(expander => expander.Header as string == "handoff.md").First(), "handoff.md"))
        { Height = 1300 };

        // Runs: a Parse all words in progress, then the refusals a person meets.
        yield return new("shell", "parse-progress", stage =>
        {
            stage.Open(WorkspacePage.Texts, TextsTab.Matrix);
            return Task.FromResult(stage.Workspace.ParseAllWordsProgressText);
        })
        {
            Setup = async stage =>
            {
                stage.Client.AssessBlocksUntilCancelled(
                    new Refusal(RefusalCodes.AssessmentCancelled, FailureReason.Cancelled, "The Assessment run was cancelled."),
                    new AssessmentProgress(AssessmentStage.Parsing, 4, 9, "Parsing hawajafika"));
                stage.Running = stage.Workspace.Assess.RunCommand.ExecuteAsync(null);
                await stage.Until(() => stage.Workspace.ShowsParseAllWordsProgress &&
                    stage.Workspace.Assess.Progress is not null, "Parse all words progress");
            },
            Teardown = async stage =>
            {
                stage.Workspace.Assess.CancelCommand.Execute(null);
                await stage.Running!;
                await stage.ParseAgain();
            },
        };
        yield return new("texts", "parse-refusal", stage => stage.ExpandRefusal(WorkspacePage.Texts, TextsTab.Matrix))
        {
            ExpectedRefusalCode = RefusalCodes.AssessParserUnavailable,
            Setup = async stage =>
            {
                stage.Client.AssessRefusesWith(new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused,
                    "PanGloss exited with code 3: the grammar file could not be read (pangloss-win-x64.exe, v0.6.2)."));
                await stage.Workspace.Assess.RunCommand.ExecuteAsync(null);
            },
            Teardown = stage => stage.ParseAgain(),
        };
        yield return new("try-a-word", "refusal", stage => stage.ExpandRefusal(WorkspacePage.TryAWord, null))
        {
            ExpectedRefusalCode = RefusalCodes.WordTraceParserUnavailable,
            Setup = async stage =>
            {
                stage.Client.OnTraceWord((_, _) => Task.FromResult(CommandOutcome<WordTraceResponse>.Refused(
                    new Refusal(RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused,
                        "PanGloss exited with code 3: the grammar file could not be read (pangloss-win-x64.exe, v0.6.2)."))));
                stage.Workspace.Context.TryWord("kitabu");
                await stage.Workspace.Assess.Trace.TryCommand.ExecutionTask!;
            },
        };
        yield return new("timing", "empty-step-limit", async stage =>
        {
            stage.Client.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
                new TimingResponse(request.AssessmentId ?? "assessment/one", request.WordSet, request.By,
                    0, 0, 0, [], [], [])
                {
                    Words = [],
                    Attribution = new WordTimeAttribution(0, 0, 0, 0, null, 0, false),
                })));
            stage.Open(WorkspacePage.Timing);
            var timing = stage.Workspace.PageModel<TimingPageModel>();
            await timing.SelectWordSetCommand.ExecuteAsync("step-limit");
            await stage.Until(() => timing.ShowEmptySelection, "Timing's empty selection");
            return "Timing when no words stopped at a search limit.";
        });
    }

    // Two matching frames in a row, so no chevron is caught turning; a blinking caret ends the bounded run instead.
    private static void Save(MainWindow window, string path)
    {
        LayoutAssertions.AssertCurrent(window);
        byte[]? previous = null;
        byte[] current = [];
        for (var pass = 0; pass < 40; pass++)
        {
            PageScreenshots.Settle(window);
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
            using var encoded = new MemoryStream();
            frame.Save(encoded, PngBitmapEncoderOptions.Default);
            current = encoded.ToArray();
            if (previous is not null && current.AsSpan().SequenceEqual(previous)) break;
            previous = current;
        }
        File.WriteAllBytes(path, current);
    }

    /// <summary>The open window, the ways a state reaches into it, and the reset after each picture.</summary>
    private sealed class Stage(WorkspaceShellViewModel workspace, MainWindow window, FakeCommandClient client,
        AssessCommandResponse parsed)
    {
        private readonly List<FlyoutBase> _menus = [];
        private readonly List<Control> _tips = [];
        private Control? _pressed;

        public WorkspaceShellViewModel Workspace { get; } = workspace;

        public MainWindow Window { get; } = window;

        public FakeCommandClient Client { get; } = client;

        public string CurrentState { get; set; } = "unknown state";

        public Task? Running { get; set; }

        public ResultsInTextViewModel InText => Workspace.PageModel<TextsPageModel>().ResultsInText;

        public TextsListsViewModel Lists => Workspace.PageModel<TextsPageModel>().TextsLists;

        public void Open(WorkspacePage page, TextsTab? tab = null)
        {
            if (tab is { } chosen) Workspace.PageModel<TextsPageModel>().Tab = chosen;
            Workspace.CurrentPage = page;
            PageScreenshots.Settle(Window);
        }

        public IEnumerable<T> Visible<T>(Func<T, bool> match) where T : Control =>
            Window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible && match(control));

        public T Named<T>(string name) where T : Control =>
            Visible<T>(control => AutomationProperties.GetName(control) == name).FirstOrDefault()
            ?? throw new InvalidOperationException($"No visible {typeof(T).Name} is named '{name}'.");

        public ListBoxItem SidebarItem(string title) =>
            Visible<ListBoxItem>(item => item.DataContext is PageViewModel page && page.Title == title).Single();

        public Border LargestCell()
        {
            var largest = Workspace.Assess.Compare.Cells.MaxBy(cell => cell.Count)!;
            return Visible<Border>(border => border.Classes.Contains("matrixCell") && border.DataContext == largest).Single();
        }

        public ResultsTokenViewModel Token(string form) =>
            InText.VisibleLines.SelectMany(line => line.Tokens).First(token => token.Form == form);

        public Border Strip(string form)
        {
            var token = Token(form);
            var line = InText.VisibleLines.First(line => line.Tokens.Contains(token));
            var panel = Visible<ResultsInTextPanel>(_ => true).Single();
            panel.FindControl<ItemsControl>("TextLineItems")!.ScrollIntoView(InText.VisibleLines.IndexOf(line));
            PageScreenshots.Settle(Window);
            return Visible<Border>(border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, token)).First();
        }

        public async Task<string> Hover(WorkspacePage? page, Func<Control> find, string what, TextsTab? tab = null)
        {
            if (page is { } shown) Open(shown, tab);
            var control = find();
            var owner = control.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => ToolTip.GetTip(c) is not null);
            if (owner is not null)
            {
                ToolTip.SetShowDelay(owner, 0);
                _tips.Add(owner);
            }
            Window.MouseMove(CentreOf(control));
            PageScreenshots.Settle(Window);
            if (owner is null) return $"Pointer over {what}; it has no tooltip.";
            await Task.Yield();
            PageScreenshots.Settle(Window);
            return ToolTip.GetIsOpen(owner)
                ? $"Pointer over {what}; its tooltip is open: \"{ToolTip.GetTip(owner)}\"."
                : $"Pointer over {what}; its tooltip \"{ToolTip.GetTip(owner)}\" did not open.";
        }

        public async Task<string> OpenMenu(WorkspacePage? page, Func<Button> find, string what, TextsTab? tab = null)
        {
            if (page is { } shown) Open(shown, tab);
            var button = find();
            var menu = button.Flyout ?? throw new InvalidOperationException($"{what} has no menu.");
            button.Focus(NavigationMethod.Tab);
            PageScreenshots.Settle(Window);
            HeadlessClick.Click(Window, button, what);
            await Until(() => menu.IsOpen, $"the {what} menu");
            _menus.Insert(0, menu);
            return $"Clicked {what}; its menu is open.";
        }

        public Task<string> FocusFromKeyboard(WorkspacePage page, Func<Control> find, string what, TextsTab? tab = null)
        {
            Open(page, tab);
            var control = find();
            var target = control.Focusable ? control
                : control.GetVisualDescendants().OfType<Control>().First(candidate => candidate.Focusable);
            target.Focus(NavigationMethod.Tab);
            PageScreenshots.Settle(Window);
            return Task.FromResult(target.IsKeyboardFocusWithin
                ? $"Keyboard focus on {what}."
                : $"Keyboard focus did not reach {what}.");
        }

        public Task<string> Press(WorkspacePage page, Func<Control> find, string what)
        {
            Open(page);
            var control = find();
            var point = CentreOf(control);
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
            _pressed = control;
            PageScreenshots.Settle(Window);
            return Task.FromResult($"Pointer held down on {what}.");
        }

        public async Task<string> Expand(WorkspacePage page, Func<Expander> find, string what)
        {
            Open(page);
            var expander = find();
            expander.IsExpanded = true;
            await Until(() => expander.IsExpanded, what);
            return $"Expanded {what}.";
        }

        /// <summary>The first row of the named word list: the part a pointer or Tab reaches.</summary>
        public Border RowBody(string list) => Visible<ListBox>(box => AutomationProperties.GetName(box) == list).Single()
            .GetVisualDescendants().OfType<WordRow>().First()
            .GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("wordRowBody"));

        /// <summary>Opens the first row of the named list from the keyboard, as Enter on a focused row does.</summary>
        public async Task<string> OpenRow(WorkspacePage? page, TextsTab? tab, string list)
        {
            await CloseRows();
            if (page is { } shown) Open(shown, tab);
            PageScreenshots.Settle(Window);
            var body = RowBody(list);
            var row = body.FindAncestorOfType<WordRow>()!;
            body.BringIntoView();
            body.Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Until(() => row.IsOpen, "the opened word row");
            return $"Pressed Enter on '{row.Row?.Word}' in {list}; its card is open inside the row.";
        }

        public Task CloseRows()
        {
            foreach (var word in Workspace.Assess.Compare.Words) word.IsExpanded = false;
            return Task.CompletedTask;
        }

        public async Task<string> OpenCard(string form)
        {
            Open(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
            InText.CloseTokenCard();
            await InText.OpenTokenCardAsync(Token(form));
            PageScreenshots.Settle(Window);
            return $"Opened the word card for {form}.";
        }

        public async Task<string> OpenWarningRow(string code, bool named)
        {
            Open(WorkspacePage.Warnings);
            var row = Workspace.PageModel<WarningsPageModel>().Grammar.Warnings.Rows
                .Cast<GrammarWarningRowViewModel>()
                .First(candidate => candidate.GroupCode == code && (candidate.Warning.Subject.Count > 0) == named);
            row.IsOpen = true;
            var container = Visible<Border>(border => border.Classes.Contains("warningRow") && border.DataContext == row).Single();
            container.BringIntoView();
            await Until(() => (row.HasReachStateText
                ? Visible<TextBlock>(block => block.Classes.Contains("warningState")
                    && block.Text == row.ReachStateText).Any()
                : Visible<TextBlock>(block => block.Text == row.NoExactUsesText).Any())
                && (!row.HasGuidance || Visible<TextBlock>(block => block.Text == "What to do in FieldWorks").Any()),
                "the finding details and producer guidance when present");
            return $"Opened the {row.PanGlossTitle} finding{(named ? " with a named item" : ", which names no item")}.";
        }

        public Task<string> HoverWarningPartLink()
        {
            Open(WorkspacePage.Warnings);
            var row = Workspace.PageModel<WarningsPageModel>().Grammar.Warnings.Rows
                .Cast<GrammarWarningRowViewModel>().First(candidate => candidate.Warning.Subject.Any(part => part.FieldWorksLink is not null));
            row.IsOpen = true;
            PageScreenshots.Settle(Window);
            var link = Visible<HyperlinkButton>(button => button.Classes.Contains("warningObjectLink")).First();
            return Hover(null, () => link, "a FieldWorks link in a finding");
        }

        public Task CloseWarningRow()
        {
            foreach (var row in Workspace.PageModel<WarningsPageModel>().Grammar.Warnings.Rows
                .Cast<GrammarWarningRowViewModel>())
                row.IsOpen = false;
            PageScreenshots.Settle(Window);
            return Task.CompletedTask;
        }

        public async Task ChooseList()
        {
            Open(WorkspacePage.Texts, TextsTab.Lists);
            var list = Lists.Lists.FirstOrDefault(candidate => candidate.HasWords)
                ?? throw new InvalidOperationException("No word list has words.");
            if (Lists.SelectedList != list) Lists.SelectListCommand.Execute(list);
            await Until(() => Lists.SelectedList == list && Lists.Compare.Words.Count > 0, "the chosen list's words");
        }

        public async Task<string> ExpandRefusal(WorkspacePage page, TextsTab? tab)
        {
            Open(page, tab);
            var details = Visible<Expander>(expander => expander.Classes.Contains("refusalDetails")).FirstOrDefault();
            if (details is null) return "The refusal shows no Details.";
            details.IsExpanded = true;
            await Until(() => details.IsExpanded, "the refusal's Details");
            return "Refusal with its Details expanded.";
        }

        public async Task StageChakula()
        {
            Open(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
            var chakula = Token("chakula");
            var add = chakula.Marking.FixChoices.Single(choice => choice.Label == "Add as Approved");
            await chakula.StageMarkingChoiceForTokenCommand!.ExecuteAsync(add);
            await Until(() => Token("chakula").HasStagedChanges, "the staged change on chakula");
        }

        public async Task UnstageChakula()
        {
            Open(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
            foreach (var staged in Token("chakula").StagedChanges.ToList())
                await InText.Changes.RemoveCommand.ExecuteAsync(staged.Change);
            await Until(() => !Token("chakula").HasStagedChanges, "chakula with nothing staged");
        }

        // Choosing a word on Texts primes Try a Word afresh, so its trace is run again before it is shown.
        /// <summary>Clicks the name <paramref name="target"/> finds, and waits for the inspector to read it.</summary>
        public async Task<string> Inspect(Func<Control> target, string what)
        {
            var name = FindTarget(CurrentState, what, target);
            var centre = CentreOf(name);
            Window.MouseMove(centre);
            PageScreenshots.Settle(Window);
            Window.MouseDown(centre, MouseButton.Left);
            Window.MouseUp(centre, MouseButton.Left);
            Window.MouseMove(new Point(4, Window.Bounds.Height - 4));
            PageScreenshots.Settle(Window);
            await Workspace.Inspector.Loading;
            PageScreenshots.Settle(Window);
            if (!Workspace.Inspector.IsOpen) throw new InvalidOperationException($"Clicking {what} opened no inspector.");
            return $"Clicked {what}; the inspector shows {Workspace.Inspector.Title}, " +
                $"breadcrumb {string.Join(" › ", Workspace.Inspector.Crumbs.Select(crumb => crumb.Label))}.";
        }

        public static Control FindTarget(string state, string what, Func<Control> target)
        {
            try
            {
                return target();
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException($"State '{state}' could not find {what}.", exception);
            }
        }

        public Task TryTheSampleWord()
        {
            Workspace.Context.TryWord("hawajafika");
            return Workspace.Assess.Trace.TryCommand.ExecutionTask!;
        }

        /// <summary>The visible word rows of the list named <paramref name="list"/> in the rows' automation ids.</summary>
        public IEnumerable<WordRow> WordRows(string list) => Visible<WordRow>(row => row.List == list);

        /// <summary>Opens the first row of a list from the keyboard, as Enter on a focused row does.</summary>
        public async Task<string> OpenWordRow(string list)
        {
            await Until(() => WordRows(list).Any(), $"the {list} rows");
            var row = WordRows(list).First();
            if (!row.IsOpen)
            {
                var body = row.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("wordRowBody"));
                body.BringIntoView();
                body.Focus(NavigationMethod.Tab);
                Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Until(() => row.IsOpen, $"the opened {list} row");
            }
            return $"Pressed Enter on '{row.Row?.Word}' in the {list} rows; its card is open inside the row.";
        }

        public Task CloseWordRows()
        {
            foreach (var row in Window.GetVisualDescendants().OfType<WordRow>()) row.IsOpen = false;
            PageScreenshots.Settle(Window);
            return Task.CompletedTask;
        }

        public async Task ShowTimingWords()
        {
            Open(WorkspacePage.Timing);
            var timing = Workspace.PageModel<TimingPageModel>();
            await Until(() => timing.RuleRows.Count > 0, "Timing's rules");
            if (timing.CostliestRuleWordRows.Count == 0) await timing.ChooseRuleCommand.ExecuteAsync(timing.RuleRows[0].Row);
            await Until(() => WordRows("timing-rule-words").Any(), "the chosen rule's costliest words");
        }

        public void ShowWordList()
        {
            Workspace.PageModel<TextsPageModel>().ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
            Open(WorkspacePage.Texts, TextsTab.AnalyzeTexts);
        }

        // A second run in which kitabu loses its analysis, so What changed has a move to show, and that move chosen.
        public async Task ParseWithKitabuLost()
        {
            Client.AssessCompletesWith(parsed with
            {
                Words = [.. parsed.Words.Select(word => word.Word != "kitabu" ? word : word with
                {
                    Outcome = "no-analysis",
                    Morphology = null,
                    Readings = [],
                    ReadingGrades = [],
                })],
            });
            await Workspace.Assess.RunCommand.ExecuteAsync(null);
            var difference = Workspace.Assess.Difference;
            await Until(() => difference.Moves.Count > 0, "a move between the two runs");
            difference.SelectedMove = difference.Moves.First(move => move.Words.Any(word => word.Word == "kitabu"));
        }

        public async Task ParseAgain()
        {
            Client.AssessCompletesWith(parsed);
            await Workspace.Assess.RunCommand.ExecuteAsync(null);
        }

        /// <summary>Lets the dispatcher run until <paramref name="done"/> holds, failing by name if it never does.</summary>
        public async Task Until(Func<bool> done, string what)
        {
            for (var pass = 0; pass < 50 && !done(); pass++)
            {
                await Task.Yield();
                PageScreenshots.Settle(Window);
            }
            if (!done()) throw new InvalidOperationException($"The window never showed {what}.");
        }

        /// <summary>Closes what a state opened and moves the pointer and focus away, so the next state starts clean.</summary>
        public void Reset()
        {
            Workspace.Context.CloseInspector();
            foreach (var menu in _menus) menu.Hide();
            _menus.Clear();
            foreach (var owner in _tips)
            {
                ToolTip.SetIsOpen(owner, false);
                owner.ClearValue(ToolTip.ShowDelayProperty);
            }
            _tips.Clear();
            var away = new Point(4, Window.Bounds.Height - 4);
            Window.MouseMove(away);
            if (_pressed is not null) Window.MouseUp(away, MouseButton.Left);
            _pressed = null;
            Window.FocusManager?.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
            PageScreenshots.Settle(Window);
            if (Window.FocusManager?.GetFocusedElement() is Control kept)
                throw new InvalidOperationException($"Focus stayed on {kept.GetType().Name} after the reset.");
        }

        private Point CentreOf(Control control)
        {
            control.BringIntoView();
            PageScreenshots.Settle(Window);
            return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
                ?? throw new InvalidOperationException($"{control.GetType().Name} is not in the window.");
        }
    }
}

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TryWordReviewScreenshots
{
    [ScreenshotFact]
    public void CapturePlainReviewScenes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            FakeCommandClient? client = null;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (fake, assessment) =>
            {
                client = fake;
                OverviewTimingScreenshots.ReadOverviewAndTiming(fake, assessment);
                fake.AssessCompletesWith(assessment with
                {
                    Measurements = [new ProducedAssessmentReference("assessment/one", "ParseTime", "assessment/one")],
                });
            });
            var page = workspace.PageModel<TryWordPageModel>();
            var trace = page.Trace;
            var defaultWordContext = client!.WordContextHandler;
            var project = workspace.Context.ProjectPath;
            var sample = WordTraceQuery.LoadDiagnostic(File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!;
            var captured = WordTraceQuery.LoadDiagnostic(SIL.Motif.Tests.TestFixtures.TraceEnvelope.CapturedRuleLabel).Value!;
            var unknown = WordTraceQuery.LoadDiagnostic(SIL.Motif.Tests.TestFixtures.TraceEnvelope.CapturedRuleLabel
                .Replace("RequiredSyntacticFeatureStruct", "UnknownFutureCode", StringComparison.Ordinal)).Value!;
            var interrupted = new WordTraceResponse("dogs", false, false,
                "The parser stopped at a search limit after 1,000,000 analysis attempts, so this trace is not the whole search.",
                1_000_000, null, 1, TraceReadingBuilder.Build("dogs",
                    new TraceStep("WordAnalysis", null, "dogs", null, null,
                    [new TraceStep("MorphologicalRuleAnalysis", "Plural", "dogs", "dog", null, [])
                    { OutcomeStatus = "attempted" }]), [], []));
            try
            {
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var width in new[] { 1040, 1240 })
                    {
                        window.Width = width;
                        window.Height = 780;
                        await Show(sample, timing: true);
                        Save("earlier-timing");
                        window.Height = 1500;
                        Save("earlier-timing-tall");
                        window.Height = 780;
                        var input = Named<TextBox>("Word to try");
                        window.MouseMove(input.TranslatePoint(new Point(20, 10), window)!.Value);
                        Save("tools-hover");
                        var tools = Named<Button>("Try a Word tools");
                        tools.Focus(NavigationMethod.Tab);
                        Save("tools-focus");
                        tools.Flyout!.ShowAt(tools);
                        Save("tools-menu");
                        tools.Flyout.Hide();
                        Named<Button>("Open in Analyze texts").Focus(NavigationMethod.Tab);
                        Save("result-actions-focus");

                        window.Height = 1500;
                        await Show(sample, timing: true);
                        SelectPlain("0.2.0.3.1");
                        var selectedCard = window.GetVisualDescendants().OfType<TextBlock>()
                            .Single(block => block.IsEffectivelyVisible && block.Text == trace.SelectedStep!.PlainRefusalText);
                        var card = selectedCard.FindAncestorOfType<Border>()!;
                        card.BringIntoView();
                        PageScreenshots.Settle(window);
                        var cardOrigin = card.TranslatePoint(default, window)!.Value;
                        Assert.True(cardOrigin.Y + card.Bounds.Height <= window.ClientSize.Height + 1,
                            "The selected rejection card must fit inside the captured viewport.");
                        Save("selected-rejection");
                        await Show(unknown);
                        SelectPlain("0.0");
                        Save("unknown-reason");
                        await Show(captured);
                        SelectPlain("0.0");
                        Save("captured-labels-live");
                        trace.Reset();
                        trace.SetProjectPath(null);
                        trace.Result = TraceWordViewModel.FromDiagnosticJson(captured.DiagnosticJson).Result;
                        SelectPlain("0.0");
                        Save("captured-labels-reopened");
                        trace.SetProjectPath(project);
                        await Show(interrupted);
                        Save("interrupted-progress");
                        trace.Result = interrupted with { StopReason = null };
                        Save("incomplete-reason-unavailable");
                        trace.IsExpert = true;
                        await Show(sample, timing: true);
                        window.Height = 2400;
                        Named<Expander>("Full derivation tree").IsExpanded = true;
                        trace.RuleFilter = "lu";
                        PageScreenshots.Settle(window);
                        Named<Expander>("Full derivation tree").BringIntoView();
                        Save("expanded-filters");

                        trace.IsExpert = true;
                        await Show(sample, timing: true);
                        window.Height = 5000;
                        trace.ShowDroppedPaths = true;
                        var groupIndex = trace.Reading!.StopGroups.ToList().FindIndex(group =>
                            group.Attempts.Any(attempt => attempt.AttemptId == "0.2.0.3.2"));
                        trace.SelectStopGroupCommand.Execute(trace.StopGroups[groupIndex]);
                        PageScreenshots.Settle(window);
                        foreach (var expander in window.GetVisualDescendants().OfType<Expander>()
                                     .Where(expander => expander.IsEffectivelyVisible && expander.Header?.ToString() == "Steps on this path"))
                            expander.IsExpanded = true;
                        Assert.Single(trace.ClosestAttempts).IsTreeContextExpanded = true;
                        PageScreenshots.Settle(window);
                        Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(),
                            block => block.IsEffectivelyVisible && block.Text == "Recorded event: 0.0");
                        var finalContextRow = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                            .Single(block => block.IsEffectivelyVisible && block.Text == "Recorded event: 0.2.0.3.1")
                            .FindAncestorOfType<StackPanel>()!;
                        var contextOrigin = finalContextRow.TranslatePoint(default, window)!.Value;
                        Assert.True(contextOrigin.Y + finalContextRow.Bounds.Height <= window.ClientSize.Height + 1,
                            "The final recorded context row must fit inside the expanded-path capture.");
                        Save("steps-expanded");

                        window.Height = 1500;
                        trace.IsExpert = true;
                        await Show(sample with
                        {
                            HostCapture = (sample.HostCapture ?? new TraceHostCapture(null, null, null, null, null, null, [])) with
                            {
                                Baseline = new TraceBaselineSource(workspace.Context.Evidence.Assessment!.Assessment.Baseline.Token,
                                    DateTimeOffset.Parse("2026-09-22T09:18:00Z"), DateTimeOffset.Parse("2026-09-22T10:00:00Z"),
                                    "Returned trace Baseline"),
                            },
                        });
                        Save("grammar-source");
                        var measuredBaseline = workspace.Context.Evidence.Assessment!.Assessment.Baseline.Token;
                        var differentBaseline = new BaselineToken(measuredBaseline.ProjectIdentity,
                            "sha256:" + new string('c', 64), measuredBaseline.ProjectionVersion,
                            "2026-09-22T11:00:00Z", "sha256:" + new string('d', 64));
                        await Show(WordTraceQuery.LoadDiagnostic(PageScreenshots.SampleTrace()).Value! with
                        {
                            ParserElapsedMs = null,
                            HostCapture = new TraceHostCapture(null, null, null, differentBaseline.BundleDigest, null, null, [])
                            {
                                Baseline = new TraceBaselineSource(differentBaseline,
                                    DateTimeOffset.Parse("2026-09-22T10:48:00Z"), DateTimeOffset.Parse(differentBaseline.CapturedUtc),
                                    "Different Baseline capture"),
                            },
                        }, timing: true, timingBaseline: measuredBaseline);
                        Assert.NotEmpty(page.TraceBaselineWarning);
                        Save("different-baselines");

                        await Show(WordTraceQuery.LoadDiagnostic(SIL.Motif.Tests.TestFixtures.TraceEnvelope.AnalysisRecords()).Value!);
                        window.GetVisualDescendants().OfType<Expander>()
                            .Single(expander => AutomationProperties.GetName(expander) == "Recorded source analyses").IsExpanded = true;
                        Save("source-analyses");

                        window.Height = 1500;
                        foreach (var (scene, context) in new[]
                        {
                            ("fieldworks-mixed-opinions", new WordContextResponse("matinlu", true)
                            {
                                IsInFieldWorks = true, Baseline = workspace.Context.Evidence.Assessment!.Assessment.Baseline.Token,
                                SourceLastWriteUtc = DateTimeOffset.Parse("2026-09-22T09:18:00Z"), IsStale = true,
                                Analyses = new string?[] { "approved", "disapproved", "candidate", null }.Select(opinion =>
                                    new ParserReading([new("matin", "stem", "n", null, false, null),
                                        new("lu", "suffix", string.Empty, null, false, null)])
                                    { StoredAnalysisOpinion = opinion }).ToArray(),
                            }),
                            ("fieldworks-empty", new WordContextResponse("matinlu", true) { IsInFieldWorks = true }),
                            ("fieldworks-absent", new WordContextResponse("matinlu", true) { IsInFieldWorks = false }),
                            ("fieldworks-membership-unknown", new WordContextResponse("matinlu", true)),
                        })
                        {
                            client!.WordContextHandler = (_, _) => Task.FromResult(CommandOutcome<WordContextResponse>.Success(context));
                            await Show(sample);
                            Save(scene);
                        }
                        client!.WordContextHandler = defaultWordContext;
                        trace.IsExpert = false;
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }

            async Task Show(WordTraceResponse response, bool timing = false, BaselineToken? timingBaseline = null)
            {
                trace.RuleFilter = string.Empty;
                trace.SearchText = string.Empty;
                trace.SetProjectPath(project);
                client!.TraceWordCompletesWith(response);
                if (timing) OverviewTimingScreenshots.ReadOverviewAndTiming(client, workspace.Context.Evidence.Assessment!.Assessment);
                else client.TimingCompletesWith(new TimingResponse("assessment/one", "selected", "rule", 0, 0, 0, [], [], []));
                if (timingBaseline is not null)
                    client.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
                        SampleEvidence.Timing(workspace.Context.Evidence.Assessment!.Assessment, request.By, request.Rule,
                            request.ExplicitWords) with { Baseline = timingBaseline })));
                workspace.Context.TryWord(response.Word);
                await trace.TryCommand.ExecutionTask!;
                PageScreenshots.Settle(window);
                if (trace.IsExpert) Named<Expander>("Full derivation tree").IsExpanded = false;
                var panel = window.GetVisualDescendants().OfType<TryWordPanel>().Single();
                panel.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = default;
                window.FocusManager?.Focus(null, NavigationMethod.Unspecified, KeyModifiers.None);
                window.MouseMove(default);
                PageScreenshots.Settle(window);
            }

            void SelectPlain(string address)
            {
                PageScreenshots.Settle(window);
                trace.SelectedStep = trace.ExpertEvents.Single(step => step.RecordedStep.StepId == address);
                PageScreenshots.Settle(window);
                var list = Named<ListBox>("Plain trace steps");
                var step = trace.PlainSteps.Single(step => step.RecordedStep.StepId == address);
                list.SelectedItem = step;
                PageScreenshots.Settle(window);
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(step));
                row.BringIntoView();
                PageScreenshots.Settle(window);
                Assert.Equal(address, trace.SelectedStep!.RecordedStep.StepId);
            }

            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>()
                .Single(control => AutomationProperties.GetName(control) == name);

            void Save(string scene)
            {
                PageScreenshots.AssertSceneHasExpectedErrorState(window, workspace, $"try-a-word/{scene}");
                PageScreenshots.Save(window,
                    Path.Combine(folder, $"state-try-a-word-{scene}-{(int)window.Width}-{(Application.Current!.RequestedThemeVariant == ThemeVariant.Dark ? "dark" : "light")}.png"));
            }
        }, TimeSpan.FromMinutes(3));
    }
}
