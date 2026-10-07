using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
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
            Segments = [new OutcomeSegment(Mark.Same, 3, "agree"), new OutcomeSegment(Mark.Of(MeaningTone.Problem), 1, "differ")],
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
        AssertStyled(() => new OutcomeBar { ShowLegend = false, Segments = [new OutcomeSegment(Mark.Same, 3, "agree")] },
            [new("the track", bar => Nth<Grid>(bar, 0), Grid.HeightProperty, "Component.OutcomeBar.CompactTrackHeight")]);

    [Fact]
    public void WordCardOpensUnderItsLineAndStacksItsSectionsInOrder()
    {
        var markup = File.ReadAllText(Path.Combine(AppDirectory(), "Views", "ResultsInTextPanel.axaml"));
        var cardMarkup = File.ReadAllText(Path.Combine(AppDirectory(), "Controls", "WordPresentation", "WordCard.axaml"));
        Assert.DoesNotContain("<Popup", markup, StringComparison.Ordinal);
        var start = markup.IndexOf("Content=\"{Binding OpenCard}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "The word card must open under its line.");
        var end = markup.IndexOf("</ContentControl>", start, StringComparison.Ordinal);
        Assert.True(end > start, "The word card's slot must have a closing element.");
        var card = markup[start..end];

        Assert.Contains("<word:WordCard Host=", card, StringComparison.Ordinal);
        Assert.Contains("HandlesTextReaderShortcuts=\"True\"", card, StringComparison.Ordinal);
        Assert.Contains("private async void OnKeyDown", File.ReadAllText(Path.Combine(AppDirectory(), "Controls", "WordPresentation", "WordCard.cs")), StringComparison.Ordinal);
        Assert.True(cardMarkup.IndexOf("In FieldWorks · now", StringComparison.Ordinal) <
                    cardMarkup.IndexOf("In PanGloss · now", StringComparison.Ordinal));
        Assert.True(cardMarkup.IndexOf("In PanGloss · now", StringComparison.Ordinal) <
                    cardMarkup.IndexOf("Text=\"What to do\"", StringComparison.Ordinal));
    }

    [Fact]
    public void HoverSummaryIsReadOnlyTextWithoutButtons()
    {
        var token = new ResultsTokenViewModel("Text", 1,
            new TextToken("word", "word", null, null), null);
        var markup = File.ReadAllText(Path.Combine(AppDirectory(), "Controls", "WordPresentation", "WordStripToken.axaml"));
        Assert.Equal("word · No analysis in FieldWorks · PanGloss: Not parsed yet", token.HoverSummary);
        Assert.Null(token.Actions);
        Assert.Contains("ToolTip.Tip=\"{Binding HoverSummary}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ToolTip", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StripAndCardOfferExplicitFixChoicesWithoutAnEmptyAnalysisSelector()
    {
        var markup = File.ReadAllText(Path.Combine(AppDirectory(), "Controls", "WordPresentation", "WordStripToken.axaml"));
        var cardMarkup = File.ReadAllText(Path.Combine(AppDirectory(), "Controls", "WordPresentation", "WordCard.axaml"));
        var view = XDocument.Parse(markup);
        Assert.DoesNotContain("Header=\"Fix ▾\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Fix ▾\"", markup, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Fix actions for this word\"", cardMarkup,
            StringComparison.Ordinal);
        var stripFix = Assert.Single(view.Descendants(), element => element.Name.LocalName == "Button" &&
            (string?)element.Attribute("AutomationProperties.Name") == "Fix actions from the word strip");
        var stripFixContent = Assert.Single(stripFix.Elements(), element => element.Name.LocalName == "StackPanel");
        Assert.Collection(stripFixContent.Elements(),
            label => Assert.Equal("Fix", (string?)label.Attribute("Text")),
            caret =>
            {
                Assert.Equal("Path", caret.Name.LocalName);
                Assert.Contains("actionChipCaret", ((string?)caret.Attribute("Classes") ?? "").Split(' '));
                Assert.Equal("M0 1 L5 6 L10 1 Z", (string?)caret.Attribute("Data"));
            });
        Assert.Contains("AutomationProperties.Name=\"Fix actions from the word strip\"", markup,
            StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Marking.FixChoices}\"", cardMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ComboBox", cardMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListBox", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void BulkActionsNameChosenTextsCountWordsAndExplainWordOnlyOpinions()
    {
        var markup = File.ReadAllText(Path.Combine(AppDirectory(), "Views", "ResultsInTextPanel.axaml"));
        var bulk = markup[..markup.IndexOf("<ScrollViewer Grid.Row=\"2\"", StringComparison.Ordinal)];
        Assert.Contains("Select all", bulk, StringComparison.Ordinal);
        Assert.Contains("all chosen Texts", bulk, StringComparison.Ordinal);
        Assert.Contains("Approve one analysis at a time", bulk, StringComparison.Ordinal);
        Assert.Contains("InText.AllCount", bulk, StringComparison.Ordinal);
    }

    [Fact]
    public void AFilterChipSpacesItsGlyphFromATokenLabel() =>
        AssertStyled(() => new FilterChip { Label = "Same", Count = 3, Mark = Mark.Same },
            [new("the chip's row", chip => Nth<Grid>(chip, 0), Grid.ColumnSpacingProperty, "Intent.Space.Snug")]);

    [Fact]
    public void AMarkChipSpacesItsGlyphFromAToken() =>
        AssertStyled(() => new MarkChip { Mark = Mark.Same },
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

    private static Border MorphBlock(Control row, int index) =>
        row.GetLogicalDescendants().OfType<Border>().Where(block => block.Classes.Contains("morph")).ElementAt(index);

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
