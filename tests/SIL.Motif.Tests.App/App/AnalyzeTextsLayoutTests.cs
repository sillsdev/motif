using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Renders Analyze texts over the sample project and checks the layout claims Round 4 makes for it: the word
/// card is a raised surface opened under its own line, the controls share one row, the text shows before the
/// first parse, and each word strip carries the Word, FieldWorks and PanGloss lines with one action and one Fix.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnalyzeTextsLayoutTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Fact]
    public void TheOpenWordCardHasARaisedSurfaceAndAShadowInBothThemes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    Settle(window);
                    var card = OpenCard(window);
                    Assert.True(Application.Current.TryGetResource("Intent.Surface.Raised", variant, out var surface));
                    Assert.Same(surface, card.Background);
                    Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color.A);
                    Assert.True(Application.Current.TryGetResource("Intent.Shadow.Raised", variant, out var shadow));
                    Assert.Equal(shadow, card.BoxShadow);
                    Assert.True(card.BoxShadow.Count > 0);
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheWordCardOpensUnderItsLineAndCoversNoWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var panel = Panel(window);
                var card = OpenCard(window);
                var open = Assert.IsType<ResultsTokenViewModel>(card.DataContext);
                Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Popup>(), popup => popup.IsOpen);

                var cardBounds = BoundsIn(card, panel);
                var strips = Strips(panel).ToArray();
                var ownStrip = Assert.Single(strips, strip => ReferenceEquals(strip.Tag, open));
                Assert.True(cardBounds.Top >= BoundsIn(ownStrip, panel).Bottom,
                    $"The card starts at {cardBounds.Top}, above the bottom of its word at {BoundsIn(ownStrip, panel).Bottom}.");
                Assert.All(strips, strip => Assert.False(BoundsIn(strip, panel).Intersects(cardBounds),
                    $"The card covers {((ResultsTokenViewModel)strip.Tag!).Form}."));
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void ArrowKeysMoveTheCardBetweenWordsAndEscapeClosesItBackOntoTheWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var alikula = inText.SelectedToken!;
                inText.CloseTokenCard();
                Settle(window);
                await inText.OpenTokenCardAsync(alikula);
                Settle(window);
                var first = OpenCard(window);
                Assert.True(first.IsKeyboardFocusWithin, "Opening the card puts the keyboard in it.");

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                await inText.ReadStateRefresh;
                Settle(window);
                Assert.Equal("chakula", inText.SelectedToken?.Form);
                var moved = OpenCard(window);
                Assert.Same(inText.SelectedToken, moved.DataContext);
                Assert.True(moved.IsKeyboardFocusWithin, "The keyboard follows the card to the next word.");

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Null(inText.SelectedToken);
                Assert.DoesNotContain(Panel(window).GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("wordCard"));
                var strip = Assert.Single(Strips(Panel(window)), candidate =>
                    candidate.Tag is ResultsTokenViewModel { Form: "chakula" });
                Assert.True(strip.IsFocused, "Escape returns the keyboard to the word whose card closed.");

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                Settle(window);
                Assert.Null(inText.SelectedToken);
                Assert.True(StripOf(Panel(window), "watoto").IsFocused, "With no card open, arrows move between words.");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheTextPickerChipsMarkReadAndSelectShareOneRowAboveTheText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var controls = new List<Control>
                {
                    Named<ComboBox>(panel, "Text to read"),
                    Named<Button>(panel, "Mark read or unread"),
                    Named<Button>(panel, "Select words for actions"),
                };
                var chips = panel.GetVisualDescendants().OfType<FilterChip>()
                    .Where(chip => chip.IsEffectivelyVisible).ToArray();
                Assert.Equal(["All", "Unread", "Differs", "Not in FieldWorks", "No parse", "Stopped"],
                    chips.Select(chip => chip.Label));
                controls.AddRange(chips);

                var picked = Assert.Single(controls[0].GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Hadithi ya sungura");
                Assert.DoesNotContain(picked.TextLayout.TextLines, line => line.HasCollapsed);

                var bounds = controls.Select(control => BoundsIn(control, panel)).ToArray();
                var rowTop = bounds.Min(rect => rect.Top);
                var rowBottom = bounds.Max(rect => rect.Bottom);
                Assert.True(bounds.Max(rect => rect.Top) < bounds.Min(rect => rect.Bottom),
                    "The controls wrap: " + string.Join(", ", controls.Zip(bounds, (control, rect) =>
                        $"{control.GetType().Name} at {rect}")));
                Assert.True(rowBottom - rowTop <= 40, $"The control row is {rowBottom - rowTop} px deep.");
                var firstWord = Strips(panel).Min(strip => BoundsIn(strip, panel).Top);
                Assert.True(firstWord - rowBottom <= 48, $"The text starts {firstWord - rowBottom} px below the controls.");
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Expander>(), expander =>
                    expander.IsEffectivelyVisible && Equals(expander.Header, "Actions by scope"));
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void WordCheckboxesWaitUntilTheReaderChoosesWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                bool AnyCheckboxShows() => Panel(window).GetVisualDescendants().OfType<CheckBox>().Any(box =>
                    box.IsEffectivelyVisible && box.DataContext is ResultsTokenViewModel);
                Assert.False(AnyCheckboxShows());

                inText.ChooseWordsCommand.Execute(null);
                Settle(window);
                Assert.True(AnyCheckboxShows());

                inText.ChooseWordsCommand.Execute(null);
                Settle(window);
                Assert.False(AnyCheckboxShows());
                inText.SelectAllWordsCommand.Execute(null);
                Settle(window);
                Assert.True(AnyCheckboxShows(), "Checked words keep their checkboxes so they can be cleared.");
                Assert.Equal($"{inText.AllCount} selected ▾", inText.SelectMenuLabel);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void BeforeTheFirstParseTheTextShowsUnderTheParsePrompt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts(parse: false);
            try
            {
                Assert.True(workspace.Context.NeedsAssessment);
                var page = Assert.Single(window.GetLogicalDescendants().OfType<TextsPage>());
                var prompt = Assert.Single(page.GetVisualDescendants().OfType<ParsePrompt>(),
                    candidate => candidate.IsEffectivelyVisible);
                var action = Assert.Single(prompt.GetVisualDescendants().OfType<Button>());
                Assert.Equal(workspace.Context.ParsePromptActionText, action.Content);

                var panel = Panel(window);
                var strips = Strips(panel).ToArray();
                Assert.Equal(["Sungura", "alikula", "chakula"],
                    strips.Take(3).Select(strip => ((ResultsTokenViewModel)strip.Tag!).Form));
                Assert.True(BoundsIn(prompt, page).Bottom <= BoundsIn(strips[0], page).Top,
                    "The prompt sits above the text.");
                var sungura = strips[0];
                Assert.Contains(sungura.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "hare" && text.IsEffectivelyVisible);
                Assert.Contains(sungura.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Not parsed yet" && text.IsEffectivelyVisible);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<FilterChip>(), chip => chip.IsEffectivelyVisible);
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(button) is
                        "Mark read or unread" or "Select words for actions");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void EachLineHasAWordFieldWorksPanGlossGutterAlignedWithItsStrips()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var sungura = StripOf(panel, "Sungura");
                var line = sungura.GetVisualAncestors().OfType<ContentPresenter>()
                    .First(presenter => presenter.DataContext is ResultsLineViewModel);
                foreach (var (label, part) in new[] { ("Word", "word"), ("FieldWorks", "fieldworks"), ("PanGloss", "pangloss") })
                {
                    var gutter = Assert.Single(line.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.Text == label && text.Classes.Contains("gutterLabel"));
                    var row = Part(sungura, part);
                    Assert.True(Math.Abs(BoundsIn(gutter, panel).Top - BoundsIn(row, panel).Top) <= 2,
                        $"{label} sits at {BoundsIn(gutter, panel).Top}, its row at {BoundsIn(row, panel).Top}.");
                }
                Assert.Contains(line.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Sungura alikula chakula." && text.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void StripsReadAsRoundFour()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                Assert.Equal("= same", VisibleText(Part(StripOf(panel, "anapenda"), "pangloss")));
                Assert.Equal("∅ No parse", VisibleText(Part(StripOf(panel, "hawajafika"), "pangloss")));
                Assert.Equal("Stopped at the step limit", VisibleText(Part(StripOf(panel, "mwalimu"), "pangloss")));
                Assert.StartsWith("Nothing in FieldWorks", VisibleText(Part(StripOf(panel, "chakula"), "fieldworks")), StringComparison.Ordinal);
                Assert.Equal("ch- 7 akula food", VisibleText(Part(StripOf(panel, "chakula"), "pangloss")));
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Different from FieldWorks");

                foreach (var strip in Strips(panel))
                {
                    var form = ((ResultsTokenViewModel)strip.Tag!).Form;
                    Assert.DoesNotContain(strip.GetVisualDescendants().OfType<Expander>(), _ => true);
                    var fixes = strip.GetVisualDescendants().OfType<Button>().Where(button =>
                        Avalonia.Automation.AutomationProperties.GetName(button) == "Fix actions from the word strip").ToArray();
                    Assert.True(fixes.Length <= 1, $"{form} offers {fixes.Length} Fix controls.");
                    Assert.All(fixes, fix =>
                    {
                        Assert.Equal("Fix ▾", fix.Content);
                        Assert.NotNull(fix.Flyout);
                    });
                    Assert.Equal(strip.BorderThickness.Top, strip.BorderThickness.Bottom);
                    Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(strip.BorderBrush).Color.A);

                    var links = strip.GetVisualDescendants().OfType<HyperlinkButton>().ToArray();
                    var link = Assert.Single(links);
                    Assert.Equal($"Open {form} in FieldWorks", Avalonia.Automation.AutomationProperties.GetName(link));
                    Assert.Equal(0, link.Opacity);
                    Assert.True(link.Focusable && link.IsTabStop, $"The FieldWorks link of {form} leaves the tab order.");
                }

                var alikula = StripOf(panel, "alikula");
                var unread = Part(alikula, "unread");
                Assert.True(BoundsIn(unread, panel).Right <= BoundsIn(Part(alikula, "word"), panel).Left,
                    "The Unread dot comes before the word.");
                Assert.DoesNotContain(unread.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible);
                var alikulaLink = Assert.Single(alikula.GetVisualDescendants().OfType<HyperlinkButton>());
                Assert.True(alikulaLink.Focus(NavigationMethod.Tab));
                Settle(window);
                Assert.Equal(1, alikulaLink.Opacity);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void AStagedAddSaysWillAddAsApprovedAndCoversNoOtherWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                inText.CloseTokenCard();
                var chakula = inText.VisibleLines[0].Tokens.Single(token => token.Form == "chakula");
                var add = Assert.Single(chakula.Marking.FixChoices, choice => choice.Label == "Add as Approved");
                await chakula.StageMarkingChoiceForTokenCommand!.ExecuteAsync(add);
                Settle(window);

                var panel = Panel(window);
                var staged = Part(StripOf(panel, "chakula"), "staged");
                Assert.Contains("Will add as Approved", VisibleText(staged), StringComparison.Ordinal);
                var stagedBounds = BoundsIn(staged, panel);
                Assert.All(Strips(panel).Where(strip => !ReferenceEquals(strip.Tag, chakula)), strip =>
                    Assert.False(BoundsIn(strip, panel).Intersects(stagedBounds),
                        $"The staged note covers {((ResultsTokenViewModel)strip.Tag!).Form}."));
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void ThreeLinesOfTextFitAboveTheFoldAt1240()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var panel = Panel(window);
                var viewer = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(), candidate =>
                    candidate.IsEffectivelyVisible && candidate.Content is ItemsControl);
                var kitabu = BoundsIn(StripOf(panel, "kitabu"), viewer);
                Assert.True(kitabu.Bottom <= viewer.Viewport.Height,
                    $"Line 3 ends at {kitabu.Bottom}, below the {viewer.Viewport.Height} px the reader shows.");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    internal static Border StripOf(ResultsInTextPanel panel, string form) =>
        Assert.Single(Strips(panel), strip => strip.Tag is ResultsTokenViewModel token && token.Form == form);

    internal static Control Part(Border strip, string part) =>
        Assert.Single(strip.GetVisualDescendants().OfType<Control>(), control =>
            (Avalonia.Automation.AutomationProperties.GetAutomationId(control) ?? string.Empty)
                .EndsWith("-" + part, StringComparison.Ordinal) && control.IsEffectivelyVisible);

    internal static string VisibleText(Control part) => string.Join(" ",
        part.GetVisualDescendants().Prepend(part).OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text));

    internal static T Named<T>(Visual root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control =>
            Avalonia.Automation.AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);

    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenAnalyzeTexts(
        int width = 1240, bool parse = true)
    {
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: parse);
        window.Width = width;
        window.Height = 780;
        workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
        workspace.CurrentPage = WorkspacePage.Texts;
        Settle(window);
        return (workspace, window);
    }

    internal static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    internal static ResultsInTextPanel Panel(Window window) =>
        Assert.Single(window.GetLogicalDescendants().OfType<ResultsInTextPanel>());

    internal static Border OpenCard(Window window) =>
        Assert.Single(Panel(window).GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("wordCard") && border.IsEffectivelyVisible);

    internal static IEnumerable<Border> Strips(ResultsInTextPanel panel) =>
        panel.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Name == "WordStrip" && border.IsEffectivelyVisible);

    internal static Rect BoundsIn(Visual visual, Visual relativeTo)
    {
        var origin = visual.TranslatePoint(new Point(0, 0), relativeTo) ??
            throw new InvalidOperationException("The visual is not under the panel.");
        return new Rect(origin, visual.Bounds.Size);
    }
}
