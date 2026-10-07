using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace SIL.Motif.App.Services;

public sealed partial class TextStyles
{
    private const string EmbeddedFontCollectionKey = "fonts:Motif";
    private const string EmbeddedFontAssets = "avares://SIL.Motif.App/Assets/Fonts";
    private const string AndikaFamilyName = EmbeddedFontCollectionKey + "#Andika";
    private static readonly FontFamily UiFontFamily = new(AndikaFamilyName);
    private static readonly FontFeatureCollection DefaultInterfaceFeatures =
        FontFeatureCollection.Parse("kern=1,liga=1,clig=1,calt=1");

    private static readonly IReadOnlyDictionary<string, string> LanguageFallbacks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["am"] = "Abyssinica SIL",
            ["ar"] = "Scheherazade New",
            ["as"] = "Noto Sans Bengali",
            ["ban"] = "Noto Sans Balinese",
            ["bn"] = "Noto Sans Bengali",
            ["bo"] = "Noto Sans Tibetan",
            ["dz"] = "Noto Sans Tibetan",
            ["el"] = "Gentium",
            ["fa"] = "Scheherazade New",
            ["gu"] = "Noto Sans Gujarati",
            ["he"] = "Ezra SIL",
            ["hi"] = "Annapurna SIL",
            ["hy"] = "Noto Sans Armenian",
            ["ii"] = "Nuosu SIL",
            ["ja"] = "Noto Sans CJK JP",
            ["jv"] = "Noto Sans Javanese",
            ["ka"] = "Noto Sans Georgian",
            ["km"] = "Mondulkiri",
            ["kn"] = "Noto Sans Kannada",
            ["ko"] = "Noto Sans CJK KR",
            ["lo"] = "Noto Sans Lao",
            ["ml"] = "Noto Sans Malayalam",
            ["mn"] = "Noto Sans Mongolian",
            ["mr"] = "Annapurna SIL",
            ["my"] = "Padauk",
            ["ne"] = "Annapurna SIL",
            ["or"] = "Noto Sans Oriya",
            ["pa"] = "Noto Sans Gurmukhi",
            ["ps"] = "Scheherazade New",
            ["sa"] = "Annapurna SIL",
            ["si"] = "Noto Sans Sinhala",
            ["su"] = "Noto Sans Sundanese",
            ["ta"] = "Noto Sans Tamil",
            ["te"] = "Noto Sans Telugu",
            ["th"] = "Noto Sans Thai",
            ["ti"] = "Abyssinica SIL",
            ["ur"] = "Scheherazade New",
        };

    private static readonly IReadOnlyDictionary<string, string[]> UnicodeRangesByFamily =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Abyssinica SIL"] = ["U+1200-137F", "U+1380-139F", "U+2D80-2DDF", "U+AB00-AB2F", "U+1E7E0-1E7FF"],
            ["Annapurna SIL"] = ["U+0900-097F", "U+1CD0-1CFF", "U+A8E0-A8FF"],
            ["Ezra SIL"] = ["U+0590-05FF", "U+FB1D-FB4F"],
            ["Gentium"] = ["U+0370-03FF", "U+1F00-1FFF"],
            ["Mondulkiri"] = ["U+1780-17FF", "U+19E0-19FF"],
            ["Noto Sans Armenian"] = ["U+0530-058F", "U+FB13-FB17"],
            ["Noto Sans Balinese"] = ["U+1B00-1B7F"],
            ["Noto Sans Bengali"] = ["U+0980-09FF"],
            ["Noto Sans CJK HK"] = CjkHanAndPunctuation(),
            ["Noto Sans CJK JP"] = CjkHanAndPunctuation().Concat(["U+3040-30FF", "U+31F0-31FF"]).ToArray(),
            ["Noto Sans CJK KR"] = CjkHanAndPunctuation().Concat(["U+1100-11FF", "U+3130-318F", "U+AC00-D7AF"]).ToArray(),
            ["Noto Sans CJK SC"] = CjkHanAndPunctuation().Concat(["U+3100-312F", "U+31A0-31BF"]).ToArray(),
            ["Noto Sans CJK TC"] = CjkHanAndPunctuation().Concat(["U+3100-312F", "U+31A0-31BF"]).ToArray(),
            ["Noto Sans Gujarati"] = ["U+0A80-0AFF"],
            ["Noto Sans Gurmukhi"] = ["U+0A00-0A7F"],
            ["Noto Sans Georgian"] = ["U+10A0-10FF", "U+1C90-1CBF", "U+2D00-2D2F"],
            ["Noto Sans Javanese"] = ["U+A980-A9DF"],
            ["Noto Sans Kannada"] = ["U+0C80-0CFF"],
            ["Noto Sans Lao"] = ["U+0E80-0EFF"],
            ["Noto Sans Malayalam"] = ["U+0D00-0D7F"],
            ["Noto Sans Mongolian"] = ["U+1800-18AF", "U+11660-1167F"],
            ["Noto Sans Oriya"] = ["U+0B00-0B7F"],
            ["Noto Sans Sinhala"] = ["U+0D80-0DFF"],
            ["Noto Sans Sundanese"] = ["U+1B80-1BBF", "U+1CC0-1CCF"],
            ["Noto Sans Tamil"] = ["U+0B80-0BFF"],
            ["Noto Sans Telugu"] = ["U+0C00-0C7F"],
            ["Noto Sans Thai"] = ["U+0E00-0E7F"],
            ["Noto Sans Tibetan"] = ["U+0F00-0FFF"],
            ["Nuosu SIL"] = ["U+A000-A4CF"],
            ["Padauk"] = ["U+1000-109F", "U+A9E0-A9FF", "U+AA60-AA7F"],
            ["Scheherazade New"] = ["U+0600-06FF", "U+0750-077F", "U+08A0-08FF", "U+FB50-FDFF", "U+FE70-FEFF"],
        };

    private static double InterfaceFontSize
    {
        get
        {
            var application = Application.Current ??
                throw new InvalidOperationException("Interface typography requires the App's token resources.");
            if (application.TryGetResource("Intent.Type.Body", application.ActualThemeVariant, out var value) &&
                value is double size && double.IsFinite(size) && size > 0)
                return size;
            throw new InvalidOperationException("The Intent.Type.Body typography token is unavailable.");
        }
    }

    internal static FontFamily InterfaceFontFamily => UiFontFamily;
    internal static string InterfaceFontFamilyName => AndikaFamilyName;
    internal static FontManagerOptions CurrentInterfaceFontOptions =>
        InterfaceFontOptionsForLanguage(System.Globalization.CultureInfo.CurrentUICulture.Name);

    internal static void RegisterUiFontCollection(FontManager fontManager) =>
        fontManager.AddFontCollection(new EmbeddedFontCollection(
            new Uri(EmbeddedFontCollectionKey, UriKind.Absolute),
            new Uri(EmbeddedFontAssets, UriKind.Absolute)));

    internal static void ApplyApplicationFontResources(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var family = application.FindResource("Primitive.Font.UI") as FontFamily ??
            throw new InvalidOperationException("The Primitive.Font.UI token is not a font family.");
        application.Resources["DefaultFontFamily"] = family;
        application.Resources["ContentControlThemeFontFamily"] = family;
        application.Resources["SemiFontFamilyRegular"] = family;
    }

    internal static FontManagerOptions InterfaceFontOptionsForLanguage(string? language)
    {
        var family = FallbackFamilyForLanguage(language);
        var fallbacks = family is null
            ? Array.Empty<FontFallback>()
            : UnicodeRangesByFamily[family].Select(range => new FontFallback
            {
                FontFamily = new FontFamily(family),
                UnicodeRange = UnicodeRange.Parse(range),
            }).ToArray();
        return new FontManagerOptions { DefaultFamilyName = AndikaFamilyName, FontFallbacks = fallbacks };
    }

    private static IReadOnlyList<string> UiFallbackFamilies(string? language)
    {
        var family = FallbackFamilyForLanguage(language);
        return family is null ? Array.Empty<string>() : [family];
    }

    private static string? FallbackFamilyForLanguage(string? language)
    {
        var subtags = language?.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries) ?? [];
        var languageCode = subtags.FirstOrDefault();
        if (languageCode?.Equals("zh", StringComparison.OrdinalIgnoreCase) == true)
            return ChineseFallback(subtags);
        if (languageCode?.Equals("pa", StringComparison.OrdinalIgnoreCase) == true &&
            subtags.Any(subtag => subtag.Equals("PK", StringComparison.OrdinalIgnoreCase)))
            return "Scheherazade New";
        return LanguageFallbacks.GetValueOrDefault(languageCode ?? "");
    }

    private static string ChineseFallback(string[] subtags)
    {
        if (subtags.Any(subtag => subtag.Equals("HK", StringComparison.OrdinalIgnoreCase)) ||
            subtags.Any(subtag => subtag.Equals("MO", StringComparison.OrdinalIgnoreCase)))
            return "Noto Sans CJK HK";
        if (subtags.Any(subtag => subtag.Equals("Hant", StringComparison.OrdinalIgnoreCase)) ||
            subtags.Any(subtag => subtag.Equals("TW", StringComparison.OrdinalIgnoreCase)))
            return "Noto Sans CJK TC";
        return "Noto Sans CJK SC";
    }

    private static string[] CjkHanAndPunctuation() =>
    [
        "U+3000-303F", "U+3400-4DBF", "U+4E00-9FFF", "U+F900-FAFF",
        "U+20000-2EE5F", "U+30000-323AF",
    ];
}
