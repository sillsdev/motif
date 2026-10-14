using Avalonia.Media;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsFontSourceTests
{
    [Theory]
    [InlineData("Andika", "Andika")]
    [InlineData("Charis SIL", "Charis SIL")]
    [InlineData("Charis", "Charis SIL")]
    [InlineData("Doulos SIL", "Doulos SIL")]
    [InlineData("Gentium Plus", "Gentium Plus")]
    public void ShippedWritingSystemFontsIdentifyTheirActualSource(string requested, string bundled)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (row, style) = Resolve(requested);
            Assert.True(style.RequestedFontInstalled);
            var installed = FontManager.Current.SystemFonts.Any(font =>
                (font.Name.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                 font.Name.Equals(bundled, StringComparison.OrdinalIgnoreCase) ||
                 bundled == "Charis SIL" && font.Name.Equals("Charis", StringComparison.OrdinalIgnoreCase)) &&
                FontManager.Current.TryGetGlyphTypeface(new Typeface(font), out var face) &&
                string.Equals(font.Name, face!.FamilyName, StringComparison.OrdinalIgnoreCase));
            if (installed)
                Assert.Equal("Installed", row.FontSourceText);
            else
            {
                Assert.Equal("Bundled with Motif", row.FontSourceText);
            }
            Assert.False(row.IsFontMissing);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(style.FontFamily!), out var face));
            if (bundled == "Charis SIL") Assert.Contains(face!.FamilyName, new[] { "Charis SIL", "Charis" });
            else Assert.Equal(bundled, face!.FamilyName);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AnInstalledSystemFontIsIdentifiedAsInstalled()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var family = FontManager.Current.SystemFonts.First(font =>
                !TextStyles.TryGetBundledWritingSystemFont(font.Name, out _, out _) &&
                FontManager.Current.TryGetGlyphTypeface(new Typeface(font), out var face) &&
                string.Equals(font.Name, face!.FamilyName, StringComparison.OrdinalIgnoreCase));
            var (row, style) = Resolve(family.Name);
            Assert.True(style.RequestedFontInstalled);
            Assert.Equal("Installed", row.FontSourceText);
            Assert.False(row.IsFontMissing);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(style.FontFamily!), out var face));
            Assert.Equal(family.Name, face!.FamilyName, StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("Motif Missing Test Font")]
    [InlineData("fonts:Motif#Motif Missing Test Font")]
    public void AnUnavailableFontIsFallbackEvenWhenItsRequestNamesTheMotifCollection(string requested)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (row, style) = Resolve(requested);
            Assert.False(style.RequestedFontInstalled);
            Assert.Equal("Fallback", row.FontSourceText);
            Assert.True(row.IsFontMissing);
            Assert.Contains(requested, row.MissingFontFamilies);
            Assert.Contains(requested, row.MissingFontCaption);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    private static (WritingSystemSettingsRow Row, ResolvedTextStyle Style) Resolve(string family)
    {
        var system = new WritingSystemDisplay("qaa-x-source", "Font source", "F", WritingSystemKind.Vernacular,
            0, true, family, string.Empty, false, new Dictionary<string, double> { ["Normal"] = 12 })
        {
            StyleFonts = new Dictionary<string, WritingSystemStyleFont> { ["Normal"] = new(family, string.Empty) },
        };
        var resolver = new TextStyles();
        resolver.ReplaceContext([system]);
        return (new WritingSystemSettingsRow(system, resolver), resolver.Resolve(TextStyleRequest.Linguistic(system.Id)));
    }
}
