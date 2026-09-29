using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Applies every component style file to controls wearing its classes, in the light and the dark theme, and
/// checks each chosen property took the token's value. A misspelled selector matches nothing and raises no
/// error, so only applying the styles shows that a component is styled at all.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentStyleTests
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
                    Assert.True(Application.Current!.TryGetResource(item.Key, variant, out var expected));
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
    public void ReviewParserOutlineKeepsItsDashedStroke()
    {
        _avalonia.Invoke(() =>
        {
            var outline = new Rectangle();
            outline.Classes.Add("reviewParserOutline");
            var window = new Window { Content = outline };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal([3d, 2d], outline.StrokeDashArray);
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
                Assert.Equal(expected, chip.Background);
            }
            finally
            {
                window.Close();
            }
        });
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

        yield return new("VerdictChip", "an approved chip", host => Add(host, Box("verdictChip", "approved")),
            Border.BackgroundProperty, "Intent.Approved.Fill");
        yield return new("VerdictChip", "an approved chip's text", host => Inside(host, Box("verdictChip", "approved")),
            TextBlock.ForegroundProperty, "Intent.Approved.Text");
        yield return new("VerdictChip", "a candidate chip's text", host => Inside(host, Box("verdictChip", "candidate")),
            TextBlock.ForegroundProperty, "Intent.Candidate.Text");
        yield return new("VerdictChip", "a chip", host => Add(host, Box("verdictChip")), Border.PaddingProperty, "Component.VerdictChip.Padding");
        yield return new("VerdictChip", "a chip", host => Add(host, Box("verdictChip")), Border.CornerRadiusProperty, "Component.VerdictChip.Radius");

        yield return new("OutcomeBar", "a candidate segment", host => Add(host, Box("outcomeSegment", "candidate")),
            Border.BackgroundProperty, "Intent.Candidate.Accent");
        yield return new("OutcomeBar", "an unknown segment", host => Add(host, Box("outcomeSegment")),
            Border.BackgroundProperty, "Intent.TextFaint");

        yield return new("WordVerdict", "an approved word", host => Add(host, Box("wordVerdict", "approved")),
            Border.BorderBrushProperty, "Intent.Approved.Accent");
        yield return new("WordVerdict", "a word", host => Add(host, Box("wordVerdict")), Border.BackgroundProperty, "Intent.Clear");
        yield return new("WordVerdict", "a word", host => Add(host, Box("wordVerdict")), Border.PaddingProperty, "Component.WordVerdict.Padding");
        yield return new("WordVerdict", "a dimmed word", host => Add(host, Box("wordVerdict", "dimmed")),
            Visual.OpacityProperty, "Intent.Opacity.Dimmed");
        yield return new("WordVerdict", "a changed uncertain word", host => Add(host, Box("wordVerdict", "uncertainChanged")),
            Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("WordVerdict", "a changed uncertain word", host => Add(host, Box("wordVerdict", "uncertainChanged")),
            Border.BorderBrushProperty, "Intent.Warning.Text");

        yield return new("FilterChip", "a chip", host => Add(host, Press("filterChip")), Button.BackgroundProperty, "Intent.Clear");
        yield return new("FilterChip", "a chip", host => Add(host, Press("filterChip")), Button.MarginProperty, "Component.FilterChip.Margin");
        yield return new("FilterChip", "an active chip", host => Add(host, Press("filterChip", "active")),
            Button.ForegroundProperty, "Intent.Primary");

        yield return new("HeatCell", "a heat shade", host => Add(host, Box("heat")), Border.BackgroundProperty, "Intent.Warning");
        yield return new("MatrixCell", "a matrix cell", host => Add(host, Box("matrixCell")),
            Border.BackgroundProperty, "Intent.Surface");
        yield return new("MatrixCell", "a compact word cell", host => Add(host, Box("matrixWordCell")),
            Border.PaddingProperty, "Component.MatrixCell.WordPadding");
        yield return new("MatrixCell", "a compact word cell", host => Add(host, Box("matrixWordCell")),
            Border.BorderBrushProperty, "Intent.Border");
        yield return new("MatrixCell", "a violation cell", host => Add(host, Box("matrixCell", "violation")),
            Border.BackgroundProperty, "Intent.Danger.Fill");
        yield return new("MatrixCell", "a compact matrix cell", host => Add(host, Box("matrixCell", "compact")),
            Border.WidthProperty, "Component.MatrixCell.CompactWidth");
        yield return new("MorphemeRow", "a morpheme edge", host => Add(host, Box("morphEdge")), Border.BorderBrushProperty, "Intent.Border");

        yield return new("DifferencePanel", "a move row", DenseListRow,
            ListBoxItem.PaddingProperty, "Component.DifferencePanel.MoveRowPadding");
        yield return new("DifferencePanel", "a move column heading", host => Add(host, Text("columnHeader")),
            TextBlock.ForegroundProperty, "Intent.TextMuted");

        yield return new("Handoff", "a step mark", host => Add(host, Box("stepMark")), Border.BackgroundProperty, "Intent.Emphasis.Fill");
        yield return new("Handoff", "a step mark", host => Add(host, Box("stepMark")), Border.WidthProperty, "Component.Handoff.StepMarkSize");
        yield return new("Handoff", "a file's kind", host => Inside(host, Box("handoffKind")), TextBlock.ForegroundProperty, "Intent.Emphasis.Text");
        yield return new("Handoff", "a written file", host => Add(host, Box("handoffFile")), Border.BorderThicknessProperty,
            "Component.Handoff.FileEdge");
        yield return new("Handoff", "the page header", host => Add(host, Box("handoffHeader")), Border.PaddingProperty,
            "Component.Handoff.HeaderPadding");

        yield return new("TopBar", "the top bar", host => Add(host, Box("topBar")), Border.HeightProperty, "Component.TopBar.Height");
        yield return new("TopBar", "the top bar", host => Add(host, Box("topBar")), Border.PaddingProperty, "Component.TopBar.Padding");
        yield return new("TopBar", "the banner", host => Add(host, Box("banner")), Border.MarginProperty, "Component.TopBar.BannerMargin");
        yield return new("TopBar", "the project menu", host => Add(host, Press("projectMenu")),
            Button.MaxWidthProperty, "Component.TopBar.ProjectMenuMaxWidth");

        yield return new("Menu", "a menu", host => Add(host, Stack("menu")), StackPanel.WidthProperty, "Component.Menu.Width");
        yield return new("Menu", "a menu entry", host => Add(host, Press("menuEntry")), Button.PaddingProperty, "Component.Menu.EntryPadding");
        yield return new("Menu", "a menu entry's detail", host => InsideButton(host, Press("menuEntry"), "menuDetail"),
            TextBlock.ForegroundProperty, "Intent.TextMuted");

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
        yield return new("Warnings", "the empty state", host => Add(host, Box("warningEmpty")),
            Border.MarginProperty, "Component.Warnings.EmptyMargin");
        yield return new("Warnings", "a selected kind", host => Add(host, Press("findingGroup", "chosen")),
            Button.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Warnings", "warning severity", host => Add(host, Text("warningSeverity", "warning")),
            TextBlock.ForegroundProperty, "Intent.Warning");
        yield return new("Review", "a reading", host => Add(host, Box("reviewReading")),
            Border.BorderBrushProperty, "Intent.Border");
        yield return new("Review", "a changed sentence word", host => Add(host, Box("reviewSentenceToken", "changed")),
            Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("Review", "a changed sentence word", host => Add(host, Box("reviewSentenceToken", "changed")),
            Border.BorderThicknessProperty, "Intent.Stroke.Box");
        yield return new("Review", "a parser outline", host => Add(host, With(new Rectangle(), ["reviewParserOutline"])),
            Rectangle.StrokeProperty, "Intent.TextFaint");
        yield return new("TryWord", "a failed diagnostic", host => Add(host, Text("failed")),
            TextBlock.ForegroundProperty, "Intent.Danger");
        yield return new("Timing", "the page", host => Add(host, Box("timingPage")), Border.PaddingProperty,
            "Component.Timing.PagePadding");
        yield return new("Timing", "the table header", host => Add(host, Box("timingTableHeader")),
            Border.BackgroundProperty, "Intent.Surface.Subtle");
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
        yield return new("Overview", "a tile value", host => Add(host, Text("overviewTileValue")),
            TextBlock.FontSizeProperty, "Intent.Type.Title");
        yield return new("Overview", "a handoff icon", host => Add(host,
                new PathIcon { Classes = { "overviewHandoffIcon" } }),
            Control.WidthProperty, "Component.Overview.HandoffIconSize");

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
        yield return new("PanGlossLine", "a different reading", host => Add(host, Box("panGlossLine", "different")),
            Border.BackgroundProperty, "Intent.Surface");
        yield return new("PanGlossLine", "a different reading edge", host => Add(host, Box("panGlossLine", "different")),
            Border.BorderBrushProperty, "Intent.Agreement.Suggestion.Edge");
        yield return new("PanGlossLine", "a different reading label", host => Inside(host, Box("panGlossLine", "different")),
            TextBlock.ForegroundProperty, "Intent.Agreement.Suggestion");
        yield return new("PanGlossLine", "a conflicting reading", host => Add(host, Box("panGlossLine", "conflict")),
            Border.BackgroundProperty, "Intent.Agreement.Conflict.Fill");
        yield return new("PanGlossLine", "a conflicting reading edge", host => Add(host, Box("panGlossLine", "conflict")),
            Border.BorderBrushProperty, "Intent.Agreement.Conflict.Edge");
        yield return new("PanGlossLine", "a conflicting inset edge", host => Add(host, Box("panGlossLine", "conflict")),
            Border.BoxShadowProperty, "Intent.Agreement.Conflict.Shadow");
        yield return new("PanGlossLine", "a conflicting reading label", host => Inside(host, Box("panGlossLine", "conflict")),
            TextBlock.ForegroundProperty, "Intent.Agreement.Conflict");
        yield return new("PanGlossLine", "an extra reading count", host => Add(host, Box("panGlossExtra")),
            Border.BorderBrushProperty, "Intent.Agreement.Suggestion.Edge");
        yield return new("PanGlossLine", "a suggested reading count", host => Inside(host, Box("panGlossExtra")),
            TextBlock.ForegroundProperty, "Intent.Agreement.Suggestion");
        yield return new("ActionChip", "the primary action", host => Add(host, Press("actionChip", "primary")),
            Button.HeightProperty, "Component.ActionChip.Height");
        yield return new("ActionChip", "the Fix menu action", host => Add(host, Press("actionChip", "fix")),
            Button.BorderBrushProperty, "Intent.Marking.Border");
        yield return new("ActionChip", "the Fix menu text", host => Add(host, Press("actionChip", "fix")),
            Button.ForegroundProperty, "Intent.Marking.Text");
        yield return new("ActionChip", "the primary action text", host => Add(host, Press("actionChip", "primary")),
            Button.ForegroundProperty, "Intent.Opinion.Approved.Text");
        yield return new("StagedStrip", "the staged change", host => Add(host, Box("stagedStrip")),
            Border.BackgroundProperty, "Intent.Change.Fill");
        yield return new("HoverReveal", "a hidden secondary action", RevealControl,
            Control.OpacityProperty, "Component.HoverReveal.HiddenOpacity");
        yield return new("HoverReveal", "the staged button height", host => Add(host, Press("revealControl", "revealButton")),
            Button.HeightProperty, "Component.HoverReveal.Height");
        yield return new("HoverReveal", "the FieldWorks link", host => Add(host, Press("revealControl", "revealLink")),
            Button.ForegroundProperty, "Intent.Marking.Link");
        yield return new("Density", "the compact page size", host => DensityText(host, normal: false),
            TextBlock.FontSizeProperty, "Component.Density.CompactType");
        yield return new("Density", "the compact opinion mark size", CompactOpinionMark,
            Control.HeightProperty, "Component.Density.CompactMarkSize");
        yield return new("Density", "the normal page size", host => DensityText(host, normal: true),
            TextBlock.FontSizeProperty, "Component.Density.NormalType");
        yield return new("HoverReveal", "a neutral staged button", host => Add(host, Press("revealControl", "revealButton")),
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
    }

    private static Button RevealControl(Panel host)
    {
        var button = Press("revealControl", "revealButton");
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

    private static T Add<T>(Panel host, T control) where T : Control
    {
        host.Children.Add(control);
        return control;
    }

    private static TextBlock Inside(Panel host, Border border)
    {
        var text = new TextBlock { Text = "3" };
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
