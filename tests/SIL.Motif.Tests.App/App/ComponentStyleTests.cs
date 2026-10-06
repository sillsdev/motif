using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.ControlContracts;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Applies every component style file to controls wearing its classes, in the light and the dark theme, and
/// checks each chosen property took the token's value. A misspelled selector matches nothing and raises no
/// error, so only applying the styles shows that a component is styled at all.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class ComponentStyleTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public ComponentStyleTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    private sealed record Case(string Component, string What, Func<Panel, Control> Build, AvaloniaProperty Property, string Key);

    [Fact]
    public void EveryComponentStyleFileHasACaseHere()
    {
        var directory = System.IO.Path.Combine(AppDirectory(), "Tokens", "Components");
        var styled = Directory.GetFiles(directory, "*.axaml")
            .Where(file => File.ReadAllText(file).Contains("<Style ", StringComparison.Ordinal))
            .Select(file => System.IO.Path.GetFileNameWithoutExtension(file)!)
            .Order()
            .ToList();
        Assert.Equal(styled, Cases().Select(item => item.Component).Distinct().Order().ToList());
    }

    [Fact]
    public void EveryComponentStyleSetsItsTokensInBothThemeVariants()
    {
        var failures = new List<string>();
        _avalonia.Invoke(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var item in Cases())
            {
                var host = new StackPanel();
                var window = new Window { Content = host, RequestedThemeVariant = variant, Width = 400, Height = 300 };
                try
                {
                    var target = item.Build(host);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.True(Application.Current!.TryGetResource(item.Key, variant, out var expected),
                        $"{variant} {item.Component}: {item.What} cannot resolve {item.Key}.");
                    var actual = target.GetValue(item.Property);
                    if (!Equals(expected, actual))
                        failures.Add($"{variant} {item.Component}: {item.What} {item.Property.Name} is {actual}, not {item.Key} ({expected}).");
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
    public void ActionChipsAndOutcomeLegendsFitTheirText()
    {
        _avalonia.Invoke(() =>
        {
            var chip = Press("actionChip", "primary");
            chip.Content = "✓ Approve";
            var bar = new OutcomeBar
            {
                ShowShares = false,
                Segments = [new SIL.Motif.App.ViewModels.OutcomeSegment(SIL.Motif.App.ViewModels.Mark.NoParse, 1, "none")],
            };
            var window = new Window
            {
                Content = new StackPanel { Children = { chip, bar } },
                Width = 300,
                Height = 100,
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var actionText = chip.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "✓ Approve");
                var outcomeGlyph = bar.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "∅");

                LayoutAssertions.AssertCurrent(window);
                Assert.True(chip.Bounds.Height >= actionText.Bounds.Height);
                Assert.True(outcomeGlyph.Bounds.Height <= Assert.Single(
                    outcomeGlyph.GetVisualAncestors().OfType<MarkGlyph>()).Bounds.Height);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ActionChipUsesItsHoverSurface()
    {
        _avalonia.Invoke(() =>
        {
            var chip = Press("actionChip");
            var window = new Window { Content = chip, Width = 120, Height = 40 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                window.MouseMove(CentreOf(chip, window));
                Dispatcher.UIThread.RunJobs();
                Assert.True(Application.Current!.TryGetResource("Intent.Marking.Hover", ThemeVariant.Light, out var expected));
                var face = Assert.IsType<Avalonia.Controls.Presenters.ContentPresenter>(
                    ComponentStateContractCases.PartOf(chip, StatePart.Face));
                Assert.Equal(expected, face.Background);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AHiddenRevealStaysVisibleToKeyboardAndAutomation()
    {
        _avalonia.Invoke(() =>
        {
            var button = Press("revealControl", "revealButton", "revealOnHover");
            AutomationProperties.SetName(button, "Open the word");
            var owner = new Border { Classes = { "hoverReveal" }, Background = Brushes.Transparent, Child = button };
            var window = new Window { Content = owner, Width = 200, Height = 40 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Equal(0, button.Opacity);
                Assert.True(button.IsVisible);
                Assert.True(button.IsTabStop);
                Assert.Equal("Open the word", AutomationProperties.GetName(button));
                Assert.True(button.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();

                Assert.True(owner.IsKeyboardFocusWithin);
                Assert.Equal(1, button.Opacity);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ExpanderChevronStaysInsideItsHeaderColumn()
    {
        _avalonia.Invoke(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var expander = new Expander { Header = "Details", IsExpanded = true };
                var window = new Window { Content = expander, RequestedThemeVariant = variant, Width = 400, Height = 300 };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.True(ExpanderChevronAlignment.GetIsEnabled(expander));
                    var chevron = expander.GetVisualDescendants().OfType<PathIcon>()
                        .Single(icon => icon.Name == "PART_PathIcon");
                    var headerGrid = Assert.IsType<Grid>(chevron.Parent);
                    Assert.Equal(headerGrid.ColumnDefinitions.Count - 1, Grid.GetColumn(chevron));
                    Assert.Equal(HorizontalAlignment.Left, chevron.HorizontalAlignment);
                    Assert.True(Application.Current!.TryGetResource(
                        "Component.Interaction.ExpanderChevronMargin", variant, out var expected));
                    Assert.Equal(expected, chevron.Margin);
                    LayoutAssertions.AssertCurrent(window);
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void AnUnavailableFilterChipStillLooksLikeAChip()
    {
        var failures = new List<string>();
        _avalonia.Invoke(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var chip = Press("filterChip");
                chip.IsEnabled = false;
                var window = new Window { Content = chip, RequestedThemeVariant = variant, Width = 120, Height = 40 };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var face = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(chip).OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                        .First(part => part.Name == "PART_ContentPresenter");
                    Assert.True(Application.Current!.TryGetResource("Intent.Border", variant, out var border));
                    Assert.True(Application.Current.TryGetResource("Intent.Stroke.Box", variant, out var stroke));
                    Assert.True(Application.Current.TryGetResource("Intent.Clear", variant, out var clear));
                    Assert.True(Application.Current.TryGetResource("Intent.TextMuted", variant, out var muted));
                    if (!Equals(border, face.BorderBrush)) failures.Add($"{variant}: border is {face.BorderBrush}, not Intent.Border.");
                    if (!Equals(stroke, face.BorderThickness)) failures.Add($"{variant}: stroke is {face.BorderThickness}, not Intent.Stroke.Box.");
                    if (!Equals(clear, face.Background)) failures.Add($"{variant}: fill is {face.Background}, not Intent.Clear.");
                    if (!Equals(muted, face.Foreground)) failures.Add($"{variant}: text is {face.Foreground}, not Intent.TextMuted.");
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
    public void PanGlossDifferentAndConflictLinesUseDistinctEmphasis()
    {
        _avalonia.Invoke(() =>
        {
            var differentForm = Text("f");
            var differentDetail = Text("detail");
            var differentContent = new StackPanel();
            differentContent.Children.Add(differentForm);
            differentContent.Children.Add(differentDetail);
            var different = new Border
            {
                Classes = { "panGlossLine", "different" },
                Child = differentContent,
            };

            var conflictForm = Text("f");
            var conflictDetail = Text("detail");
            var conflictContent = new StackPanel();
            conflictContent.Children.Add(conflictForm);
            conflictContent.Children.Add(conflictDetail);
            var conflict = new Border
            {
                Classes = { "panGlossLine", "conflict" },
                Child = conflictContent,
            };

            var divider = new Rectangle();
            divider.Classes.Add("panGlossDivider");
            var capped = new Border
            {
                Classes = { "panGlossLine", "capped" },
                Child = Text("capped"),
            };
            var host = new StackPanel();
            host.Children.Add(different);
            host.Children.Add(conflict);
            host.Children.Add(capped);
            host.Children.Add(divider);
            var window = new Window { Content = host };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(new Thickness(0), different.BorderThickness);
                Assert.Equal(new Thickness(3, 0, 0, 0), different.Padding);
                Assert.Equal(0, different.BoxShadow.Count);
                Assert.Equal(FontWeight.Normal, differentForm.FontWeight);
                Assert.Equal(FontWeight.Normal, differentDetail.FontWeight);
                Assert.Equal(new Thickness(3, 0, 0, 0), conflict.Padding);
                Assert.Equal(1, conflict.BoxShadow.Count);
                Assert.True(conflict.BoxShadow[0].IsInset);
                Assert.Equal(Color.Parse("#215cc7"), conflict.BoxShadow[0].Color);
                Assert.Equal(FontWeight.SemiBold, conflictForm.FontWeight);
                Assert.Equal(FontWeight.Normal, conflictDetail.FontWeight);
                Assert.Equal(FontWeight.Normal, ((TextBlock)capped.Child!).FontWeight);
                Assert.Equal([1d, 2d], divider.StrokeDashArray);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // A state a person reaches by pointer, keyboard or availability, or a selection or opening the model sets.
    [GeneratedRegex(@":(pointerover|pressed|disabled|focus|focus-visible|focus-within|selected|checked|open|expanded)\b" +
        @"|\.(active|chosen|selected|open)\b")]
    private static partial Regex InteractionState();

    [Fact]
    public void EveryInteractionSelectorHasAnAuthoredStateCase()
    {
        var app = AppDirectory();
        var declared = Directory.GetFiles(System.IO.Path.Combine(app, "Tokens"), "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(System.IO.Path.Combine(app, "Views"), "*.axaml", SearchOption.AllDirectories))
            .Append(System.IO.Path.Combine(app, "App.axaml"))
            .SelectMany(file => SelectorAttribute().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .SelectMany(selector => selector.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(selector => InteractionState().IsMatch(selector))
            .ToHashSet(StringComparer.Ordinal);
        var authored = ComponentStateContractCases.All().Select(item => item.Selector)
            .Concat(ComponentStateContractCases.Pinned.Select(item => item.Selector))
            .Where(selector => InteractionState().IsMatch(selector))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);
        Assert.True(declared.SetEquals(authored),
            "States a style declares without a case: " + string.Join("; ", declared.Except(authored).Order()) + Environment.NewLine +
            "Cases for no declared state: " + string.Join("; ", authored.Except(declared).Order()));
    }

    [GeneratedRegex(@"<Style\s+Selector=""([^""]+)""")]
    private static partial Regex SelectorAttribute();

    [Fact]
    public void EveryAuthoredComponentStateUsesItsIntentTokenInBothThemes()
    {
        var failures = new List<string>();
        var gapsSeen = new HashSet<string>(StringComparer.Ordinal);
        var gaps = ComponentStateContractCases.Gaps.Select(gap => gap.Case).ToHashSet(StringComparer.Ordinal);
        var reachedCases = 0;
        _avalonia.Invoke(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var item in ComponentStateContractCases.All())
            {
                var (content, target) = item.Build();
                var window = new Window { Content = content, RequestedThemeVariant = variant, Width = 400, Height = 300 };
                try
                {
                    if (item.Stimulus.HasFlag(StateStimulus.Disabled)) target.IsEnabled = false;
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    if (Reach(item, content, target, window) is { } missed)
                    {
                        failures.Add($"{variant} {item}: {missed}");
                        continue;
                    }
                    reachedCases++;
                    if (ComponentStateContractCases.PartOf(target, item.Part) is not { } part)
                    {
                        failures.Add($"{variant} {item}: the control shows no {item.Part}");
                        continue;
                    }
                    Assert.True(Application.Current!.TryGetResource(item.Key, variant, out var expected), $"{item.Key} does not resolve");
                    var actual = part.GetValue(item.Property);
                    if (SamePaint(expected, actual)) continue;
                    if (gaps.Contains(item.ToString())) gapsSeen.Add(item.ToString());
                    else failures.Add($"{variant} {item}: is {Describe(actual)}, not {item.Key} ({Describe(expected)})");
                }
                finally
                {
                    window.Close();
                }
            }
        });
        failures.AddRange(gaps.Except(gapsSeen).Select(gap => $"{gap}: the reported gap is fixed, so remove it from the gaps"));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(2 * ComponentStateContractCases.All().Count(), reachedCases);
    }

    // Drives the state by real input and confirms it landed, so a missed stimulus fails instead of reading rest.
    private static string? Reach(ComponentStateCase item, Control content, Control target, Window window)
    {
        if (item.Stimulus.HasFlag(StateStimulus.Disabled) && target.IsEffectivelyEnabled) return "the control is still enabled";
        if (item.Stimulus.HasFlag(StateStimulus.KeyboardFocus) && !target.Focus(NavigationMethod.Tab))
            return "the control took no keyboard focus";
        if (item.Stimulus.HasFlag(StateStimulus.KeyboardFocusInside))
        {
            if (!target.Focus(NavigationMethod.Tab)) return "the control inside took no keyboard focus";
            if (!content.IsKeyboardFocusWithin) return "focus is not within the owner";
        }
        if (item.Stimulus.HasFlag(StateStimulus.Pointer) || item.Stimulus.HasFlag(StateStimulus.Press))
        {
            window.MouseMove(CentreOf(target, window));
            Dispatcher.UIThread.RunJobs();
            if (!content.IsPointerOver) return "the pointer is not over the control";
            if (target.IsHitTestVisible && !target.IsPointerOver) return "the pointer is not over the target";
        }
        if (item.Stimulus.HasFlag(StateStimulus.Press))
        {
            window.MouseDown(CentreOf(target, window), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (target is Button { IsPressed: false }) return "the press did not land";
        }
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return null;
    }

    // Semi and an Intent alias may hand out two brushes for one paint, so brushes compare by what they paint.
    private static bool SamePaint(object? expected, object? actual) => (expected, actual) switch
    {
        (ISolidColorBrush want, ISolidColorBrush got) => want.Color == got.Color && want.Opacity.Equals(got.Opacity),
        _ => Equals(expected, actual),
    };

    private static string Describe(object? value) =>
        value is ISolidColorBrush brush ? $"{brush.Color} at {brush.Opacity:0.##}" : value?.ToString() ?? "unset";

    private static IEnumerable<Case> Cases()
    {
        yield return new("Text", "a muted line", host => Add(host, Text("muted")), TextBlock.ForegroundProperty, "Intent.TextMuted");
        yield return new("Text", "an error", host => Add(host, Text("error")), TextBlock.ForegroundProperty, "Intent.Danger");

        yield return new("Card", "a card", host => Add(host, Box("card")), Border.PaddingProperty, "Component.Card.Padding");
        yield return new("Card", "a card", host => Add(host, Box("card")), Border.CornerRadiusProperty, "Component.Card.Radius");
        yield return new("Card", "a card", host => Add(host, Box("card")), Border.BorderBrushProperty, "Intent.Border");

        yield return new("Notice", "a notice", host => Add(host, Box("notice")), Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("Notice", "a notice", host => Add(host, Box("notice")), Border.PaddingProperty, "Component.Notice.Padding");
        yield return new("Refusal", "a refusal", host => Add(host, Stack("refusal")), StackPanel.SpacingProperty, "Intent.Space.Minimal");
        yield return new("Refusal", "a refusal's actions", host => Add(host, Stack("refusalActions")),
            StackPanel.SpacingProperty, "Intent.Space.Related");
        yield return new("Refusal", "a refusal's details", host => Add(host, Text("refusalDetails")),
            TextBlock.ForegroundProperty, "Intent.TextMuted");
        yield return new("Refusal", "scrollable refusal content", host => Add(host,
                new ScrollViewer { Classes = { "refusalViewport" }, Content = Text("refusalDetails") }),
            ScrollViewer.MaxHeightProperty, "Component.Refusal.ContentMaxHeight");

        yield return new("ToolTip", "a tooltip's maximum width", host => Add(host, new ToolTip()),
            Control.MaxWidthProperty, "Component.ToolTip.MaxWidth");

        yield return new("MarkChip", "a chip", host => Add(host, Box("markChip")), Border.PaddingProperty, "Component.MarkChip.Padding");
        yield return new("MarkChip", "a chip", host => Add(host, Box("markChip")), Border.CornerRadiusProperty, "Component.MarkChip.Radius");
        yield return new("MarkChip", "a chip", host => Add(host, Box("markChip")), Border.BackgroundProperty, "Intent.Surface.Neutral");
        yield return new("MarkChip", "a chip without a mark's words", host => Within(host, Box("markChip", "plain"), "markWord"),
            TextBlock.ForegroundProperty, "Intent.TextMuted");
        yield return new("MarkChip", "an outcome chip's words", host => Within(host, Box("markChip", "outcome", "stopped"),
            "markWord", "outcome", "outcomeMark", "stopped"), TextBlock.ForegroundProperty, "Intent.Outcome.Stopped");
        yield return new("MarkChip", "an outcome chip, which stays clear", host => Add(host, Box("markChip", "outcome", "noParse")),
            Border.BackgroundProperty, "Intent.Clear");
        foreach (var (opinion, key) in new[]
        {
            ("approved", "Approved.Text"), ("disapproved", "Disapproved.Text"), ("unknown", "Unknown.Text"), ("none", "None.Text"),
        })
            yield return new("MarkChip", $"an {opinion} opinion chip's words", host => Within(host, Box("markChip", "opinion", opinion),
                "markWord", "opinion", "opinionMark", opinion), TextBlock.ForegroundProperty, $"Intent.Opinion.{key}");
        foreach (var (meaning, key) in new[] { ("fine", "Fine"), ("look", "Look"), ("problem", "Problem"), ("neutral", "Neutral") })
            yield return new("MarkChip", $"a {meaning} meaning chip's words", host => Within(host, Box("markChip", "meaning", meaning),
                "markWord", "meaning", "meaningMark", meaning), TextBlock.ForegroundProperty, $"Intent.Consequence.{key}");
        foreach (var (meaning, key) in new[] { ("look", "Look"), ("problem", "Problem") })
        {
            yield return new("MarkChip", $"a {meaning} meaning chip", host => Add(host, Box("markChip", "meaning", meaning)),
                Border.BackgroundProperty, $"Intent.Consequence.{key}.Fill");
            yield return new("MarkChip", $"a {meaning} meaning chip", host => Add(host, Box("markChip", "meaning", meaning)),
                Border.BorderBrushProperty, $"Intent.Consequence.{key}.Edge");
        }
        yield return new("MarkChip", "a fine meaning chip", host => Add(host, Box("markChip", "meaning", "fine")),
            Border.BackgroundProperty, "Intent.Clear");
        yield return new("MarkChip", "a warning chip", host => Add(host, Box("markChip", "severity", "warning")),
            Border.BackgroundProperty, "Intent.Severity.Warning.Fill");
        foreach (var (severity, key) in new[] { ("warning", "Warning"), ("error", "Error"), ("info", "Info") })
            yield return new("MarkChip", $"a {severity} chip's count", host => Within(host, Box("markChip", "severity", severity),
                "markWord", "severity", "severityMark", severity), TextBlock.ForegroundProperty, $"Intent.Severity.{key}");

        yield return new("OutcomeBar", "a legend link's composite content", host => Add(host,
                new StackPanel { Classes = { "outcomeLegendLabel" } }),
            Panel.BackgroundProperty, "Intent.Clear");
        yield return new("OutcomeBar", "an unknown segment", host => Add(host, Box("outcomeSegment")),
            Border.BackgroundProperty, "Intent.TextFaint");
        foreach (var (outcome, key) in new[]
        {
            ("same", "Same"), ("different", "Different"), ("noParse", "NoParse"), ("stopped", "Stopped"), ("notParsed", "NotParsed"),
        })
            yield return new("OutcomeBar", $"a {outcome} segment", host => Add(host, Box("outcomeSegment", "outcome", outcome)),
                Border.BackgroundProperty, $"Intent.Outcome.{key}");
        yield return new("OutcomeBar", "a fine segment", host => Add(host, Box("outcomeSegment", "meaning", "fine")),
            Border.BackgroundProperty, "Intent.Consequence.Fine");
        yield return new("OutcomeBar", "a have-a-look segment", host => Add(host, Box("outcomeSegment", "meaning", "look")),
            Border.BackgroundProperty, "Intent.Consequence.Look.Edge");
        yield return new("OutcomeBar", "a built-something-else segment", host => Add(host, Box("outcomeSegment", "meaning", "problem")),
            Border.BackgroundProperty, "Intent.Consequence.Problem.Edge");

        yield return new("WordVerdict", "a word", host => Add(host, Box("wordVerdict")), Border.BackgroundProperty, "Intent.Clear");
        yield return new("WordVerdict", "a word", host => Add(host, Box("wordVerdict")), Border.PaddingProperty, "Component.WordVerdict.Padding");
        yield return new("WordVerdict", "a dimmed word", host => Add(host, Box("wordVerdict", "dimmed")),
            Visual.OpacityProperty, "Intent.Opacity.Dimmed");
        yield return new("WordVerdict", "a changed uncertain word", host => Add(host, Box("wordVerdict", "uncertainChanged")),
            Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("WordVerdict", "a changed uncertain word", host => Add(host, Box("wordVerdict", "uncertainChanged")),
            Border.BorderBrushProperty, "Intent.Warning.Text");

        yield return new("FilterChip", "a chip", host => Add(host, Press("filterChip")), Button.BackgroundProperty, "Intent.Clear");
        yield return new("FilterChip", "a chip", host => Add(host, Press("filterChip")), Button.MarginProperty, "Intent.Gap.CompactItem");
        yield return new("FilterChip", "a compact chip", host => Add(host, Press("filterChip", "compact")),
            Button.PaddingProperty, "Component.FilterChip.CompactPadding");
        yield return new("FilterChip", "an active chip", host => Add(host, Press("filterChip", "active")),
            Button.ForegroundProperty, "Intent.Primary");

        yield return new("HeatCell", "a heat shade", host => Add(host, Box("heat")), Border.BackgroundProperty, "Intent.Warning");
        yield return new("MatrixCell", "a matrix cell", host => Add(host, Box("matrixCell")),
            Border.BackgroundProperty, "Intent.Surface");
        yield return new("MatrixCell", "a grid cell's inset", host => Add(host, Box("matrixCell")),
            Border.PaddingProperty, "Component.MatrixCell.CellPadding");
        yield return new("MatrixCell", "a cell's label", host => Within(host, Box("matrixCell"), "matrixCellLabel"),
            TextBlock.FontSizeProperty, "Intent.Type.Label");
        yield return new("MatrixCell", "a cell's count", host => Within(host, Box("matrixCell"), "matrixCellCount"),
            TextBlock.FontSizeProperty, "Intent.Type.Title");
        yield return new("MatrixCell", "an empty cell's count", host => Within(host, Box("matrixCell", "empty"), "matrixCellCount"),
            TextBlock.FontSizeProperty, "Intent.Type.Title");
        yield return new("MatrixCell", "the dash cell's count",
            host => Within(host, Box("matrixCell", "empty", "none"), "matrixCellCount"),
            TextBlock.FontSizeProperty, "Intent.Type.Title");
        yield return new("MatrixCell", "what the words share", host => Add(host, Box("matrixShared")),
            Border.BackgroundProperty, "Intent.Surface.Subtle");
        yield return new("MatrixCell", "the chosen cells' list", host => Add(host, Box("card", "matrixChosen")),
            Border.PaddingProperty, "Component.MatrixCell.Padding");
        yield return new("MatrixCell", "the chosen outcome glyph", MatrixChosenMark,
            TextBlock.FontSizeProperty, "Intent.Type.Body");
        yield return new("MatrixCell", "the chosen outcome icon's width", MatrixChosenIcon,
            PathIcon.WidthProperty, "Component.Mark.BodyGlyphSize");
        yield return new("MatrixCell", "the chosen outcome icon's height", MatrixChosenIcon,
            PathIcon.HeightProperty, "Component.Mark.BodyGlyphSize");
        yield return new("MatrixCell", "what the words share", host => Add(host, Box("matrixShared")),
            Border.PaddingProperty, "Component.MatrixCell.Padding");
        yield return new("MatrixCell", "how many share a morpheme", host => Within(host, Box("matrixShared"), "matrixSharedCount"),
            TextBlock.ForegroundProperty, "Intent.TextMuted");
        yield return new("MatrixCell", "a cell's words", host => Inside(host, Box("matrixCell")),
            TextBlock.ForegroundProperty, "Intent.Consequence.Neutral");
        yield return new("MatrixCell", "a kept cell", host => Add(host, Box("matrixCell", "good")),
            Border.BackgroundProperty, "Intent.Surface");
        yield return new("MatrixCell", "a kept cell's words", host => Inside(host, Box("matrixCell", "good")),
            TextBlock.ForegroundProperty, "Intent.Consequence.Fine");
        yield return new("MatrixCell", "a fine cell's words", host => Inside(host, Box("matrixCell", "fine")),
            TextBlock.ForegroundProperty, "Intent.Consequence.Fine");
        yield return new("MatrixCell", "a violation cell", host => Add(host, Box("matrixCell", "violation")),
            Border.BackgroundProperty, "Intent.Consequence.Problem.Fill");
        yield return new("MatrixCell", "a violation cell", host => Add(host, Box("matrixCell", "violation")),
            Border.BorderBrushProperty, "Intent.Consequence.Problem.Edge");
        yield return new("MatrixCell", "a violation cell's words", host => Inside(host, Box("matrixCell", "violation")),
            TextBlock.ForegroundProperty, "Intent.Consequence.Problem");
        yield return new("MatrixCell", "a have-a-look cell", host => Add(host, Box("matrixCell", "review")),
            Border.BackgroundProperty, "Intent.Consequence.Look.Fill");
        yield return new("MatrixCell", "a have-a-look cell", host => Add(host, Box("matrixCell", "review")),
            Border.BorderBrushProperty, "Intent.Consequence.Look.Edge");
        yield return new("MatrixCell", "a have-a-look cell's words", host => Inside(host, Box("matrixCell", "review")),
            TextBlock.ForegroundProperty, "Intent.Consequence.Look");
        yield return new("MatrixCell", "a new cell", host => Add(host, Box("matrixCell", "new")),
            Border.BackgroundProperty, "Intent.Consequence.Look.Fill");
        yield return new("MatrixCell", "a new cell's words", host => Inside(host, Box("matrixCell", "new")),
            TextBlock.ForegroundProperty, "Intent.Consequence.Look");
        yield return new("MatrixCell", "a compact matrix cell", host => Add(host, Box("matrixCell", "compact")),
            Border.WidthProperty, "Component.MatrixCell.CompactWidth");
        yield return new("MorphemeRow", "a form that is its own link",
            host => Add(host, With(new HyperlinkButton { Content = "kul" }, ["morphForm", "morphFormLink"])),
            HyperlinkButton.FontSizeProperty, "Intent.Type.Body");
        yield return new("MorphemeRow", "a form link's arrow", host => Add(host, Text("morphLinkMark")),
            TextBlock.FontSizeProperty, "Intent.Type.Label");
        yield return new("MorphemeRow", "a form link's words", host => Add(host, Stack("morphFormLinkWords")),
            StackPanel.SpacingProperty, "Intent.Space.Minimal");
        yield return new("MorphemeRow", "a morpheme edge", host => Add(host, Box("morphEdge")), Border.BorderBrushProperty, "Intent.Border");

        yield return new("DifferencePanel", "a move row", DenseListRow,
            ListBoxItem.PaddingProperty, "Component.DifferencePanel.MoveRowPadding");
        yield return new("DifferencePanel", "a move column heading", host => Add(host, Text("columnHeader")),
            TextBlock.ForegroundProperty, "Intent.TextMuted");

        yield return new("Handoff", "a step mark", host => Add(host, Box("stepMark")), Border.BackgroundProperty, "Intent.Emphasis.Fill");
        yield return new("Handoff", "a step mark", host => Add(host, Box("stepMark")), Border.MinWidthProperty, "Component.Handoff.StepMarkSize");
        yield return new("Handoff", "a file's kind", host => Inside(host, Box("handoffKind")), TextBlock.ForegroundProperty, "Intent.Emphasis.Text");
        yield return new("Handoff", "a written file", host => Add(host, Box("handoffFile")), Border.BorderThicknessProperty,
            "Component.Handoff.FileEdge");
        yield return new("Handoff", "the page header", host => Add(host, Box("handoffHeader")), Border.PaddingProperty,
            "Component.Handoff.HeaderPadding");

        yield return new("TopBar", "the top bar", host => Add(host, Box("topBar")), Border.MinHeightProperty, "Component.TopBar.Height");
        yield return new("TopBar", "the top bar", host => Add(host, Box("topBar")), Border.PaddingProperty, "Component.TopBar.Padding");
        yield return new("TopBar", "the project menu", host => Add(host, Press("projectMenu")),
            Button.MaxWidthProperty, "Component.TopBar.ProjectMenuMaxWidth");
        yield return new("TopBar", "the parse progress bar", host => Add(host, With(new ProgressBar(), ["topBarParseProgress"])),
            ProgressBar.WidthProperty, "Component.TopBar.ParseProgressWidth");
        yield return new("TopBar", "the parse progress bar", host => Add(host, With(new ProgressBar(), ["topBarParseProgress"])),
            ProgressBar.HeightProperty, "Component.TopBar.ParseProgressHeight");

        yield return new("Menu", "a menu", host => Add(host, Stack("menu")), StackPanel.WidthProperty, "Component.Menu.Width");
        yield return new("Menu", "a menu action", host => Add(host, Press("menuAction")), Button.MarginProperty, "Intent.Gap.CompactItem");
        yield return new("Menu", "a menu button", host => Add(host, Press("menuButton")), Button.MinHeightProperty, "Component.Menu.ButtonHeight");
        yield return new("Menu", "a menu entry", host => Add(host, Press("menuEntry")), Button.PaddingProperty, "Component.Menu.EntryPadding");
        yield return new("Menu", "a menu entry's detail", host => InsideButton(host, Press("menuEntry"), "menuDetail"),
            TextBlock.ForegroundProperty, "Intent.TextMuted");

        yield return new("Freshness", "the line", host => Add(host, With(new DockPanel(), ["freshness"])),
            DockPanel.HorizontalSpacingProperty, "Intent.Space.Snug");
        yield return new("Freshness", "a stale dot", host => Add(host, Dot("stale")), Shape.FillProperty, "Intent.Warning.Text");
        yield return new("Freshness", "a dot", host => Add(host, Dot()), Shape.WidthProperty, "Component.Freshness.DotSize");
        yield return new("Freshness", "a current label", host => Add(host, Text("freshLabel", "current")),
            TextBlock.ForegroundProperty, "Intent.Success.Text");
        yield return new("Freshness", "a refused label", host => Add(host, Text("freshLabel", "refused")),
            TextBlock.ForegroundProperty, "Intent.Danger");

        yield return new("Sidebar", "the sidebar", host => Add(host, Box("sidebar")), Border.BackgroundProperty, "Intent.Surface.Subtle");
        yield return new("Sidebar", "the sidebar", host => Add(host, Box("sidebar")), Border.WidthProperty, "Component.Sidebar.Width");
        yield return new("Sidebar", "the collapsed sidebar", host => Add(host, Box("sidebar", "collapsed")),
            Border.WidthProperty, "Component.Sidebar.RailWidth");
        yield return new("Sidebar", "an entry", host => Entry(host, collapsed: false, selected: false),
            ListBoxItem.PaddingProperty, "Component.Sidebar.ItemPadding");
        yield return new("Sidebar", "an entry", host => Entry(host, collapsed: false, selected: false),
            ListBoxItem.BackgroundProperty, "Intent.Clear");
        yield return new("Sidebar", "an entry", host => Entry(host, collapsed: false, selected: false),
            ListBoxItem.ForegroundProperty, "Intent.TextSecondary");
        yield return new("Sidebar", "the selected entry", host => Entry(host, collapsed: false, selected: true),
            ListBoxItem.ForegroundProperty, "Intent.Accent");
        yield return new("Sidebar", "a collapsed entry", host => Entry(host, collapsed: true, selected: false),
            ListBoxItem.PaddingProperty, "Component.Sidebar.ItemPaddingCollapsed");
        yield return new("Sidebar", "the selected collapsed entry", host => Entry(host, collapsed: true, selected: true),
            ListBoxItem.BorderBrushProperty, "Intent.Emphasis.Fill");
        yield return new("Sidebar", "the footer", host => Add(host, Stack("sidebarFooter")),
            StackPanel.MarginProperty, "Component.Sidebar.FooterMargin");

        yield return new("Badge", "a badge", host => Add(host, Box("pageBadge")), Border.BackgroundProperty, "Intent.Neutral.Fill");
        yield return new("Badge", "a badge", host => Add(host, Box("pageBadge")), Border.PaddingProperty, "Component.Badge.Padding");
        yield return new("Badge", "a collapsed badge", host => InCollapsedList(host, Box("pageBadge")),
            Border.BackgroundProperty, "Intent.Emphasis.Fill");
        yield return new("Badge", "a collapsed badge", host => InCollapsedList(host, Box("pageBadge")),
            Border.MarginProperty, "Component.Badge.MarginCollapsed");
        yield return new("Badge", "a collapsed badge's count", host => Inside(InCollapsedListHost(host), Box("pageBadge")),
            TextBlock.ForegroundProperty, "Intent.Emphasis.Text");

        yield return new("TabStrip", "a tab", host => Add(host, Press("tab")), Button.PaddingProperty, "Component.TabStrip.TabPadding");
        yield return new("TabStrip", "a tab", host => Add(host, Press("tab")), Button.BorderBrushProperty, "Intent.Clear");
        yield return new("TabStrip", "the active tab", host => Add(host, Press("tab", "active")), Button.BorderBrushProperty, "Intent.Accent");

        yield return new("Warnings", "the page", host => Add(host, With(new Grid(), ["warningPanel"])),
            Grid.MarginProperty, "Component.Warnings.PageMargin");
        yield return new("Warnings", "the empty state", host => Add(host, Stack("warningEmpty")),
            StackPanel.MarginProperty, "Component.Warnings.EmptyMargin");
        yield return new("Warnings", "a finding row", host => Add(host, Box("warningRow")),
            Border.BackgroundProperty, "Intent.Surface");
        yield return new("Warnings", "a finding summary", host => Add(host, Press("warningSummary")),
            Button.BackgroundProperty, "Intent.Clear");
        yield return new("Warnings", "a finding's compact part gap", host => Add(host, Text("warningPart")),
            TextBlock.MarginProperty, "Intent.Gap.CompactItem");
        yield return new("Warnings", "finding details", host => Add(host, Box("warningDetail")),
            Border.PaddingProperty, "Component.Warnings.DetailPadding");
        yield return new("Review", "a group of changes", host => Add(host, Box("reviewGroup")),
            Border.BorderBrushProperty, "Intent.Border");
        yield return new("Review", "the no longer fits group", host => Add(host, Box("reviewGroup", "noLongerFits")),
            Border.BorderBrushProperty, "Intent.Danger");
        yield return new("Review", "the Uncertain group", host => Add(host, Box("reviewGroup", "uncertain")),
            Border.BorderBrushProperty, "Intent.Warning");
        yield return new("Review", "a staged note", host => Add(host, Box("reviewNote")),
            Border.BackgroundProperty, "Intent.Change.Fill");
        yield return new("Review", "an Uncertain note", host => Add(host, Box("reviewNote", "uncertain")),
            Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("Review", "a parser analysis", host => Add(host, Box("reviewAnalysis", "parser")),
            Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Review", "a still fits pill", host => Add(host, Box("reviewPill")),
            Border.BackgroundProperty, "Intent.Success.Fill");
        yield return new("Review", "a changed sentence word", host => Add(host, Box("reviewSentenceToken", "changed")),
            Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("Review", "a changed sentence word", host => Add(host, Box("reviewSentenceToken", "changed")),
            Border.BorderThicknessProperty, "Intent.Stroke.Box");
        yield return new("TryWord", "a failed diagnostic", host => Add(host, Text("failed")),
            TextBlock.ForegroundProperty, "Intent.Danger");
        yield return new("Timing", "the page", host => Add(host, Box("timingPage")), Border.PaddingProperty,
            "Component.Timing.PagePadding");
        yield return new("Timing", "the table header", host => Add(host, Box("timingTableHeader")),
            Border.BackgroundProperty, "Intent.Surface.Subtle");
        yield return new("Timing", "the chosen rule row", host => Add(host, Press("timingRuleRow", "chosen")),
            Button.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Timing", "the table's columns", host => Add(host, With(new Grid(), ["timingColumns"])),
            Grid.ColumnSpacingProperty, "Intent.Space.Group");
        yield return new("Timing", "the kind bar's morphological rules", host => Add(host, new SIL.Motif.App.Views.TimingKindBar()),
            SIL.Motif.App.Views.TimingKindBar.MorphRuleBrushProperty, "Intent.Timing.MorphRule");
        yield return new("Timing", "the kind bar's phonological rules", host => Add(host, new SIL.Motif.App.Views.TimingKindBar()),
            SIL.Motif.App.Views.TimingKindBar.PhonRuleBrushProperty, "Intent.Timing.PhonRule");
        yield return new("Timing", "the kind bar's lexical entries", host => Add(host, new SIL.Motif.App.Views.TimingKindBar()),
            SIL.Motif.App.Views.TimingKindBar.LexiconBrushProperty, "Intent.Timing.Lexicon");
        yield return new("Timing", "the kind bar's root lookup", host => Add(host, new SIL.Motif.App.Views.TimingKindBar()),
            SIL.Motif.App.Views.TimingKindBar.RootLookupBrushProperty, "Intent.Timing.RootLookup");
        yield return new("Timing", "the kind bar's time no rule accounts for", host => Add(host, new SIL.Motif.App.Views.TimingKindBar()),
            SIL.Motif.App.Views.TimingKindBar.UnattributedBrushProperty, "Intent.Timing.Unattributed");
        yield return new("Timing", "a rule row", host => Add(host, Press("timingRuleRow")), Button.PaddingProperty,
            "Component.Timing.RowPadding");
        yield return new("Selection", "a text count", host => Add(host, Text("textCount")),
            TextBlock.MarginProperty, "Component.Selection.CountMargin");
        yield return new("Setup", "the dialog", host => Add(host, Box("setupDialog")),
            Border.WidthProperty, "Component.Setup.DialogWidth");
        yield return new("Overview", "the header", host => Add(host, Box("overviewHeader")),
            Border.PaddingProperty, "Component.Overview.HeaderPadding");
        yield return new("Overview", "a summary tile", host => Add(host, Press("overviewTile")),
            Button.BackgroundProperty, "Intent.Surface");
        yield return new("Overview", "a summary tile", host => Add(host, Press("overviewTile")),
            Button.PaddingProperty, "Component.Overview.TilePadding");
        yield return new("Overview", "the Look first row", host => Add(host, Press("overviewLookFirstRow")),
            Button.PaddingProperty, "Intent.Inset.None");
        yield return new("Overview", "the Look first row", host => Add(host, Press("overviewLookFirstRow")),
            Button.BackgroundProperty, "Intent.Surface");
        yield return new("Overview", "the Look first action", host => Add(host, Text("overviewLookFirstAction")),
            TextBlock.ForegroundProperty, "Intent.Link");
        yield return new("Overview", "the Look first action", host => Add(host, Text("overviewLookFirstAction")),
            TextBlock.FontSizeProperty, "Intent.Type.Small");
        yield return new("Overview", "a framed tile", host => Add(host, Box("overviewTile")),
            Border.BackgroundProperty, "Intent.Surface");
        yield return new("Overview", "a framed tile", host => Add(host, Box("overviewTile")),
            Border.PaddingProperty, "Component.Overview.TilePadding");
        yield return new("Overview", "a tile value", host => Add(host, Text("overviewTileValue")),
            TextBlock.FontSizeProperty, "Intent.Type.Title");
        yield return new("Overview", "a handoff icon", host => Add(host,
                new PathIcon { Classes = { "overviewHandoffIcon" } }),
            Control.WidthProperty, "Component.Overview.HandoffIconSize");

        yield return new("HoverReveal", "a Look first action at rest", host => Within(host,
                new Border { Classes = { "hoverReveal" } }, "revealOnHover"),
            TextBlock.OpacityProperty, "Component.HoverReveal.HiddenOpacity");

        yield return new("OpinionMark", "an approved fill", host => Add(host, Box("opinionMark", "approved")),
            Border.BackgroundProperty, "Intent.Opinion.Approved.Fill");
        yield return new("OpinionMark", "a mark's stroke", host => Add(host, Box("opinionMark", "approved")),
            Border.BorderThicknessProperty, "Component.OpinionMark.Stroke");
        yield return new("OpinionMark", "the missing-analysis text", host => Inside(host, Box("opinionMark", "none")),
            TextBlock.ForegroundProperty, "Intent.Opinion.None.Text");
        yield return new("OpinionMark", "the missing-analysis outline", EmptyMarkDash,
            Shape.StrokeProperty, "Intent.Opinion.None.Outline");
        yield return new("OpinionMark", "an unknown mark's shape", host => Add(host, Box("opinionMark", "unknown")),
            Border.CornerRadiusProperty, "Component.OpinionMark.UnknownRadius");
        yield return new("UnreadMark", "an unread mark", host => Add(host, Box("unreadMark")),
            Border.BackgroundProperty, "Intent.Unread.Fill");
        yield return new("UnreadMark", "an unread mark's edge", host => Add(host, Box("unreadMark")),
            Border.BorderBrushProperty, "Intent.Unread.Text");
        yield return new("UnreadMark", "the unread label", host => Inside(host, Box("unreadMark")),
            TextBlock.ForegroundProperty, "Intent.Unread.Text");
        yield return new("UnreadMark", "the unread dot", UnreadDot, Shape.FillProperty, "Intent.Unread.Text");
        yield return new("UnreadMark", "the unread dot size", UnreadDot, Control.WidthProperty, "Intent.Space.Snug");
        yield return new("UnreadMark", "the unread dot height", UnreadDot, Control.HeightProperty, "Intent.Space.Snug");
        yield return new("UnreadMark", "the unread content gap", UnreadContent, StackPanel.SpacingProperty,
            "Intent.Space.Snug");

        yield return new("WordRow", "a row's divider", host => Add(host, Box("wordRowFrame")),
            Border.BorderBrushProperty, "Intent.Border");
        yield return new("WordRow", "an opened row", host => Add(host, Box("wordRowFrame", "open")),
            Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("WordRow", "an opened row's accent edge", OpenRowEdge, Border.BorderBrushProperty, "Intent.Accent");
        yield return new("WordRow", "a row's body", host => Add(host, Box("wordRowBody")),
            Border.PaddingProperty, "Component.WordRow.Padding");
        yield return new("WordRow", "the compact cell gap", host => Add(host, With(new Grid(), ["wordRowCells"])),
            Grid.ColumnSpacingProperty, "Intent.Space.Compact");
        yield return new("WordRow", "a row's height", host => Add(host, With(new Grid(), ["wordRowLine"])),
            Control.MinHeightProperty, "Component.WordRow.MinHeight");
        yield return new("WordRow", "the word form's hit area", host => Add(host, Text("wordRowForm")),
            TextBlock.BackgroundProperty, "Intent.Clear");
        yield return new("WordRow", "the word", host => Add(host, Text("wordRowForm")),
            TextBlock.FontSizeProperty, "Intent.Type.Navigation");
        yield return new("WordRow", "the gloss", host => Add(host, Text("wordRowGloss")),
            TextBlock.ForegroundProperty, "Intent.TextMuted");
        yield return new("WordRow", "a staged change", host => Add(host, Text("wordRowStaged")),
            TextBlock.ForegroundProperty, "Intent.Change.Text");
        yield return new("WordRow", "a morpheme PanGloss built differently",
            host => Add(host, Box("wordRowMorph", "different")), Border.BackgroundProperty, "Intent.Outcome.Different.Fill");
        yield return new("WordRow", "a differing morpheme's form", host => Inside(host, Box("wordRowMorph", "different")),
            TextBlock.ForegroundProperty, "Intent.Outcome.Different");
        yield return new("WordRow", "the opened card", host => Add(host, Box("wordRowCard")),
            Border.PaddingProperty, "Component.WordRow.CardPadding");
        yield return new("WordRow", "the meaning column", host => Add(host, Box("wordRowMeaning")),
            Control.WidthProperty, "Component.WordRow.MeaningWidth");
        yield return new("WordRow", "a column head", host => Add(host, Text("wordRowHeading")),
            TextBlock.ForegroundProperty, "Intent.TextMuted");

        yield return new("PanGlossLine", "a same reading label", host => Inside(host, Box("panGlossLine", "same")),
            TextBlock.ForegroundProperty, "Intent.Outcome.Same");
        yield return new("PanGlossLine", "a different reading", host => Add(host, Box("panGlossLine", "different")),
            Border.BackgroundProperty, "Intent.Surface");
        yield return new("PanGlossLine", "a different reading edge", host => Add(host, Box("panGlossLine", "different")),
            Border.BorderBrushProperty, "Intent.Outcome.Different");
        yield return new("PanGlossLine", "a different reading label", host => Inside(host, Box("panGlossLine", "different")),
            TextBlock.ForegroundProperty, "Intent.Outcome.Different");
        yield return new("PanGlossLine", "a conflicting reading", host => Add(host, Box("panGlossLine", "conflict")),
            Border.BackgroundProperty, "Intent.Outcome.Different.Fill");
        yield return new("PanGlossLine", "a conflicting reading edge", host => Add(host, Box("panGlossLine", "conflict")),
            Border.BorderBrushProperty, "Intent.Outcome.Different");
        yield return new("PanGlossLine", "a conflicting inset edge", host => Add(host, Box("panGlossLine", "conflict")),
            Border.BoxShadowProperty, "Intent.Outcome.Different.Marker");
        yield return new("PanGlossLine", "a conflicting reading label", host => Inside(host, Box("panGlossLine", "conflict")),
            TextBlock.ForegroundProperty, "Intent.Outcome.Different");
        yield return new("PanGlossLine", "a no-parse label", host => Inside(host, Box("panGlossLine", "none")),
            TextBlock.ForegroundProperty, "Intent.Outcome.NoParse");
        yield return new("PanGlossLine", "a stopped label", host => Inside(host, Box("panGlossLine", "capped")),
            TextBlock.ForegroundProperty, "Intent.Outcome.Stopped");
        yield return new("PanGlossLine", "a not-parsed label", host => Inside(host, Box("panGlossLine", "notAssessed")),
            TextBlock.ForegroundProperty, "Intent.Outcome.NotParsed");
        yield return new("PanGlossLine", "an extra reading count", host => Add(host, Box("panGlossExtra")),
            Border.BorderBrushProperty, "Intent.Outcome.Different");
        yield return new("PanGlossLine", "a suggested reading count", host => Inside(host, Box("panGlossExtra")),
            TextBlock.ForegroundProperty, "Intent.Outcome.Different");

        foreach (var (outcome, key) in new[]
        {
            ("same", "Same"), ("different", "Different"), ("noParse", "NoParse"), ("stopped", "Stopped"), ("notParsed", "NotParsed"),
        })
            yield return new("Mark", $"a {outcome} outcome", host => Add(host, Text("outcomeMark", outcome)),
                TextBlock.ForegroundProperty, $"Intent.Outcome.{key}");
        yield return new("OutcomeBar", "an outcome sign's line floor", host =>
        {
            var label = new StackPanel { Classes = { "outcomeLegendLabel" } };
            var glyph = new MarkGlyph { Mark = SIL.Motif.App.ViewModels.Mark.NoParse, Classes = { "inline" } };
            label.Children.Add(glyph);
            host.Children.Add(label);
            return glyph;
        }, Control.MinHeightProperty, "Component.OutcomeBar.LegendLineHeight");
        yield return new("Mark", "a stopped outcome icon", host => MarkIcon(host, SIL.Motif.App.ViewModels.Mark.Stopped),
            Control.WidthProperty, "Component.Mark.InlineGlyphSize");
        yield return new("Mark", "a stopped outcome icon", host => MarkIcon(host, SIL.Motif.App.ViewModels.Mark.Stopped),
            Control.HeightProperty, "Component.Mark.InlineGlyphSize");
        yield return new("Mark", "a stopped outcome icon's colour", host => MarkIcon(host, SIL.Motif.App.ViewModels.Mark.Stopped),
            PathIcon.ForegroundProperty, "Intent.Outcome.Stopped");
        yield return new("Mark", "an outcome, which never fills", host => Add(host, Text("outcomeMark", "noParse")),
            TextBlock.BackgroundProperty, "Intent.Clear");
        foreach (var (meaning, key) in new[] { ("fine", "Fine"), ("look", "Look"), ("problem", "Problem"), ("neutral", "Neutral") })
            yield return new("Mark", $"a {meaning} meaning's words", host => Inside(host, Box("meaningMark", meaning)),
                TextBlock.ForegroundProperty, $"Intent.Consequence.{key}");
        foreach (var (meaning, key) in new[] { ("look", "Look"), ("problem", "Problem") })
        {
            yield return new("Mark", $"a {meaning} meaning", host => Add(host, Box("meaningMark", meaning)),
                Border.BackgroundProperty, $"Intent.Consequence.{key}.Fill");
            yield return new("Mark", $"a {meaning} meaning", host => Add(host, Box("meaningMark", meaning)),
                Border.BorderBrushProperty, $"Intent.Consequence.{key}.Edge");
        }
        yield return new("Mark", "a fine meaning", host => Add(host, Box("meaningMark", "fine")),
            Border.BackgroundProperty, "Intent.Clear");
        yield return new("Mark", "a meaning", host => Add(host, Box("meaningMark")), Border.PaddingProperty, "Component.Mark.Padding");
        yield return new("Mark", "a meaning", host => Add(host, Box("meaningMark")), Border.CornerRadiusProperty, "Component.Mark.Radius");
        foreach (var (severity, key) in new[] { ("warning", "Warning"), ("error", "Error"), ("info", "Info") })
            yield return new("Mark", $"a {severity} mark's words", host => Inside(host, Box("severityMark", severity)),
                TextBlock.ForegroundProperty, $"Intent.Severity.{key}");
        yield return new("Mark", "a warning mark", host => Add(host, Box("severityMark", "warning")),
            Border.BackgroundProperty, "Intent.Severity.Warning.Fill");
        yield return new("Mark", "an error mark", host => Add(host, Box("severityMark", "error")),
            Border.BackgroundProperty, "Intent.Clear");
        foreach (var (step, key) in new[] { ("built", "Intent.Success.Text"), ("refused", "Intent.Danger.Text"), ("tried", "Intent.TextMuted") })
            yield return new("Mark", $"a {step} trace step", host => Add(host, Text("stepMark", step)),
                TextBlock.ForegroundProperty, key);
        yield return new("Mark", "a severity glyph", host => Add(host, Box("severityGlyph", "error")),
            Border.MinWidthProperty, "Component.Mark.GlyphSize");
        yield return new("Mark", "a severity glyph", host => Add(host, Box("severityGlyph", "error")),
            Border.CornerRadiusProperty, "Component.Mark.GlyphRadius");
        yield return new("Mark", "an error glyph's ring", host => Add(host, Box("severityGlyph", "error")),
            Border.BorderBrushProperty, "Intent.Severity.Error");
        yield return new("Mark", "a note glyph's ring", host => Add(host, Box("severityGlyph", "info")),
            Border.BorderBrushProperty, "Intent.Severity.Info");
        yield return new("Mark", "a warning glyph, which has no ring", host => Add(host, Box("severityGlyph", "warning")),
            Border.BorderBrushProperty, "Intent.Clear");
        yield return new("Mark", "a warning icon", WarningIcon, Control.WidthProperty, "Component.Mark.WarningIconSize");
        yield return new("Mark", "a warning icon's colour", WarningIcon, PathIcon.ForegroundProperty, "Intent.Severity.Warning");
        foreach (var (severity, key) in new[] { ("warning", "Warning"), ("error", "Error"), ("info", "Info") })
            yield return new("Mark", $"a {severity} glyph's sign", host => Inside(host, Box("severityGlyph", severity)),
                TextBlock.ForegroundProperty, $"Intent.Severity.{key}");
        yield return new("Mark", "a glyph's sign", host => Inside(host, Box("severityGlyph", "error")),
            TextBlock.FontSizeProperty, "Component.Mark.GlyphType");
        yield return new("ActionChip", "the primary action floor", host => Add(host, Press("actionChip", "primary")),
            Button.MinHeightProperty, "Component.ActionChip.Height");
        yield return new("ActionChip", "the Fix menu action", host => Add(host, Press("actionChip", "fix")),
            Button.BorderBrushProperty, "Intent.Marking.Border");
        yield return new("ActionChip", "the Fix menu text", host => Add(host, Press("actionChip", "fix")),
            Button.ForegroundProperty, "Intent.Marking.Text");
        yield return new("ActionChip", "the primary action text", host => Add(host, Press("actionChip", "primary")),
            Button.ForegroundProperty, "Intent.Opinion.Approved.Text");
        yield return new("StagedStrip", "the staged change", host => Add(host, Box("stagedStrip")),
            Border.BackgroundProperty, "Intent.Change.Fill");
        yield return new("Inspector", "the inspector's surface", host => Add(host, Box("inspector")),
            Border.BackgroundProperty, "Intent.Surface.Raised");
        yield return new("Inspector", "the inspector's width", host => Add(host, Box("inspector")),
            Control.WidthProperty, "Component.Inspector.Width");
        yield return new("Inspector", "the breadcrumb strip", host => Add(host, Box("inspectorCrumbs")),
            Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Inspector", "a section's source", host => Add(host, Text("inspectorSource")),
            TextBlock.ForegroundProperty, "Intent.TextMuted");
        yield return new("Inspector", "a warning that names it", host => Add(host, Box("inspectorWarning")),
            Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("Inspector", "a name that opens it", host => Add(host, new SIL.Motif.App.Views.InspectLink { Content = "kat" }),
            Button.ForegroundProperty, "Intent.Link");
        yield return new("Inspector", "a Lost word", host => Add(host, Text("inspectorMeaning", "problem")),
            TextBlock.ForegroundProperty, "Intent.Consequence.Problem");
        yield return new("WordCard", "the word card surface", host => Add(host, Box("card", "wordCard")),
            Border.BackgroundProperty, "Intent.Surface.Raised");
        yield return new("WordCard", "the word card shadow", host => Add(host, Box("card", "wordCard")),
            Border.BoxShadowProperty, "Intent.Shadow.Raised");
        yield return new("WordCard", "the word card edge", host => Add(host, Box("card", "wordCard")),
            Border.BorderBrushProperty, "Intent.Accent");
        yield return new("WordCard", "the word card header", host => Add(host, Box("wordCardHead")),
            Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("WordCard", "the list card's gaps", host => Add(host, Stack("listCard")),
            StackPanel.SpacingProperty, "Intent.Space.Related");
        yield return new("WordCard", "the list card's columns", host => Add(host, With(new Grid(), ["listCardAlignment"])),
            Grid.ColumnSpacingProperty, "Intent.Space.Compact");
        yield return new("WordCard", "a list card segment at rest", host => Add(host, Box("listCardSpan")),
            Border.BorderBrushProperty, "Intent.Clear");
        yield return new("WordCard", "where the two analyses part", host => Add(host, Box("listCardSpan", "parted")),
            Border.BorderBrushProperty, "Intent.Border");
        yield return new("WordCard", "the segment's edge", host => Add(host, Box("listCardSpan", "parted")),
            Border.BorderThicknessProperty, "Intent.Stroke.Box");
        yield return new("WordCard", "a linked form in the list card", host => Within(host, Box("listCardMorph"), "listCardFormText"),
            TextBlock.ForegroundProperty, "Intent.Text");
        yield return new("WordCard", "a differing linked form in the list card",
            host => Within(host, Box("listCardMorph", "different"), "listCardFormText"),
            TextBlock.ForegroundProperty, "Intent.Outcome.Different");
        yield return new("WordCard", "the where-they-part sentence", host => Add(host, Text("listCardSentence")),
            TextBlock.FontSizeProperty, "Intent.Type.Small");
        yield return new("WordCard", "the closest-reading note", host => Add(host, Text("listCardNote")),
            TextBlock.MarginProperty, "Component.WordCard.NoteGap");
        yield return new("WordCard", "an action in the word card", host => Add(host, Press("wordCardAction")),
            Button.MarginProperty, "Intent.Gap.CompactItem");
        yield return new("WordStrip", "a word strip's group gap", host => Add(host, Box("wordStrip")),
            Border.MarginProperty, "Intent.Gap.CompactItem");
        yield return new("WordStrip", "a resting word strip edge", host => Add(host, Box("wordStrip")),
            Border.BorderBrushProperty, "Intent.Clear");
        yield return new("WordStrip", "an open word strip edge", host => Add(host, Box("wordStrip", "open")),
            Border.BorderBrushProperty, "Intent.Accent");
        yield return new("WordStrip", "an open word strip", host => Add(host, Box("wordStrip", "open")),
            Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("WordStrip", "an analysis row", host => Add(host, Box("stripRow", "analysisRow")),
            Control.MinHeightProperty, "Component.WordStrip.AnalysisRowHeight");
        yield return new("WordStrip", "word and analysis rows have no extra gap",
            host => Add(host, new StackPanel { Classes = { "stripStack" } }),
            StackPanel.SpacingProperty, "Intent.Space.None");
        yield return new("WordStrip", "the gutter aligns its analysis rows",
            host => Add(host, new StackPanel { Classes = { "gutter" } }),
            StackPanel.SpacingProperty, "Intent.Space.None");
        yield return new("WordStrip", "the analysis rows keep a one-pixel divider gap",
            host => Add(host, Box("stripFieldWorks")),
            Control.MarginProperty, "Component.WordStrip.AnalysisRowGap");
        yield return new("WordStrip", "the FieldWorks gutter label has the divider gap",
            host => Add(host, new TextBlock { Classes = { "gutterLabel", "fieldWorksRow" } }),
            Control.MarginProperty, "Component.WordStrip.AnalysisRowGap");
        yield return new("WordStrip", "an analysis row rule", host => Add(host, Box("stripRow")),
            Border.BorderBrushProperty, "Intent.Marking.Divider");
        yield return new("UnreadMark", "the unread dot alone", host => Add(host, Box("unreadMark", "dotOnly")),
            Border.BackgroundProperty, "Intent.Clear");
        yield return new("HoverReveal", "a hidden secondary action", RevealControl,
            Control.OpacityProperty, "Component.HoverReveal.HiddenOpacity");
        yield return new("HoverReveal", "the staged button height", host => Add(host, Press("revealControl", "revealButton")),
            Button.MinHeightProperty, "Component.HoverReveal.Height");
        yield return new("HoverReveal", "the FieldWorks link", host => Add(host, Press("revealControl", "revealLink")),
            Button.ForegroundProperty, "Intent.Marking.Link");
        yield return new("Density", "the compact page size", host => DensityText(host, normal: false),
            TextBlock.FontSizeProperty, "Component.Density.CompactType");
        yield return new("TextsPage", "results kept while a parse runs", host => Add(host, Stack("waiting")),
            Visual.OpacityProperty, "Intent.Opacity.Waiting");

        yield return new("Interaction", "the focus ring", host => Add(host, new SIL.Motif.App.Views.FocusRing()),
            Border.BorderBrushProperty, "Intent.Focus");
        yield return new("Interaction", "the focus ring", host => Add(host, new SIL.Motif.App.Views.FocusRing()),
            Border.BorderThicknessProperty, "Intent.Stroke.Focus");

        yield return new("Density", "the compact word strip height", CompactWordStrip,
            Border.MinHeightProperty, "Component.Density.CompactWordHeight");
        yield return new("Density", "the compact word strip type", host =>
                Assert.IsType<StackPanel>(CompactWordStrip(host).Child).Children.OfType<TextBlock>().Single(),
            TextBlock.FontSizeProperty, "Component.Density.CompactType");
        yield return new("Density", "the normal word strip type", host =>
                Assert.IsType<StackPanel>(DensityWordStrip(host, normal: true).Child).Children.OfType<TextBlock>().Single(),
            TextBlock.FontSizeProperty, "Component.Density.NormalType");
        yield return new("Density", "the compact opinion mark size", CompactOpinionMark,
            Control.MinHeightProperty, "Component.Density.CompactMarkSize");
        yield return new("Density", "the normal page size", host => DensityText(host, normal: true),
            TextBlock.FontSizeProperty, "Component.Density.NormalType");
        yield return new("ToolTip", "a staged strip's tooltip words", TipInStagedStrip,
            TextBlock.ForegroundProperty, "Intent.Tooltip.Text");
        yield return new("ToolTip", "a staged strip's tooltip size", TipInStagedStrip,
            TextBlock.FontSizeProperty, "Intent.Type.Small");
        yield return new("HoverReveal", "a neutral staged button",host => Add(host, Press("revealControl", "revealButton")),
            Button.FontSizeProperty, "Component.HoverReveal.ButtonType");
        yield return new("HoverReveal", "the staged button radius", host => Add(host, Press("revealControl", "revealButton")),
            Button.CornerRadiusProperty, "Component.HoverReveal.ButtonRadius");
        yield return new("HoverReveal", "the staged button border", host => Add(host, Press("revealControl", "revealButton")),
            Button.BorderThicknessProperty, "Intent.Stroke.Box");
        yield return new("HoverReveal", "the staged button text", host => Add(host, Press("revealControl", "revealButton")),
            Button.ForegroundProperty, "Intent.Marking.Text");
        yield return new("HoverReveal", "a FieldWorks link", host => Add(host, Press("revealControl", "revealLink")),
            Button.FontSizeProperty, "Component.HoverReveal.LinkType");
        yield return new("HoverReveal", "the staged button padding", host => Add(host, Press("revealControl", "revealButton")),
            Button.PaddingProperty, "Component.HoverReveal.ButtonPadding");
        yield return new("HoverReveal", "the staged button edge", host => Add(host, Press("revealControl", "revealButton")),
            Button.BorderBrushProperty, "Intent.Marking.Border");
        yield return new("HoverReveal", "the staged button surface", host => Add(host, Press("revealControl", "revealButton")),
            Button.BackgroundProperty, "Intent.Marking.Surface");
        yield return new("HoverReveal", "the FieldWorks link colour", host => Add(host, Press("revealControl", "revealLink")),
            Button.ForegroundProperty, "Intent.Marking.Link");
        yield return new("HoverReveal", "the FieldWorks link padding", host => Add(host, Press("revealControl", "revealLink")),
            Button.PaddingProperty, "Intent.Inset.None");
        yield return new("Settings", "the popup corner radius", host => Add(host, Box("settingsSurface")),
            Border.CornerRadiusProperty, "Component.Settings.Radius");
        yield return new("Settings", "the gear width", host => Add(host, Press("settingsGear")),
            Button.WidthProperty, "Component.Menu.ButtonHeight");
        yield return new("Settings", "a shortcut key cap radius", host => Add(host, Box("settingsKeyCap")),
            Border.CornerRadiusProperty, "Component.Settings.KeyRadius");
    }

    private static Button RevealControl(Panel host)
    {
        var button = Press("revealControl", "revealButton", "revealOnHover");
        Add(host, new Border { Classes = { "hoverReveal" }, Child = button });
        return button;
    }

    private static Rectangle EmptyMarkDash(Panel host)
    {
        var dash = new Rectangle { Classes = { "opinionDash" } };
        Add(host, new Border
        {
            Classes = { "opinionMark", "none" },
            Child = new Grid { Children = { dash } },
        });
        return dash;
    }

    private static StackPanel UnreadContent(Panel host)
    {
        var content = new StackPanel { Classes = { "unreadMarkContent" } };
        content.Children.Add(new Ellipse { Classes = { "unreadMarkDot" } });
        content.Children.Add(new TextBlock { Text = "Unread" });
        Add(host, new Border { Classes = { "unreadMark" }, Child = content });
        return content;
    }

    private static Ellipse UnreadDot(Panel host) => (Ellipse)UnreadContent(host).Children[0];

    private static TextBlock DensityText(Panel host, bool normal)
    {
        var root = new StackPanel { Classes = { "analysisDensity" } };
        if (normal) root.Classes.Add("normal");
        var text = Text("densitySample");
        root.Children.Add(text);
        host.Children.Add(root);
        return text;
    }

    private static Control CompactOpinionMark(Panel host)
    {
        var root = new StackPanel { Classes = { "analysisDensity" } };
        var mark = Box("opinionMark");
        root.Children.Add(mark);
        host.Children.Add(root);
        return mark;
    }

    // The staged strip restyles every TextBlock beneath it, so its tooltip's words show whether the tip wins.
    private static TextBlock TipInStagedStrip(Panel host)
    {
        var words = new TextBlock { Text = "Still fits the project." };
        var tip = new ToolTip { Content = words };
        ((IPseudoClasses)tip.Classes).Set(":open", true);
        host.Children.Add(new Border { Classes = { "stagedStrip" }, Child = tip });
        return words;
    }

    private static Border CompactWordStrip(Panel host)
        => DensityWordStrip(host, normal: false);

    private static Border DensityWordStrip(Panel host, bool normal)
    {
        var root = new Border { Classes = { "wordVerdict", "analysisDensity" } };
        if (normal) root.Classes.Add("normal");
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "word" });
        content.Children.Add(Box("opinionMark"));
        root.Child = content;
        host.Children.Add(root);
        return root;
    }

    private static T Add<T>(Panel host, T control) where T : Control
    {
        host.Children.Add(control);
        return control;
    }

    private static PathIcon MarkIcon(Panel host, SIL.Motif.App.ViewModels.Mark mark)
    {
        var glyph = new MarkGlyph { Mark = mark, Classes = { "inline" } };
        host.Children.Add(glyph);
        return Assert.IsType<PathIcon>(glyph.Child);
    }

    private static TextBlock MatrixChosenMark(Panel host)
    {
        var group = new StackPanel { Classes = { "matrixChosenMarks" } };
        var mark = new MarkGlyph { Classes = { "inline", "bodyText", "matrixChosenWord" }, Mark = SIL.Motif.App.ViewModels.Mark.Same };
        group.Children.Add(mark);
        host.Children.Add(group);
        return Assert.IsType<TextBlock>(mark.Child);
    }

    private static PathIcon MatrixChosenIcon(Panel host)
    {
        var group = new StackPanel { Classes = { "matrixChosenMarks" } };
        var mark = new MarkGlyph { Classes = { "inline", "bodyText", "matrixChosenWord" }, Mark = SIL.Motif.App.ViewModels.Mark.Stopped };
        group.Children.Add(mark);
        host.Children.Add(group);
        return Assert.IsType<PathIcon>(mark.Child);
    }

    private static Control WarningIcon(Panel host)
    {
        var glyph = new MarkGlyph { Mark = SIL.Motif.App.ViewModels.Mark.Warning };
        var border = new Border
        {
            Classes = { "severityGlyph", "warning" },
            Child = glyph,
        };
        Add(host, border);
        return Assert.IsType<PathIcon>(glyph.Child);
    }

    private static TextBlock Inside(Panel host, Border border)
    {
        var text = new TextBlock { Text = "3" };
        border.Child = text;
        host.Children.Add(border);
        return text;
    }

    private static TextBlock Within(Panel host, Border border, params string[] textClasses)
    {
        var text = Text(textClasses);
        border.Child = text;
        host.Children.Add(border);
        return text;
    }

    private static TextBlock InsideButton(Panel host, Button button, string textClass)
    {
        var text = Text(textClass);
        button.Content = text;
        host.Children.Add(button);
        return text;
    }

    private static Border InCollapsedList(Panel host, Border border)
    {
        InCollapsedListHost(host).Children.Add(border);
        return border;
    }

    private static Panel InCollapsedListHost(Panel host)
    {
        var inner = new StackPanel();
        var list = new ListBox { Classes = { "sidebar", "collapsed" } };
        list.Items.Add(new ListBoxItem { Content = inner });
        host.Children.Add(list);
        return inner;
    }

    private static Border OpenRowEdge(Panel host)
    {
        var edge = Box("wordRowEdge");
        host.Children.Add(new Border { Classes = { "wordRowFrame", "open" }, Child = edge });
        return edge;
    }

    private static ListBoxItem DenseListRow(Panel host)
    {
        var list = new ListBox { Classes = { "dense" } };
        var item = new ListBoxItem();
        list.Items.Add(item);
        host.Children.Add(list);
        return item;
    }

    private static ListBoxItem Entry(Panel host, bool collapsed, bool selected)
    {
        var list = new ListBox { Classes = { "sidebar" } };
        if (collapsed) list.Classes.Add("collapsed");
        var item = new ListBoxItem { Content = "Overview" };
        list.Items.Add(item);
        host.Children.Add(list);
        if (selected) list.SelectedIndex = 0;
        return item;
    }

    private static TextBlock Text(params string[] classes) => With(new TextBlock { Text = "Motif" }, classes);

    private static Border Box(params string[] classes) => With(new Border(), classes);

    private static Button Press(params string[] classes) => With(new Button { Content = "Go" }, classes);

    private static StackPanel Stack(params string[] classes) => With(new StackPanel(), classes);

    private static Ellipse Dot(params string[] classes) => With(new Ellipse { Classes = { "freshDot" } }, classes);

    private static T With<T>(T control, string[] classes) where T : StyledElement
    {
        control.Classes.AddRange(classes);
        return control;
    }

    private static string AppDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return System.IO.Path.Combine(root.FullName, "src", "SIL.Motif.App");
    }

    private static Point CentreOf(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("The control is not positioned in the window.");
}
