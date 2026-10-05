using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>Resolves the saved FieldWorks display settings into the Avalonia values used for language text.</summary>
public sealed class WritingSystemTextStyleResolver
{
    private static readonly string[] MotifFallbacks = ["Noto Sans", "Segoe UI", "Arial", "sans-serif"];
    private static readonly Regex OpenTypeFeature = new("^[A-Za-z0-9]{4}=-?[0-9]+$", RegexOptions.Compiled);
    private FontManager? _fontManager;
    private readonly Dictionary<string, WritingSystemDisplay> _systems = new(StringComparer.Ordinal);
    private readonly Dictionary<(string? WritingSystem, string Style), WritingSystemTextStyle> _cache = [];
    private readonly HashSet<string> _noticedMissingFonts = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _fallbackFamilies = MotifFallbacks;

    /// <summary>The first requested font failure for each writing system drawn by this resolver.</summary>
    public ObservableCollection<WritingSystemFontNotice> MissingFontNotices { get; } = [];

    /// <summary>Creates a resolver backed by Avalonia's current font manager.</summary>
    public WritingSystemTextStyleResolver()
    {
    }

    internal WritingSystemTextStyleResolver(FontManager fontManager) =>
        _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));

    private FontManager CurrentFontManager => _fontManager ??= FontManager.Current;

    /// <summary>Replaces the writing-system records captured with the open project's Baseline.</summary>
    public void SetWritingSystems(IReadOnlyList<WritingSystemDisplay> writingSystems)
    {
        ArgumentNullException.ThrowIfNull(writingSystems);
        _systems.Clear();
        _noticedMissingFonts.Clear();
        MissingFontNotices.Clear();
        foreach (var writingSystem in writingSystems)
            _systems.TryAdd(writingSystem.Id, writingSystem);
        _cache.Clear();
    }

    /// <summary>The current default vernacular id, if the Baseline recorded one.</summary>
    public string? DefaultVernacularId => _systems.Values
        .Where(system => system.Kind == WritingSystemKind.Vernacular && system.IsDefault)
        .Select(system => system.Id)
        .FirstOrDefault();

    /// <summary>Gets the style for a writing system and a FieldWorks style name such as Normal or Paragraph.</summary>
    public WritingSystemTextStyle Resolve(string? writingSystemId, string? styleName = null)
    {
        var requestedStyle = string.IsNullOrWhiteSpace(styleName) ? "Normal" : styleName;
        _systems.TryGetValue(writingSystemId ?? string.Empty, out var writingSystem);
        var style = writingSystem is not null &&
            (writingSystem.StyleSizes.ContainsKey(requestedStyle) || writingSystem.StyleFonts.ContainsKey(requestedStyle))
                ? requestedStyle : "Normal";
        var key = (writingSystemId, style);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var sizePoints = 0d;
        writingSystem?.StyleSizes.TryGetValue(style, out sizePoints);
        var size = sizePoints > 0 ? sizePoints : 10d;
        var styleFont = writingSystem?.StyleFonts.GetValueOrDefault(style);
        var requestedFamily = string.IsNullOrWhiteSpace(styleFont?.FontFamily)
            ? writingSystem?.FontFamily
            : styleFont.FontFamily;
        var featuresText = styleFont?.FontFeatures ?? writingSystem?.FontFeatures;
        string?[] familyNames = [requestedFamily, .. _fallbackFamilies];
        var family = new FontFamily(string.Join(", ",
            familyNames.Where(name => !string.IsNullOrWhiteSpace(name))));
        var features = ParseOpenTypeFeatures(featuresText);
        var result = new WritingSystemTextStyle(
            family,
            features,
            writingSystem?.RightToLeft == true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            size * 96d / 72d,
            writingSystem?.Id,
            writingSystem?.Name ?? writingSystem?.Id ?? string.Empty,
            requestedFamily,
            IsInstalled(requestedFamily));
        _cache.Add(key, result);
        return result;
    }

    internal static WritingSystemTextStyleResolver FromTrace(IReadOnlyList<TraceWritingSystem> writingSystems)
    {
        var resolver = new WritingSystemTextStyleResolver();
        resolver.SetWritingSystems(writingSystems.Select((system, index) =>
        {
            var rtl = string.Equals(system.Direction, "rtl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(system.Direction, "right-to-left", StringComparison.OrdinalIgnoreCase);
            var sizes = system.StyleSizes is { Count: > 0 } ? system.StyleSizes :
                new Dictionary<string, double>(StringComparer.Ordinal) { ["Normal"] = 10d };
            var fonts = system.StyleFonts ?? new Dictionary<string, WritingSystemStyleFont>(StringComparer.Ordinal);
            return new WritingSystemDisplay(
                system.Id, system.Name ?? system.Id, system.Id,
                system.IsVernacular ? WritingSystemKind.Vernacular : WritingSystemKind.Analysis,
                index, system.IsDefault, system.Font ?? string.Empty, system.FontFeatures ?? string.Empty,
                rtl, sizes)
            {
                StyleFonts = fonts,
            };
        }).ToArray());
        return resolver;
    }

    /// <summary>Finds the first installed fallback family that can draw text in a missing requested font.</summary>
    public string? FindFallbackFamily(string? writingSystemId, string? styleName, string? text)
    {
        var style = Resolve(writingSystemId, styleName);
        if (style.RequestedFontInstalled || string.IsNullOrWhiteSpace(style.RequestedFontFamily)) return null;
        string?[] familyNames = [style.RequestedFontFamily, .. _fallbackFamilies];
        var family = new FontFamily(string.Join(", ", familyNames));
        var culture = CultureFor(writingSystemId);
        if (text is not null)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune)) continue;
                if (CurrentFontManager.TryMatchCharacter(rune.Value, FontStyle.Normal, FontWeight.Normal,
                        FontStretch.Normal, family, culture, out var matched))
                    return matched.FontFamily.Name;
            }
        }
        return "sans-serif";
    }

    /// <summary>Applies a resolved style to one language-text control.</summary>
    public void Apply(Control control, string? writingSystemId, string? styleName)
    {
        ArgumentNullException.ThrowIfNull(control);
        var style = Resolve(writingSystemId, styleName);
        control.SetValue(TextElement.FontFamilyProperty, style.FontFamily);
        control.SetValue(TextElement.FontFeaturesProperty, style.FontFeatures);
        control.SetValue(TextElement.FontSizeProperty, style.FontSize);
        control.SetValue(Visual.FlowDirectionProperty, style.FlowDirection);
        RegisterMissingFontNotice(control, style, styleName, TextOf(control));
    }

    /// <summary>Sets the direction of a running-text layout from its language's saved settings.</summary>
    public void ApplyFlowDirection(Control control, string? writingSystemId, string? styleName = null)
    {
        var direction = Resolve(writingSystemId, styleName).FlowDirection;
        if (control is RunningTextPanel runningText)
        {
            runningText.TextDirection = direction;
            control.SetValue(Visual.FlowDirectionProperty, FlowDirection.LeftToRight);
            return;
        }
        control.SetValue(Visual.FlowDirectionProperty, direction);
    }

    /// <summary>Keeps a window sentence left to right inside a right-to-left language layout.</summary>
    public void ApplyWindowDirection(Control control) =>
        control.SetValue(Visual.FlowDirectionProperty, FlowDirection.LeftToRight);

    internal void SetFallbackFamilies(IReadOnlyList<string> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        _fallbackFamilies = families.Where(family => !string.IsNullOrWhiteSpace(family)).ToArray();
        _cache.Clear();
        _noticedMissingFonts.Clear();
        MissingFontNotices.Clear();
    }

    private bool IsInstalled(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return false;
        var fontFamily = new FontFamily(family);
        var systemFont = CurrentFontManager.SystemFonts.Any(installed =>
            string.Equals(installed.Name, fontFamily.Name, StringComparison.OrdinalIgnoreCase));
        var embeddedFont = Uri.TryCreate(family, UriKind.Absolute, out var familyUri) &&
            !string.IsNullOrWhiteSpace(familyUri.Fragment);
        if (!systemFont && !embeddedFont) return false;
        return CurrentFontManager.TryGetGlyphTypeface(new Typeface(fontFamily), out var typeface) &&
            string.Equals(typeface.FamilyName, fontFamily.Name, StringComparison.OrdinalIgnoreCase);
    }

    internal void RegisterMissingFontNotice(Control control, string? writingSystemId, string? styleName, string? text)
    {
        RegisterMissingFontNotice(control, Resolve(writingSystemId, styleName), styleName, text);
    }

    private void RegisterMissingFontNotice(
        Control control, WritingSystemTextStyle style, string? styleName, string? text)
    {
        if (style.RequestedFontInstalled ||
            style.WritingSystemId is not { Length: > 0 } id ||
            string.IsNullOrWhiteSpace(style.RequestedFontFamily) || string.IsNullOrWhiteSpace(text) ||
            _noticedMissingFonts.Contains(id) ||
            !control.IsEffectivelyVisible || !control.GetVisualAncestors().OfType<TopLevel>().Any() ||
            !_noticedMissingFonts.Add(id)) return;
        var fallback = FindFallbackFamily(id, styleName, text) ?? "sans-serif";
        MissingFontNotices.Add(new WritingSystemFontNotice(id,
            $"FieldWorks asks for {style.RequestedFontFamily} for {style.Name}, which isn't installed on this computer. Motif is using {fallback}."));
    }

    private static string? TextOf(Control control) => control switch
    {
        TextBlock textBlock => textBlock.Text,
        TextBox textBox => textBox.Text,
        _ => null,
    };

    private static FontFeatureCollection? ParseOpenTypeFeatures(string? features)
    {
        if (string.IsNullOrWhiteSpace(features)) return null;
        var valid = features.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(feature => OpenTypeFeature.IsMatch(feature) && feature[..4].Any(char.IsLetter))
            .ToArray();
        if (valid.Length == 0) return null;
        try
        {
            return FontFeatureCollection.Parse(string.Join(',', valid));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static CultureInfo CultureFor(string? writingSystemId)
    {
        if (string.IsNullOrWhiteSpace(writingSystemId)) return CultureInfo.InvariantCulture;
        try
        {
            return CultureInfo.GetCultureInfo(writingSystemId);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

/// <summary>A quiet notice that Motif is using a fallback for a missing requested font.</summary>
public sealed record WritingSystemFontNotice(string WritingSystemId, string Message);

/// <summary>The font family, OpenType features, direction, and size for one language-text style.</summary>
public sealed record WritingSystemTextStyle(
    FontFamily FontFamily,
    FontFeatureCollection? FontFeatures,
    FlowDirection FlowDirection,
    double FontSize,
    string? WritingSystemId,
    string Name,
    string? RequestedFontFamily,
    bool RequestedFontInstalled);
