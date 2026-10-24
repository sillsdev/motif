using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins WCAG AA on the four places the page review measured below it: 4.5:1 for text, 3:1 for a chart's parts.
/// Each ratio is taken from what the window paints, its text's own brush over every background and opacity
/// above it, in the light and the dark theme, so a fix in the tokens is what turns these green.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ContrastTests(AvaloniaHeadlessFixture avalonia)
{
    private const double Text = 4.5;
    private const double Part = 3.0;
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    public static TheoryData<string> SolidPrimaryButtons() => new() { "setup", "review" };

    // Setup names its button by class; Review changes asks Semi for the solid theme and its Primary class.
    [Theory]
    [MemberData(nameof(SolidPrimaryButtons))]
    public void ASolidPrimaryButtonsLabelIsReadableInBothThemes(string which)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var button = new Button { Content = "Start first run" };
                if (which == "setup") button.Classes.Add("setupPrimary");
                else
                {
                    button.Classes.Add("Primary");
                    button.Theme = (ControlTheme)Application.Current!.FindResource("SolidButton")!;
                }
                AssertReadableIn(button, theme, $"{theme} {which} solid button");
            }
        });
    }

    [Fact]
    public void AButtonInANoticeIsReadableInBothThemes()
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var button = new Button { Content = "Try again" };
                AssertReadableIn(new Border { Classes = { "notice" }, Child = button }, theme, $"{theme} notice button");
            }
        });
    }

    [Theory]
    [InlineData("ButtonSolidPrimaryPointeroverBackground")]
    [InlineData("ButtonSolidPrimaryPressedBackground")]
    public void ASolidPrimaryButtonStaysReadableUnderThePointer(string fill)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var ratio = Ratio(Resolve("ButtonSolidForeground", theme), Resolve(fill, theme));
                Assert.True(ratio >= Text, $"{theme} {fill}: {ratio:F2}:1");
            }
        });
    }

    // WCAG 1.4.11 asks 3:1 of a focus indicator against what surrounds it.
    [Theory]
    [InlineData("Intent.Surface")]
    [InlineData("Intent.Surface.Subtle")]
    public void TheKeyboardFocusRingStandsOutInBothThemes(string surface)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var ratio = Ratio(Resolve("Intent.Focus", theme), Resolve(surface, theme));
                Assert.True(ratio >= Part, $"{theme} Intent.Focus on {surface}: {ratio:F2}:1");
            }
        });
    }

    // Each mark's text, then the fill it sits on; a mark with no fill of its own sits on the page or a card.
    public static TheoryData<string, string?> MarkTexts() => new()
    {
        { "Intent.Outcome.Same", null },
        { "Intent.Outcome.Different", "Intent.Outcome.Different.Fill" },
        { "Intent.Outcome.NoParse", null },
        { "Intent.Outcome.Stopped", null },
        { "Intent.Outcome.NotParsed", null },
        { "Intent.Consequence.Fine", null },
        { "Intent.Consequence.Look", "Intent.Consequence.Look.Fill" },
        { "Intent.Consequence.Problem", "Intent.Consequence.Problem.Fill" },
        { "Intent.Consequence.Neutral", null },
        { "Intent.Severity.Warning", "Intent.Severity.Warning.Fill" },
        { "Intent.Severity.Error", null },
        { "Intent.Severity.Info", null },
    };

    [Theory]
    [MemberData(nameof(MarkTexts))]
    public void AMarksTextIsReadableOnItsFillAndOnThePageInBothThemes(string text, string? fill)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            foreach (var surface in new[] { fill, "Intent.Surface", "Intent.Surface.Raised" }.OfType<string>())
            {
                var ratio = Ratio(Resolve(text, theme), Resolve(surface, theme));
                Assert.True(ratio >= Text, $"{theme} {text} on {surface}: {ratio:F2}:1, needs {Text}:1");
            }
        });
    }

    [Fact]
    public void ComponentStatusTextMeetsWcagAaAgainstItsBackgroundInBothThemes()
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            foreach (var scene in ComponentStatusTextScenes())
            {
                var (content, text) = scene.Build();
                var window = new MotifTestWindow { Content = content, RequestedThemeVariant = theme, Width = 400, Height = 160 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var ratio = Effective(text);
                    if (ratio < Text) failures.Add($"{theme} {scene.What}: {ratio:F2}:1, needs {Text}:1");
                }
                finally
                {
                    window.Close();
                }
            }
        });

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private sealed record StatusTextScene(string What, Func<(Control Content, Visual Text)> Build);

    private static IEnumerable<StatusTextScene> ComponentStatusTextScenes()
    {
        yield return TextScene("Text error", "error", "error");
        yield return SelectableTextScene("warning missing", "warningMissing");
        yield return SelectableTextScene("warning value", "warningValue");
        yield return ReviewHeaderTextScene("review no-longer-fits title", "reviewGroupTitle");
        yield return ReviewHeaderTextScene("review no-longer-fits note", "reviewGroupNote");
        yield return ReviewFailedPillScene();
        yield return TextScene("freshness current", "freshLabel", "current");
        yield return TextScene("freshness stale", "freshLabel", "stale");
        yield return TextScene("freshness busy", "freshLabel", "busy");
        yield return TextScene("freshness refused", "freshLabel", "refused");
        yield return TextScene("Setup coverage", "setupCoverage");
        yield return TextScene("Setup error", "setupError");
        yield return TextScene("Try a Word failed", "failed");
        yield return TextScene("Try a Word succeeded", "succeeded");
        yield return TextScene("Try a Word stopped", "stopText");
        yield return TextScene("word presentation failure", "wordPresentationCardFailure");
    }

    private static StatusTextScene TextScene(string what, params string[] classes)
    {
        var text = StatusText(what, classes);
        return new StatusTextScene(what, () => (text, text));
    }

    private static StatusTextScene SelectableTextScene(string what, string @class)
    {
        var text = new SelectableTextBlock { Text = what };
        text.Classes.Add(@class);
        return new StatusTextScene(what, () => (text, text));
    }

    private static StatusTextScene ReviewHeaderTextScene(string what, string @class)
    {
        var text = StatusText(what, @class);
        var header = new Border { Classes = { "reviewGroupHeader" }, Child = text };
        var group = new Border { Classes = { "reviewGroup", "noLongerFits" }, Child = header };
        return new StatusTextScene(what, () => (group, text));
    }

    private static StatusTextScene ReviewFailedPillScene()
    {
        var text = StatusText("Apply failed");
        var pill = new Border { Classes = { "reviewPill", "failed" }, Child = text };
        return new StatusTextScene("review failed pill", () => (pill, text));
    }

    private static TextBlock StatusText(string text, params string[] classes)
    {
        var block = new TextBlock { Text = text };
        foreach (var @class in classes) block.Classes.Add(@class);
        return block;
    }

    [Fact]
    public void TheStatusBarsProblemReportIsLiveAndReadableInBothThemes()
    {
        var failures = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var priorTheme = Application.Current!.RequestedThemeVariant;
            var (_, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var banner = window.FindControl<Border>("StatusBar")!;
                foreach (var theme in Themes)
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    PageScreenshots.Settle(window);
                    foreach (var name in new[] { "Report a problem" })
                    {
                        var button = banner.GetVisualDescendants().OfType<Button>()
                            .Single(candidate => candidate.IsEffectivelyVisible && AutomationProperties.GetName(candidate) == name);
                        if (!button.IsEffectivelyEnabled) failures.Add($"{theme} '{name}' is disabled");
                        var text = button.GetVisualDescendants().OfType<TextBlock>().First();
                        var ratio = Effective(text);
                        if (ratio < Text) failures.Add($"{theme} '{name}': {ratio:F2}:1");
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = priorTheme;
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TheTechDemoNoticeAndItsButtonsAreLiveAndReadableInBothThemes()
    {
        var failures = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var priorTheme = Application.Current!.RequestedThemeVariant;
            var (_, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var banner = window.FindControl<Border>("TechDemoNotice")!;
                foreach (var theme in Themes)
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    PageScreenshots.Settle(window);
                    if (!banner.IsEffectivelyVisible) failures.Add($"{theme}: the notice is not visible");
                    var message = banner.GetVisualDescendants().OfType<TextBlock>()
                        .First(candidate => candidate.Text == TechDemoNoticeViewModel.NoticeText);
                    var messageRatio = Effective(message);
                    if (messageRatio < Text) failures.Add($"{theme} notice text: {messageRatio:F2}:1");
                    foreach (var name in new[] { "Acknowledge the tech demo notice" })
                    {
                        var button = banner.GetLogicalDescendants().OfType<Button>()
                            .Single(candidate => AutomationProperties.GetName(candidate) == name);
                        if (!button.IsEffectivelyEnabled) failures.Add($"{theme} '{name}' is disabled");
                        if (!button.IsEffectivelyVisible) failures.Add($"{theme} '{name}' is not visible");
                        var text = button.GetVisualDescendants().OfType<TextBlock>().First();
                        var ratio = Effective(text);
                        if (ratio < Text) failures.Add($"{theme} '{name}': {ratio:F2}:1");
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = priorTheme;
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryMatrixCellsTextIsReadableEvenWhenItsCountIsZero()
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var compare = MatrixListsWindowWordsTests.Compare(MatrixListsWindowWordsTests.EveryKindOfWord);
                var window = new MotifTestWindow
                {
                    Content = new ComparePanel(compare), RequestedThemeVariant = theme, Width = 1400, Height = 900,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var cells = window.GetVisualDescendants().OfType<MatrixCell>().Where(cell => cell.IsEffectivelyVisible).ToArray();
                    Assert.Contains(cells, cell => cell.DataContext is CompareCellViewModel { IsEmpty: true });
                    Assert.Contains(cells, cell => cell.DataContext is CompareCellViewModel { IsNone: true });
                    Assert.Contains(cells, cell => cell.DataContext is CompareCellViewModel { IsGood: true, IsEmpty: false });
                    foreach (var cell in cells)
                    foreach (var text in cell.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible))
                    {
                        var ratio = Effective(text);
                        if (ratio < Text)
                            failures.Add($"{theme} {((CompareCellViewModel)cell.DataContext!).AccessibleName} '{text.Text}': {ratio:F2}:1");
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        });

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TimingsChartHasItsOwnColoursEachVisibleOnItsCard()
    {
        var failures = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var priorTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            try
            {
                workspace.CurrentPage = WorkspacePage.Timing;
                foreach (var theme in Themes)
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    PageScreenshots.Settle(window);
                    var bar = Assert.Single(window.GetVisualDescendants().OfType<TimingKindBar>(),
                        candidate => candidate.IsEffectivelyVisible);
                    Assert.True(bar.IsEffectivelyVisible);
                    var brushes = KindBrushes(bar);
                    var parts = brushes.Select(brush => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color).ToArray();
                    var reserved = new[] { Resolve("Intent.Primary", theme), Resolve("Intent.Link", theme) };
                    if (parts.Distinct().Count() != parts.Length) failures.Add($"{theme}: two parts share a colour");
                    if (parts.Intersect(reserved).Any()) failures.Add($"{theme}: a part is Intent.Primary or Intent.Link");
                    var card = Backdrop(bar);
                    for (var index = 0; index < parts.Length; index++)
                    {
                        var ratio = Ratio(Painted(bar, Layer(brushes[index])), card);
                        if (ratio < Part) failures.Add($"{theme} part {index + 1} {parts[index]}: {ratio:F2}:1 on {card}");
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = priorTheme;
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TimingsLegendShowsEachKindBesideItsPartsColourInARowThatWraps()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            try
            {
                // The width the page review and PR-06 drew.
                window.Width = 1240;
                window.Height = 780;
                workspace.CurrentPage = WorkspacePage.Timing;
                PageScreenshots.Settle(window);
                var bar = Assert.Single(window.GetVisualDescendants().OfType<TimingKindBar>(),
                    candidate => candidate.IsEffectivelyVisible);
                var legend = window.GetLogicalDescendants().OfType<ItemsControl>()
                    .Single(control => AutomationProperties.GetName(control) == "Timing by kind");
                var swatches = legend.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("swatch")).Select(border => border.Background).ToArray();

                Assert.Equal(bar.Rows!.Count, swatches.Length);
                var rows = legend.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("swatch"))
                    .Select(border => Math.Round(border.TranslatePoint(default, legend)!.Value.Y)).Distinct().Count();
                Assert.True(rows < swatches.Length, $"the legend is a list of {rows} rows, not a row that wraps");
                for (var index = 0; index < swatches.Length; index++)
                    Assert.Same(bar.BrushFor(bar.Rows[index].Kind), swatches[index]);
                Assert.Equal(swatches.Length, swatches.Distinct().Count());
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("morph_rule", "Intent.Timing.MorphRule")]
    [InlineData("phon_rule", "Intent.Timing.PhonRule")]
    [InlineData("lex_entry", "Intent.Timing.Lexicon")]
    [InlineData("root_index", "Intent.Timing.RootLookup")]
    [InlineData("anything else", "Intent.Timing.Unattributed")]
    public void ATimingKindKeepsItsOwnColourWhereverItFallsInTheBar(string kind, string key)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var bar = new TimingKindBar();
                var window = new MotifTestWindow { Content = bar, RequestedThemeVariant = theme, Width = 400, Height = 100 };
                try
                {
                    window.Show();
                    foreach (var order in new[] { new[] { kind, "other" }, new[] { "other", kind } })
                    {
                        bar.Rows = [.. order.Select(name => new TimingShare(name, name, 1, 0.5, 1,
                            new SIL.Motif.Contract.Responses.TimingAggregateRow(name, name, 1, 0.5, 1) { Kind = name }))];
                        window.UpdateLayout();
                        var entry = Assert.Single(bar.Legend, entry => entry.Row.Kind == kind);
                        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(entry.Brush);
                        Assert.Equal(Resolve(key, theme), brush.Color);
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    private static IBrush?[] KindBrushes(TimingKindBar bar) =>
        [bar.MorphRuleBrush, bar.PhonRuleBrush, bar.LexiconBrush, bar.RootLookupBrush, bar.UnattributedBrush];

    // Each owner restyles the text inside it, and a tooltip's text is its owner's logical descendant.
    public static TheoryData<string> TooltipOwners() => new() { "plain", "stagedStrip", "markChip", "ruleRow", "handoffQuestion" };

    [Theory]
    [MemberData(nameof(TooltipOwners))]
    public void ATooltipIsReadableWhateverItsOwnerStylesInBothThemes(string owner)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                Control control = owner switch
                {
                    "stagedStrip" => new Border { Classes = { "stagedStrip" }, Child = new TextBlock { Text = "Staged" } },
                    "markChip" => new MarkChip { Mark = Mark.Of(MeaningTone.Problem), Text = "Built anyway", Compact = true },
                    "ruleRow" => new Button { Classes = { "ruleRow" }, Content = "lu" },
                    "handoffQuestion" => new Button { Classes = { "handoffQuestion" }, Content = "Which words did not parse?" },
                    _ => new Button { Content = "Refresh" },
                };
                ToolTip.SetTip(control, "Still fits the project.");
                var window = new MotifTestWindow { Content = control, RequestedThemeVariant = theme, Width = 400, Height = 200 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var text = OpenTip(control);
                    var ratio = Effective(text);
                    Assert.True(ratio >= Text, $"{theme} tooltip on {owner}: {ratio:F2}:1, needs {Text}:1");
                    Assert.True(Application.Current!.TryGetResource("Intent.Type.Small", theme, out var size));
                    Assert.Equal(size, text.FontSize);
                }
                finally
                {
                    ToolTip.SetIsOpen(control, false);
                    window.Close();
                }
            }
        });
    }

    /// <summary>Opens <paramref name="owner"/>'s tooltip and returns the text block that shows its words.</summary>
    internal static TextBlock OpenTip(Control owner)
    {
        ToolTip.SetIsOpen(owner, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var property = (AvaloniaProperty)typeof(ToolTip)
            .GetField("ToolTipProperty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var tip = Assert.IsType<ToolTip>(owner.GetValue(property));
        // The tip fades in; it is measured as it settles, fully opaque.
        tip.Transitions = null;
        tip.Opacity = 1;
        tip.UpdateLayout();
        return tip.GetVisualDescendants().OfType<TextBlock>().First(block => !string.IsNullOrEmpty(block.Text));
    }

    // A notice's label colour is for live buttons; a disabled one must still look like every other disabled button.
    [Fact]
    public void ADisabledButtonInANoticeStillLooksDisabled()
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var inNotice = new Button { Content = "Try again", IsEnabled = false };
                var elsewhere = new Button { Content = "Try again", IsEnabled = false };
                var panel = new StackPanel
                {
                    Children = { new Border { Classes = { "notice" }, Child = inNotice }, elsewhere },
                };
                var window = new MotifTestWindow { Content = panel, RequestedThemeVariant = theme, Width = 300, Height = 200 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Assert.Equal(LabelColour(elsewhere), LabelColour(inNotice));
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    // A disabled control's own label may be faint; its reason, in the tooltip and beside it, must still read.
    [Fact]
    public void DisabledReasonsRemainReadableAfterThemeChange()
    {
        var failures = new List<string>();
        var measured = 0;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
                [new("kept", "wordform/kept", "kept", "approve", "assessment/one", "reading", ["operation/kept"])],
                [new ChangeFit("kept", true, [])]));
            var context = DisabledReasonContext(fake);
            var review = new ReviewPageModel(context);
            await context.OpenProjectAsync(@"C:\projects\reasons.fwdata");
            var compare = new CompareViewModel();
            compare.Load([new AssessWordRowViewModel(
                new AssessmentWordResult("hawajafika", "no-analysis", false, "Search completed", 1, null)
                {
                    ProjectStanding = ProjectStanding.Approved,
                    OccurrenceCount = 1,
                })]);
            var lists = new TextsListsViewModel(compare);

            var prior = Application.Current!.RequestedThemeVariant;
            var reviewWindow = new MotifTestWindow
            {
                Content = SIL.Motif.App.Views.Shell.WorkspaceLayoutHost.ForPage(new ReviewPanel(review)),
                Width = 1100, Height = 800,
            };
            var listsWindow = new MotifTestWindow { Content = new TextsListsPanel(lists), Width = 1100, Height = 800 };
            try
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                reviewWindow.Show();
                listsWindow.Show();
                foreach (var (state, list) in new[] { ("ticked none", "Lost"), ("empty list", "Nobody can analyze") })
                {
                    lists.SelectListCommand.Execute(lists.Lists.Single(candidate => candidate.Name == list));
                    foreach (var theme in Themes)
                    {
                        Application.Current.RequestedThemeVariant = theme;
                        PageScreenshots.Settle(listsWindow);
                        PageScreenshots.Settle(reviewWindow);
                        var apply = Disabled(reviewWindow, "Apply to FieldWorks project", failures);
                        var ticked = DisabledByAutomationId(listsWindow,
                            SIL.Motif.App.AutomationIds.HandOffCheckedWords, failures);
                        var whole = lists.HandOffListUnavailable
                            ? DisabledByAutomationId(listsWindow, SIL.Motif.App.AutomationIds.HandOffList, failures) : null;
                        var owners = new[] { apply, ticked, whole }.OfType<Button>().ToList();
                        var reasons = new List<(string What, TextBlock Text)>();
                        foreach (var owner in owners)
                            reasons.AddRange(ControlContracts.TooltipScenes.OpenTipTexts(owner)
                                .Select(text => ($"the tooltip on {AutomationProperties.GetName(owner)}", text)));
                        reasons.AddRange(Shown(listsWindow, lists.HandOffCheckedWordsDisabledReason, lists.HandOffListDisabledReason)
                            .Select(text => ("the reason under the AI Handoff buttons", text)));
                        reasons.AddRange(Shown(reviewWindow, [review.ApplyBlockedTitle, review.ApplyDisabledReason ?? string.Empty])
                            .Select(text => ("the reasons Apply is blocked", text)));
                        if (reasons.Count < 3) failures.Add($"{theme} {state}: only {reasons.Count} reasons showed");
                        foreach (var (what, text) in reasons)
                        {
                            var ratio = Effective(text);
                            measured++;
                            if (ratio < Text) failures.Add($"{theme} {state}: {what} '{text.Text}' is {ratio:F2}:1, needs {Text}:1");
                        }
                        foreach (var owner in owners) ToolTip.SetIsOpen(owner, false);
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = prior;
                reviewWindow.Close();
                listsWindow.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(measured > 0, "no reason was measured");
    }

    private static Button? Disabled(Window window, string name, List<string> failures)
    {
        var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(candidate => AutomationProperties.GetName(candidate) == name);
        if (button is null) failures.Add($"'{name}' is not shown");
        else if (button.IsEffectivelyEnabled) failures.Add($"'{name}' is enabled, so it shows no reason");
        else if (ToolTip.GetTip(button) is not string { Length: > 0 }) failures.Add($"'{name}' is disabled with no reason in its tooltip");
        return button is { IsEffectivelyEnabled: false } ? button : null;
    }

    private static Button? DisabledByAutomationId(Window window, string automationId, List<string> failures)
    {
        var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(candidate =>
            AutomationProperties.GetAutomationId(candidate) == automationId);
        if (button is null) failures.Add($"'{automationId}' is not shown");
        else if (button.IsEffectivelyEnabled) failures.Add($"'{automationId}' is enabled, so it shows no reason");
        else if (ToolTip.GetTip(button) is not string { Length: > 0 })
            failures.Add($"'{automationId}' is disabled with no reason in its tooltip");
        return button is { IsEffectivelyEnabled: false } ? button : null;
    }

    private static IEnumerable<TextBlock> Shown(Window window, params string[] sentences) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) && sentences.Contains(text.Text) &&
                text.FindAncestorOfType<ToolTip>() is null);

    private static WorkspaceContext DisabledReasonContext(FakeCommandClient fake)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake), fake,
            new NoFolder(), new NoDrag(), new BaselineViewModel(fake));
    }

    private sealed class NoFolder : SIL.Motif.App.Services.IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoDrag : SIL.Motif.App.Services.IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(Avalonia.Input.PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, Avalonia.Input.DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }

    private static Color LabelColour(Button button) => Assert.IsAssignableFrom<ISolidColorBrush>(
        button.GetVisualDescendants().OfType<TextBlock>().First().Foreground).Color;

    private static void AssertReadableIn(Control content, ThemeVariant theme, string what)
    {
        var window = new MotifTestWindow { Content = content, RequestedThemeVariant = theme, Width = 300, Height = 100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var text = content.GetVisualDescendants().OfType<TextBlock>().First();
            var ratio = Effective(text);
            Assert.True(ratio >= Text, $"{what} '{text.Text}': {ratio:F2}:1, needs {Text}:1");
        }
        finally
        {
            window.Close();
        }
    }

    // Light values Motif and FieldWorks' FwAvaloniaTheme share, written out here as FieldWorks holds them.
    public static TheoryData<string, object> SharedLightValues() => new()
    {
        { "Intent.Space.Compact", 4.0 },                       // DialogLabelFieldGap, Semi ExtraTight
        { "Intent.Space.Related", 6.0 },                       // DialogControlGap
        { "Intent.Space.Group", 8.0 },                         // DialogButtonStripGap, Semi Tight
        { "Intent.Gap.BeforeGroup", new Thickness(0, 8, 0, 0) },   // DialogGroupSeparation
        { "Intent.Gap.BeforeRelated", new Thickness(0, 6, 0, 0) }, // DialogControlGapAbove, DialogLabelGroupGapAbove
        { "Intent.Gap.AfterRelated", new Thickness(0, 0, 6, 0) },  // DataTree.TrailingGap
        { "Intent.Inset.Chip", new Thickness(6, 2, 6, 2) },        // DataTree.OptionItemPadding
        { "Intent.Stroke.Box", new Thickness(1) },                 // DialogFieldBorderThickness, DataTree.HairlineBorderThickness
        { "Intent.Stroke.DividerTop", new Thickness(0, 1, 0, 0) }, // DataTree.TopHairlineBorderThickness
        { "Intent.Stroke.DividerBottom", new Thickness(0, 0, 0, 1) }, // DataTree.BottomHairlineBorderThickness
        { "Intent.Radius.Control", new CornerRadius(3) },          // DataTree.PickerCornerRadius, Semi Small
    };

    [Theory]
    [MemberData(nameof(SharedLightValues))]
    public void AValueMotifSharesWithFieldWorksStaysAtFieldWorksValue(string key, object expected)
    {
        avalonia.Invoke(() =>
        {
            Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out var value), $"{key} does not resolve");
            Assert.Equal(expected, value);
        });
    }

    [Fact]
    public void ANoFillIsFullyTransparentAsInFieldWorks()
    {
        avalonia.Invoke(() =>
        {
            Assert.True(Application.Current!.TryGetResource("Intent.Clear", ThemeVariant.Light, out var value));
            var clear = Assert.IsAssignableFrom<ISolidColorBrush>(value);
            Assert.Equal(0, clear.Color.A); // FwTransparentBrush
        });
    }

    [Theory]
    [InlineData("Intent.Selected.Fill", "#D7E6FF")] // FwSelectedRowBrush
    [InlineData("Intent.Marking.Border", "#7A7A7A")] // FwDialogFieldBorderBrush
    [InlineData("Intent.Marking.Divider", "#D3D3D3")] // LightGray, FwSliceRuleBrush
    public void AColourMotifAdoptedFromFieldWorksStaysAtItsValue(string key, string hex)
    {
        avalonia.Invoke(() => Assert.Equal(Color.Parse(hex), Resolve(key, ThemeVariant.Light)));
    }

    [Fact]
    public void TheAdoptedFieldWorksColoursReadOnMotifsLightSurfaces()
    {
        avalonia.Invoke(() =>
        {
            var selected = Resolve("Intent.Selected.Fill", ThemeVariant.Light);
            foreach (var ink in new[] { "Intent.Accent", "Intent.Text", "Intent.TextSecondary" })
                Assert.True(Ratio(Resolve(ink, ThemeVariant.Light), selected) >= Text, $"{ink} on Intent.Selected.Fill");
            foreach (var surface in new[] { "Intent.Surface", "Intent.Surface.Subtle" })
                Assert.True(Ratio(Resolve("Intent.Marking.Border", ThemeVariant.Light), Resolve(surface, ThemeVariant.Light)) >= Part,
                    $"Intent.Marking.Border on {surface}");
        });
    }

    // FwLabelBrush (#696969) is not adopted: on FieldWorks' selected row it is 4.35:1, under the 4.5:1 text needs.
    [Fact]
    public void FieldWorksLabelInkStillFailsOnTheSelectedRow()
    {
        avalonia.Invoke(() => Assert.True(Ratio(Color.Parse("#696969"), Resolve("Intent.Selected.Fill", ThemeVariant.Light)) < Text));
    }

    // Known gap: muted text on a word card header misses AA on the #D7E6FF fill.
    [Fact]
    public void MutedTextOnTheWordCardHeaderStillFallsShortOfAa()
    {
        avalonia.Invoke(() =>
        {
            var text = new TextBlock { Text = "Esc closes", Classes = { "muted" } };
            var window = new MotifTestWindow
            {
                Content = new Border { Classes = { "wordPresentationCardHeader" }, Child = text },
                RequestedThemeVariant = ThemeVariant.Light, Width = 300, Height = 100,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var ratio = Effective(text);
                Assert.True(ratio is >= Part and < Text, $"muted on the card header: {ratio:F2}:1; if this now passes, drop the follow-up in the map");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Color Resolve(string key, ThemeVariant theme)
    {
        Assert.True(Application.Current!.TryGetResource(key, theme, out var value), $"{key} does not resolve in {theme}");
        return value switch
        {
            ISolidColorBrush brush => brush.Color,
            Color color => color,
            _ => throw new InvalidOperationException($"{key} is {value?.GetType().Name}"),
        };
    }

    /// <summary>The ratio between a text's painted colour and the colour painted behind it.</summary>
    internal static double Effective(TextBlock text)
    {
        return Effective((Visual)text);
    }

    private static double Effective(Visual text)
    {
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(text.GetValue(TextBlock.ForegroundProperty));
        return Ratio(Painted(text, Layer(foreground)), Painted(text, default));
    }

    /// <summary>What an element's ancestors paint beneath it.</summary>
    private static Color Backdrop(Visual visual) => Painted(visual, default);

    // Each ancestor, nearest first, lays the paint over its background, then fades the group by its opacity.
    private static Color Painted(Visual visual, Premultiplied own)
    {
        var paint = Premultiplied.Over(own, Layer(BackgroundOf(visual))).Scale(visual.Opacity);
        foreach (var ancestor in visual.GetVisualAncestors().OfType<Visual>())
            paint = Premultiplied.Over(paint, Layer(BackgroundOf(ancestor))).Scale(ancestor.Opacity);
        var window = TopLevel.GetTopLevel(visual)?.ActualThemeVariant == ThemeVariant.Dark ? Colors.Black : Colors.White;
        var flat = Premultiplied.Over(paint, new Premultiplied(window.R / 255d, window.G / 255d, window.B / 255d, 1));
        return Color.FromRgb((byte)Math.Round(flat.R * 255), (byte)Math.Round(flat.G * 255), (byte)Math.Round(flat.B * 255));
    }

    private static IBrush? BackgroundOf(Visual visual) => visual switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        ContentPresenter presenter => presenter.Background,
        TextBlock block => block.Background,
        _ => null,
    };

    private static Premultiplied Layer(IBrush? brush)
    {
        if (brush is not ISolidColorBrush solid) return default;
        var alpha = solid.Color.A / 255d * solid.Opacity;
        return new Premultiplied(solid.Color.R / 255d * alpha, solid.Color.G / 255d * alpha, solid.Color.B / 255d * alpha, alpha);
    }

    private static double Ratio(Color first, Color second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte value)
    {
        var channel = value / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private readonly record struct Premultiplied(double R, double G, double B, double A)
    {
        public Premultiplied Scale(double opacity) => new(R * opacity, G * opacity, B * opacity, A * opacity);

        public static Premultiplied Over(Premultiplied top, Premultiplied beneath) => new(
            top.R + beneath.R * (1 - top.A), top.G + beneath.G * (1 - top.A),
            top.B + beneath.B * (1 - top.A), top.A + beneath.A * (1 - top.A));
    }
}
