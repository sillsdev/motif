using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
            .Select(System.IO.Path.GetFileNameWithoutExtension)
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

    private static IEnumerable<Case> Cases()
    {
        yield return new("Text", "a muted line", host => Add(host, Text("muted")), TextBlock.ForegroundProperty, "Intent.TextMuted");
        yield return new("Text", "a stage title", host => Add(host, Text("stage-title")), TextBlock.FontSizeProperty, "Intent.Type.Title");
        yield return new("Text", "an error", host => Add(host, Text("error")), TextBlock.ForegroundProperty, "Intent.Danger");

        yield return new("Card", "a card", host => Add(host, Box("card")), Border.PaddingProperty, "Component.Card.Padding");
        yield return new("Card", "a card", host => Add(host, Box("card")), Border.CornerRadiusProperty, "Component.Card.Radius");
        yield return new("Card", "a card", host => Add(host, Box("card")), Border.BorderBrushProperty, "Intent.Border");

        yield return new("Notice", "a notice", host => Add(host, Box("notice")), Border.BackgroundProperty, "Intent.Warning.Fill");
        yield return new("Notice", "a notice", host => Add(host, Box("notice")), Border.PaddingProperty, "Component.Notice.Padding");

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

        yield return new("FilterChip", "a chip", host => Add(host, Press("filterChip")), Button.BackgroundProperty, "Intent.Clear");
        yield return new("FilterChip", "a chip", host => Add(host, Press("filterChip")), Button.MarginProperty, "Component.FilterChip.Margin");
        yield return new("FilterChip", "an active chip", host => Add(host, Press("filterChip", "active")),
            Button.ForegroundProperty, "Intent.Primary");

        yield return new("HeatCell", "a heat shade", host => Add(host, Box("heat")), Border.BackgroundProperty, "Intent.Warning");
        yield return new("MorphemeRow", "a morpheme edge", host => Add(host, Box("morphEdge")), Border.BorderBrushProperty, "Intent.Border");

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
        yield return new("TryWord", "a failed diagnostic", host => Add(host, Text("failed")),
            TextBlock.ForegroundProperty, "Intent.Danger");
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
}
