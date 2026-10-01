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
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
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
                    foreach (var (theme, variant) in Themes)
                    {
                        Application.Current!.RequestedThemeVariant = variant;
                        foreach (var width in Widths)
                        {
                            window.Width = width;
                            window.Height = state.Height;
                            PageScreenshots.Settle(window);
                            var note = await state.Show(stage);
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

    /// <summary>One interactive state: its page, how one picture reaches it, and any run it needs.</summary>
    private sealed record State(string Page, string Name, Func<Stage, Task<string>> Show)
    {
        public Func<Stage, Task>? Setup { get; init; }

        public Func<Stage, Task>? Teardown { get; init; }

        public int Height { get; init; } = 780;
    }

    private const string MatrixWords = "Words in the chosen cells";
    private const string ListWords = "Words in the selected list";

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
                        DateTimeOffset.UtcNow.AddDays(-2)))]);
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
        yield return new("overview", "tile-hover", stage => stage.Hover(WorkspacePage.Overview,
            () => stage.Named<Button>("Open Speed in Timing"), "the Speed tile"));
        yield return new("overview", "tile-focus", stage => stage.FocusFromKeyboard(WorkspacePage.Overview,
            () => stage.Named<Button>("Open Speed in Timing"), "the Speed tile"));
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
        yield return new("matrix", "link-hover", stage => stage.Hover(WorkspacePage.Texts,
            () => stage.Visible<HyperlinkButton>(link => link.Content as string == "Open in text").First(), "Open in text",
            TextsTab.Matrix))
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
        yield return new("analyze", "disapproved-tooltip", stage => stage.Hover(WorkspacePage.Texts,
            () => stage.Strip("walikula").GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("markChip") && border.IsEffectivelyVisible),
            "the Built anyway mark on walikula", TextsTab.AnalyzeTexts));
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
            var button = stage.Named<Button>("AI Handoff for ticked words in the selected list");
            return $"{(button.IsEffectivelyEnabled ? "Enabled" : "Disabled")} button. " +
                await stage.Hover(null, () => button, "AI Handoff for ticked words");
        });

        // Try a Word.
        yield return new("try-a-word", "tools-menu", stage => stage.OpenMenu(WorkspacePage.TryAWord,
            () => stage.Named<Button>("Try a Word tools"), "Try a Word tools"))
        { Setup = stage => stage.TryTheSampleWord() };
        yield return new("try-a-word", "rule-hover", stage => stage.Hover(WorkspacePage.TryAWord,
            () => stage.Visible<Button>(button => button.Classes.Contains("ruleRow")).First(), "the first rule row"))
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

        // Timing.
        yield return new("timing", "rule-hover", stage => stage.Hover(WorkspacePage.Timing,
            () => stage.Visible<Button>(button => button.Classes.Contains("timingRuleRow")).First(), "the first rule row"));
        yield return new("timing", "statistics-expanded", stage => stage.Expand(WorkspacePage.Timing,
            () => stage.Visible<Expander>(expander => expander.Header as string == "Detailed statistics").First(),
            "Detailed statistics"))
        { Height = 1300 };

        // Warnings.
        yield return new("warnings", "part-link-hover", stage => stage.Hover(WorkspacePage.Warnings,
            () => stage.Visible<Button>(button => ToolTip.GetTip(button) is string tip && tip.StartsWith("Open this")).First(),
            "a FieldWorks link in a finding"));
        yield return new("warnings", "row-opened", stage => stage.OpenWarningRow("conversion.unsegmentable-form", named: true))
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
            return "Timing with a rule chosen: its costliest words and the slowest words as word rows.";
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
        yield return new("timing", "slowest-word-row-hover", async stage =>
        {
            await stage.ShowTimingWords();
            var row = stage.WordRows("timing-words").First();
            return await stage.Hover(null, () => row.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("wordRowBody")), "a slowest word row");
        })
        { Height = 1700 };
        yield return new("timing", "word-row-opened", async stage =>
        {
            await stage.ShowTimingWords();
            return await stage.OpenWordRow("timing-words");
        })
        { Height = 1700, Teardown = stage => stage.CloseWordRows() };
        yield return new("overview", "slowest-row-opened", async stage =>
        {
            stage.Open(WorkspacePage.Overview);
            return await stage.OpenWordRow("overview-slowest");
        })
        { Height = 1000, Teardown = stage => stage.CloseWordRows() };
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
            () => stage.Named<Button>("Drag all AI Handoff files"), "the drag-all button"));
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
                    new AssessmentProgress(AssessmentStage.Parsing, 57, 142, "Parsing hawajafika"));
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
            Setup = async stage =>
            {
                stage.Client.AssessRefusesWith(new Refusal(RefusalCodes.AssessParserUnavailable, FailureReason.Refused,
                    "PanGloss exited with code 3: the grammar file could not be read (pangloss-win-x64.exe, v0.5.2)."));
                await stage.Workspace.Assess.RunCommand.ExecuteAsync(null);
            },
            Teardown = stage => stage.ParseAgain(),
        };
        yield return new("try-a-word", "refusal", stage => stage.ExpandRefusal(WorkspacePage.TryAWord, null))
        {
            Setup = async stage =>
            {
                stage.Client.OnTraceWord((_, _) => Task.FromResult(CommandOutcome<WordTraceResponse>.Refused(
                    new Refusal(RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused,
                        "PanGloss exited with code 3: the grammar file could not be read (pangloss-win-x64.exe, v0.5.2)."))));
                stage.Workspace.Context.TryWord("kitabu");
                await stage.Workspace.Assess.Trace.TryCommand.ExecutionTask!;
            },
        };
    }

    // Two matching frames in a row, so no chevron is caught turning; a blinking caret ends the bounded run instead.
    private static void Save(MainWindow window, string path)
    {
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

        public Border Strip(string form) =>
            Visible<Border>(border => border.Name == "WordStrip" && border.DataContext is ResultsTokenViewModel token &&
                token.Form == form).First();

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
            var grid = Named<DataGrid>("Grammar warnings");
            var row = grid.ItemsSource!.OfType<GrammarWarningRowViewModel>()
                .First(candidate => candidate.GroupCode == code && candidate.NamesNoItem != named);
            grid.SelectedItem = row;
            grid.ScrollIntoView(row, null);
            await Until(() => Visible<TextBlock>(block => block.Text == "What to do in FieldWorks").Any(), "the row's advice");
            return $"Opened the {row.GroupName} row{(named ? $" for {row.Where}" : ", which names no item")}.";
        }

        public Task CloseWarningRow()
        {
            Named<DataGrid>("Grammar warnings").SelectedItem = null;
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
        public Task TryTheSampleWord()
        {
            Workspace.Context.TryWord("matinlu");
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
            await Until(() => WordRows("timing-rule-words").Any() && WordRows("timing-words").Any(), "Timing's word rows");
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
