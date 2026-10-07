using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Diagnostics;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class UiFontResolutionTests(AvaloniaHeadlessFixture avalonia, ITestOutputHelper output)
{
    [Fact]
    public void DiagnoseAndikaResolution()
    {
        avalonia.Invoke(() =>
        {
            var manager = FontManager.Current;
            output.WriteLine($"DefaultFontFamily: {manager.DefaultFontFamily}");
            ReportTypeface(manager, "avares URI", new Typeface(new FontFamily("avares://SIL.Motif.App/Assets/Fonts#Andika")));
            ReportTypeface(manager, "bare Andika", new Typeface(new FontFamily("Andika")));
            ReportTypeface(manager, "registered collection", new Typeface(new FontFamily("fonts:Motif#Andika")));

            _ = manager.SystemFonts;
            var collections = typeof(FontManager).GetField("_fontCollections", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(manager) as IEnumerable;
            if (collections is null)
            {
                output.WriteLine("Font collections: registry unavailable");
                return;
            }

            foreach (var entry in collections)
            {
                var entryType = entry!.GetType();
                var collection = entryType.GetProperty("Value")?.GetValue(entry) as IFontCollection;
                if (collection is null) continue;

                var hasAndika = collection.TryGetGlyphTypeface("Andika", FontStyle.Normal, FontWeight.Normal,
                    FontStretch.Normal, out var collectionAndika);
                output.WriteLine($"Font collection: key={collection.Key}; type={collection.GetType().Name}; entries={collection.Count}; Andika={collectionAndika?.FamilyName ?? (hasAndika ? "<unknown>" : "<none>")}");
            }

            var andika = new FontFamily("fonts:Motif#Andika");
            AssertFace(manager, "Andika regular", new Typeface(andika));
            AssertFace(manager, "Andika bold", new Typeface(andika, FontStyle.Normal, FontWeight.Bold));
            AssertFace(manager, "Andika italic", new Typeface(andika, FontStyle.Italic, FontWeight.Normal));
            AssertFace(manager, "Andika semibold", new Typeface(andika, FontStyle.Normal, FontWeight.SemiBold));

            var textBlock = new TextBlock { Text = "Motif is a tech demo." };
            var button = new Button { Content = "Undo" };
            var wordCard = WordCardWithText();
            var window = new Window
            {
                Content = new StackPanel { Children = { textBlock, button, wordCard } },
                Width = 600,
                Height = 600,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                ReportTextBlockFontResolution(manager, textBlock);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TextBlocksButtonsAndWordCardsRenderWithAndikaGlyphs()
    {
        avalonia.Invoke(() =>
        {
            var textBlock = new TextBlock { Text = "Motif is a tech demo." };
            var button = new Button { Content = "Undo" };
            var wordCard = WordCardWithText();
            var window = new Window
            {
                Content = new StackPanel { Children = { textBlock, button, wordCard } },
                Width = 600,
                Height = 600,
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                ReportTextBlockFontResolution(FontManager.Current, textBlock);
                var application = Application.Current!;
                var uiFamily = Assert.IsType<FontFamily>(application.FindResource("Primitive.Font.UI"));
                Assert.Same(uiFamily, application.FindResource("DefaultFontFamily"));
                Assert.Same(uiFamily, application.FindResource("SemiFontFamilyRegular"));
                Assert.Equal(uiFamily, window.FontFamily);
                AssertAndika(textBlock);
                AssertAndika(Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Undo"));
                AssertAndika(Assert.Single(wordCard.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "word"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static WordCard WordCardWithText() => new()
    {
        Document = new WordCardDocument(new WordPresentationKey("font-card"), 0,
            [new WordCardText("Meaning", "word")]),
    };

    private static void AssertAndika(TextBlock text)
    {
        var shapedRuns = text.TextLayout.TextLines
            .SelectMany(line => line.TextRuns)
            .OfType<ShapedTextRun>()
            .ToArray();

        Assert.NotEmpty(shapedRuns);
        foreach (var run in shapedRuns)
        {
            var family = run.GlyphRun.GlyphTypeface.FamilyName;
            if (family.StartsWith("Andika", StringComparison.OrdinalIgnoreCase)) continue;

            var characters = string.Join(", ", run.Text.ToString().EnumerateRunes()
                .Select(rune => $"U+{rune.Value:X4} '{rune}'"));
            Assert.Fail($"Text '{text.Text}' rendered {characters} with {family}.");
        }
    }

    private void ReportTypeface(FontManager manager, string label, Typeface request)
    {
        var found = manager.TryGetGlyphTypeface(request, out var typeface);
        output.WriteLine($"TryGetGlyphTypeface({label}): {found}; FamilyName={typeface?.FamilyName ?? "<none>"}; TypographicFamilyName={typeface?.TypographicFamilyName ?? "<none>"}; Weight={typeface?.Weight.ToString() ?? "<none>"}; Style={typeface?.Style.ToString() ?? "<none>"}");
    }

    private void ReportTextBlockFontResolution(FontManager manager, TextBlock textBlock)
    {
        var property = TextElement.FontFamilyProperty;
        var diagnostic = textBlock.GetDiagnostic(property);
        var family = textBlock.GetValue(property) as FontFamily;
        var application = Application.Current!;
        var themeFamily = application.FindResource("ContentControlThemeFontFamily") as FontFamily;
        application.TryGetResource("DefaultFontFamily", application.ActualThemeVariant, out var defaultFontFamilyResource);
        var defaultFontFamily = defaultFontFamilyResource as FontFamily;
        output.WriteLine($"[Andika] TextBlock FontFamily: value={family}; priority={diagnostic.Priority}; diagnostic={diagnostic.Diagnostic}; ContentControlThemeFontFamily={themeFamily}; matchesThemeResource={Equals(family, themeFamily)}");

        foreach (var resourceKey in new[]
                 {
                     "ContentControlThemeFontFamily", "DefaultFontFamily", "SemiFontFamilyRegular",
                     "SemiFontFamilyMedium", "SemiFontFamilySemibold", "SemiFontFamilyBold",
                     "CodeFontFamily",
                 })
        {
            var resourceFound = application.TryGetResource(resourceKey, application.ActualThemeVariant, out var resource);
            output.WriteLine($"[Andika] Resource '{resourceKey}': found={resourceFound}; value={resource ?? "<none>"}");
        }

        foreach (var ancestor in textBlock.GetVisualAncestors().OfType<Control>().Reverse().Append(textBlock))
        {
            var ancestorDiagnostic = ancestor.GetDiagnostic(property);
            var ancestorFamily = ancestor.GetValue(property) as FontFamily;
            output.WriteLine($"[Andika] FontFamily ancestor {ancestor.GetType().Name}: value={ancestorFamily}; priority={ancestorDiagnostic.Priority}; matchesDefaultFontFamily={Equals(ancestorFamily, defaultFontFamily)}; diagnostic={ancestorDiagnostic.Diagnostic}");
        }

        var computedFamily = family ?? manager.DefaultFontFamily;
        var typeface = new Typeface(computedFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch);
        var typefaceFound = manager.TryGetGlyphTypeface(typeface, out var glyphTypeface);
        output.WriteLine($"[Andika] Computed Typeface: family={typeface.FontFamily}; style={typeface.Style}; weight={typeface.Weight}; stretch={typeface.Stretch}; resolved={typefaceFound}; glyphFamily={glyphTypeface?.FamilyName ?? "<none>"}");

        var culture = CultureInfo.CurrentUICulture;
        var characterMatched = manager.TryMatchCharacter('M', textBlock.FontStyle, textBlock.FontWeight,
            textBlock.FontStretch, computedFamily, culture, out var characterTypeface);
        var characterGlyphFamily = characterMatched && manager.TryGetGlyphTypeface(characterTypeface, out var characterGlyph)
            ? characterGlyph?.FamilyName ?? "<none>"
            : "<none>";
        output.WriteLine($"[Andika] TryMatchCharacter('M'): family={computedFamily}; culture={culture.Name}; matched={characterMatched}; typeface={characterTypeface.FontFamily}; glyphFamily={characterGlyphFamily}");

        var runIndex = 0;
        foreach (var run in textBlock.TextLayout.TextLines.SelectMany(line => line.TextRuns))
        {
            var runFamily = run is ShapedTextRun shaped ? shaped.GlyphRun.GlyphTypeface.FamilyName : "<no glyph run>";
            output.WriteLine($"[Andika] TextRun[{runIndex++}]: type={run.GetType().Name}; text='{run.Text.ToString()}'; glyphFamily={runFamily}");
        }
    }

    private void AssertFace(FontManager manager, string label, Typeface request)
    {
        var found = manager.TryGetGlyphTypeface(request, out var typeface);
        output.WriteLine($"{label}: {found}; FamilyName={typeface?.FamilyName ?? "<none>"}; Weight={typeface?.Weight.ToString() ?? "<none>"}; Style={typeface?.Style.ToString() ?? "<none>"}");
        Assert.True(found, $"{label} did not resolve.");
        Assert.StartsWith("Andika", typeface!.FamilyName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(request.Weight, typeface.Weight);
        Assert.Equal(request.Style, typeface.Style);
    }
}
