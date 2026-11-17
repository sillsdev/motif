using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Round 2, the window-wide look: text a person reads must read as text, a disabled button must look disabled,
/// one button class carries the main action, and a sentence is written as a sentence.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class VisualRound2Tests(AvaloniaHeadlessFixture avalonia)
{
    private const double Text = 4.5;
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    // Every text brush a page reads with, and every background the window lays them on.
    private static readonly string[] TextBrushes = ["Intent.Text", "Intent.TextSecondary", "Intent.TextMuted"];
    private static readonly string[] Backgrounds = ["Intent.Surface", "Intent.Surface.Subtle", "Intent.Surface.Raised", "Intent.Neutral.Fill"];

    [Fact]
    public void EveryTextBrushMeetsAaOnEveryWindowBackgroundInBothThemes()
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            foreach (var background in Backgrounds)
            {
                var floor = Flatten(Resolve(background, theme), Colors.Transparent, theme);
                foreach (var brush in TextBrushes)
                {
                    var ratio = Ratio(Flatten(Resolve(brush, theme), floor, theme), floor);
                    if (ratio < Text) failures.Add($"{theme} {brush} on {background}: {ratio:F2}:1, needs {Text}:1");
                }
            }
            foreach (var theme in Themes)
            {
                var selected = Flatten(Resolve("Intent.Selected.Fill", theme), Colors.Transparent, theme);
                var ratio = Ratio(Flatten(Resolve("Intent.TextMutedOnSelected", theme), selected, theme), selected);
                if (theme == ThemeVariant.Light && ratio < Text) failures.Add($"{theme} Intent.TextMutedOnSelected on Intent.Selected.Fill: {ratio:F2}:1");
            }
        });
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // A rendered helper sentence (the .muted class every page uses) reads at AA, not only its brush.
    [Fact]
    public void ARenderedMutedSentenceMeetsAaInBothThemes()
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var text = new TextBlock { Text = "Approve stages the chosen reading at the checked places.", Classes = { "muted" } };
                var window = new MotifTestWindow { Content = new Border { Child = text }, RequestedThemeVariant = theme, Width = 400, Height = 100 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var ratio = ContrastTests.Effective(text);
                    Assert.True(ratio >= Text, $"{theme} muted sentence: {ratio:F2}:1");
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void ADisabledButtonLooksClearlyDifferentFromAnEnabledOne()
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            foreach (var @class in new[] { "", "occurrenceAction", "primary" })
            {
                var enabled = Make("Ask Claude", true, @class);
                var disabled = Make("Ask Claude", false, @class);
                var window = new MotifTestWindow
                {
                    Content = new StackPanel { Children = { enabled, disabled } },
                    RequestedThemeVariant = theme, Width = 300, Height = 200,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var on = ContrastTests.Effective(Label(enabled));
                    var off = ContrastTests.Effective(Label(disabled));
                    var what = $"{theme} '{@class}' button";
                    if (on < Text) failures.Add($"{what}: enabled label {on:F2}:1, needs {Text}:1");
                    if (off > 3.0) failures.Add($"{what}: disabled label still {off:F2}:1, needs 3:1 or less");
                    if (on - off < 2.0) failures.Add($"{what}: enabled {on:F2}:1 and disabled {off:F2}:1 are under 2 apart");
                    if (Face(enabled).Equals(Face(disabled))) failures.Add($"{what}: the face is painted the same enabled and disabled");
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
    public void ThePrimaryButtonIsFilledAndItsLabelReadsInBothThemes()
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            foreach (var classes in new[] { "primary", "primary writes-fieldworks" })
            {
                var button = Make("Apply", true, classes);
                var plain = Make("Cancel", true, "");
                var window = new MotifTestWindow
                {
                    Content = new StackPanel { Children = { button, plain } },
                    RequestedThemeVariant = theme, Width = 300, Height = 200,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var ratio = ContrastTests.Effective(Label(button));
                    if (ratio < Text) failures.Add($"{theme} '{classes}': label {ratio:F2}:1");
                    if (Face(button).Equals(Face(plain))) failures.Add($"{theme} '{classes}': looks like a plain button");
                }
                finally
                {
                    window.Close();
                }
            }
        });
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Button Make(string content, bool enabled, string classes)
    {
        var button = new Button { Content = content, IsEnabled = enabled };
        foreach (var @class in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) button.Classes.Add(@class);
        return button;
    }

    private static TextBlock Label(Button button) => button.GetVisualDescendants().OfType<TextBlock>().First();

    private static Color Face(Button button)
    {
        var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First();
        return presenter.Background is ISolidColorBrush solid ? solid.Color : Colors.Transparent;
    }

    // Title Case words in a sentence are allowed for the product's own terms, which are written with capitals.
    private static readonly HashSet<string> Terms = new(StringComparer.Ordinal)
    {
        "Motif", "FieldWorks", "PanGloss", "Paratext", "Claude", "ChatGPT", "Codex", "AI", "Text", "Texts", "Selection",
        "Proposal", "Proposals", "Source", "Sources", "Passage", "Trial", "Request", "Requests", "Handoff", "Diagnostics",
        "Settings", "Dry", "Run", "Difference", "Parser", "Steps", "Words", "Word", "Not", "A",
    };

    // Product-term sentences and lists, which stay as written.
    private static readonly HashSet<string> NotThisRound = new(StringComparer.Ordinal)
    {
        "MotifApp.ChatGptInstructions", "MotifApp.TraceWritingSystemsSummaryFormat",
        "MotifApp.RefusalHandoffCancelled", "MotifApp.AssistantsNotFound", "MotifApp.SourcesFieldWorksExplanation",
    };

    private static bool OwnedByAnotherSlice(string key) =>
        key.StartsWith("MotifApp.Diagnostics", StringComparison.Ordinal)
        || key.StartsWith("MotifApp.ProposalDocument", StringComparison.Ordinal)
        || key.StartsWith("MotifApp.ProposalEvidence", StringComparison.Ordinal)
        || key == "MotifApp.ProposalActivateForApply";

    [Fact]
    public void NoSentenceInTheResxIsWrittenInTitleCase()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "SIL.Motif.App", "MotifAppStrings.resx");
        var offenders = new List<string>();
        foreach (var data in XDocument.Load(path).Root!.Elements("data"))
        {
            var key = (string)data.Attribute("name")!;
            var value = data.Element("value")!.Value;
            if (NotThisRound.Contains(key) || OwnedByAnotherSlice(key)) continue;
            if (IsTitleCaseSentence(value)) offenders.Add($"{key}: {value}");
        }
        Assert.True(offenders.Count == 0, "Sentences written in Title Case:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Theory]
    [InlineData("Diagnostics Are Advice Only. Read and Edit Rules in FieldWorks.", true)]
    [InlineData("Check Two Proposals to Compare Their Evidence.", true)]
    [InlineData("Choose a reading to keep.", false)]
    [InlineData("Approve stages the chosen reading at the checked Texts.", false)]
    [InlineData("Parser Steps", false)]
    [InlineData("Not a Word", false)]
    public void TheTitleCaseSentenceRuleTellsASentenceFromALabel(string value, bool expected) =>
        Assert.Equal(expected, IsTitleCaseSentence(value));

    private static bool IsTitleCaseSentence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.EndsWith('.')) return false;
        var words = Regex.Split(trimmed, @"\s+").Where(word => Regex.IsMatch(word, "^[A-Za-z]")).ToArray();
        if (words.Length < 5) return false;
        var capitalised = words.Count(word => char.IsUpper(word[0]) && !Terms.Contains(word.Trim('.', ',', ':', ';', '?', '(', ')')));
        var counted = words.Count(word => !Terms.Contains(word.Trim('.', ',', ':', ';', '?', '(', ')')));
        // Sentence case capitalises the first word and little else; Title Case capitalises most of the rest.
        return counted > 0 && capitalised * 2 > counted + 1;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Motif.sln not found above the test output.");
    }

    // Parser work is named Parser Steps everywhere, and a person's approval is Approve, never Add.
    [Fact]
    public void OneWordNamesOneThing()
    {
        var offenders = new List<string>();
        var path = Path.Combine(FindRepoRoot(), "src", "SIL.Motif.App", "MotifAppStrings.resx");
        foreach (var data in XDocument.Load(path).Root!.Elements("data"))
        {
            var key = (string)data.Attribute("name")!;
            var value = data.Element("value")!.Value;
            if (!OwnedByAnotherSlice(key) && Regex.IsMatch(value, "parser work", RegexOptions.IgnoreCase)) offenders.Add($"{key}: {value}");
        }
        Assert.True(offenders.Count == 0, "Say Parser Steps, not parser work:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
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

    // Lays a colour with alpha over what is beneath it, then over the window's white or black.
    private static Color Flatten(Color top, Color beneath, ThemeVariant theme)
    {
        var window = theme == ThemeVariant.Dark ? Colors.Black : Colors.White;
        var floor = beneath.A == 0 ? window : beneath;
        var a = top.A / 255d;
        byte Mix(byte over, byte under) => (byte)Math.Round(over * a + under * (1 - a));
        return Color.FromRgb(Mix(top.R, floor.R), Mix(top.G, floor.G), Mix(top.B, floor.B));
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
}
