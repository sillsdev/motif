using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the controls a view builds in code take their gaps, sizes and type from design tokens through
/// component styles, in the light and the dark theme, rather than from values set on the control; and that every
/// Component key a view names resolves in both themes. The token gate reads keys by name and layer only, so
/// whether a named key exists is checked here.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ViewTokenTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public ViewTokenTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    private sealed record Case(string What, Func<Control, Control> Find, AvaloniaProperty Property, string Key);

    [Fact]
    public void AnOutcomeBarTakesItsGapsAndSizesFromTokens()
    {
        AssertStyled(() => new OutcomeBar
        {
            Segments = [new OutcomeSegment(Verdict.Agrees, 3, "agree"), new OutcomeSegment(Verdict.Differs, 1, "differ")],
        },
        [
            new("the bar", bar => bar, StackPanel.SpacingProperty, "Intent.Space.Related"),
            new("the track", bar => Nth<Grid>(bar, 0), Grid.HeightProperty, "Component.OutcomeBar.TrackHeight"),
            new("the track", bar => Nth<Grid>(bar, 0), Grid.ColumnSpacingProperty, "Intent.Space.Minimal"),
            new("a part", bar => Nth<Border>(Nth<Grid>(bar, 0), 0), Border.CornerRadiusProperty, "Component.OutcomeBar.PartRadius"),
            new("a swatch", Swatch, Border.WidthProperty, "Component.OutcomeBar.SwatchSize"),
            new("a swatch", Swatch, Border.HeightProperty, "Component.OutcomeBar.SwatchSize"),
            new("a swatch", Swatch, Border.CornerRadiusProperty, "Component.OutcomeBar.SwatchRadius"),
            new("a swatch", Swatch, Border.MarginProperty, "Component.OutcomeBar.SwatchMargin"),
            new("a legend entry", LegendEntry, StackPanel.MarginProperty, "Component.OutcomeBar.LegendEntryMargin"),
            new("a legend count", bar => Nth<CopyableTextBlock>(LegendEntry(bar), 0), TextBlock.MarginProperty,
                "Component.OutcomeBar.LegendTextMargin"),
        ]);
    }

    [Fact]
    public void AnOutcomeBarWithoutItsLegendTakesTheCompactTrackHeight() =>
        AssertStyled(() => new OutcomeBar { ShowLegend = false, Segments = [new OutcomeSegment(Verdict.Agrees, 3, "agree")] },
            [new("the track", bar => Nth<Grid>(bar, 0), Grid.HeightProperty, "Component.OutcomeBar.CompactTrackHeight")]);

    [Fact]
    public void AMorphemeRowTakesItsGapsAndTypeFromTokens()
    {
        AssertStyled(() => new MorphemeRow
        {
            Separators = true,
            Morphs =
            [
                new ParserReadingMorphViewModel(new ParserReadingMorph("kitab", "book", "n", null, false, null)),
                new ParserReadingMorphViewModel(new ParserReadingMorph("-u", "his", "poss", null, false, "silfw://link")),
            ],
        },
        [
            new("a form", row => Nth<CopyableTextBlock>(row, 0), TextBlock.FontSizeProperty, "Intent.Type.Body"),
            new("a gloss", row => Nth<CopyableTextBlock>(row, 1), TextBlock.FontSizeProperty, "Intent.Type.Small"),
            new("a category", row => Nth<CopyableTextBlock>(row, 2), TextBlock.FontSizeProperty, "Intent.Type.Label"),
            new("a linked form", row => Nth<HyperlinkButton>(row, 0), HyperlinkButton.FontSizeProperty, "Intent.Type.Body"),
            new("a linked gloss", row => Nth<HyperlinkButton>(row, 1), HyperlinkButton.FontSizeProperty, "Intent.Type.Small"),
            new("a block", row => Nth<Border>(row, 0), Border.PaddingProperty, "Component.MorphemeRow.BlockPadding"),
            new("a block", row => Nth<Border>(row, 0), Border.MarginProperty, "Component.MorphemeRow.BlockMargin"),
            new("a block", row => Nth<Border>(row, 0), Border.BorderThicknessProperty, "Intent.Stroke.DividerEnd"),
            new("the last block", row => Nth<Border>(row, 1), Border.PaddingProperty, "Intent.Inset.None"),
            new("the last block", row => Nth<Border>(row, 1), Border.MarginProperty, "Component.MorphemeRow.LastBlockMargin"),
        ]);
    }

    [Fact]
    public void AWordCardCanHideFieldWorksLinksUntilHoverOrKeyboardFocus()
    {
        var row = new MorphemeRow
        {
            RevealLinks = true,
            Morphs = [new ParserReadingMorphViewModel(new ParserReadingMorph(
                "form", "gloss", "n", null, false, "silfw://entry"))],
        };
        var links = row.GetLogicalDescendants().OfType<HyperlinkButton>().ToArray();

        Assert.Equal(2, links.Length);
        Assert.All(links, link =>
        {
            Assert.Contains("revealControl", link.Classes);
            Assert.True(link.Focusable);
        });
    }

    [Fact]
    public void AFilterChipSpacesItsGlyphFromATokenLabel() =>
        AssertStyled(() => new FilterChip { Label = "Agrees", Count = 3, Verdict = Verdict.Agrees },
            [new("the chip's row", chip => Nth<StackPanel>(chip, 0), StackPanel.SpacingProperty, "Intent.Space.Snug")]);

    [Fact]
    public void AVerdictChipSpacesItsGlyphFromAToken() =>
        AssertStyled(() => new VerdictChip { Verdict = Verdict.Agrees },
            [new("the chip's row", chip => Nth<StackPanel>(chip, 0), StackPanel.SpacingProperty, "Intent.Space.Compact")]);

    [Fact]
    public void EveryComponentKeyAViewNamesResolvesInBothThemeVariants()
    {
        var views = Path.Combine(AppDirectory(), "Views");
        var keys = Directory.EnumerateFiles(views, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".axaml", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\b(Component\.[A-Za-z]+\.[A-Za-z.]+)").Select(match => match.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.Contains("Component.Shell.Width", keys);

        _avalonia.Invoke(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var key in keys)
                Assert.True(Application.Current!.TryGetResource(key, variant, out var value) && value is not null,
                    $"{key} is missing in {variant}.");
        });
    }

    private void AssertStyled(Func<Control> build, Case[] cases)
    {
        var failures = new List<string>();
        _avalonia.Invoke(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var control = build();
                var window = new Window { Content = control, RequestedThemeVariant = variant, Width = 800, Height = 400 };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    foreach (var item in cases)
                    {
                        var target = item.Find(control);
                        Assert.True(Application.Current!.TryGetResource(item.Key, variant, out var expected), $"{item.Key} is missing.");
                        var actual = target.GetValue(item.Property);
                        var priority = target.GetDiagnostic(item.Property).Priority;
                        if (!Equals(expected, actual) || priority == BindingPriority.LocalValue)
                            failures.Add($"{variant} {item.What}: {item.Property.Name} is {actual} ({priority}), not {item.Key} ({expected}) from a style.");
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

    private static Control Swatch(Control bar) =>
        LegendEntry(bar).GetLogicalChildren().OfType<Border>().First();

    private static StackPanel LegendEntry(Control bar) => Nth<StackPanel>(Nth<WrapPanel>(bar, 0), 0);

    private static T Nth<T>(Control root, int index) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Where(control => control != root).ElementAt(index);

    private static string AppDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "SIL.Motif.App");
    }
}
