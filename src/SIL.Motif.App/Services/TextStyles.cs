using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>Resolves language text styles and reads line metrics from the text layout a control renders.</summary>
public sealed partial class TextStyles
{
    private static readonly string[] MotifFallbacks = ["Noto Sans", "Segoe UI", "Arial", "sans-serif"];
    private static readonly Regex OpenTypeFeature = new("^[A-Za-z0-9]{4}=-?[0-9]+$", RegexOptions.Compiled);
    private FontManager? _fontManager;
    private readonly Dictionary<string, WritingSystemDisplay> _systems = new(StringComparer.Ordinal);
    private readonly Dictionary<TextStyleRequest, ResolvedTextStyle> _cache = [];
    private readonly HashSet<string> _noticedMissingFonts = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _fallbackFamilies = MotifFallbacks;
    private string _uiLocale = CultureInfo.CurrentUICulture.Name;

    internal Action<Action>? PresentationCheckScheduler { get; set; }

    /// <summary>The first requested font failure for each writing system drawn by this module.</summary>
    public ObservableCollection<WritingSystemFontNotice> MissingFontNotices { get; } = [];

    /// <summary>Creates text-style resolution backed by Avalonia's current font manager.</summary>
    public TextStyles() { }

    internal TextStyles(FontManager fontManager) =>
        _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));

    /// <summary>Raised after the captured display inventory or interface locale changes.</summary>
    public event EventHandler? ContextChanged;

    /// <summary>The revision of the captured display inventory and interface text policy.</summary>
    public long Revision { get; private set; }

    private FontManager CurrentFontManager => _fontManager ??= FontManager.Current;

    /// <summary>Replaces the captured display inventory and interface locale as one text-style context.</summary>
    public void ReplaceContext(IReadOnlyList<WritingSystemDisplay> writingSystems, string? uiLocale = null)
    {
        ArgumentNullException.ThrowIfNull(writingSystems);
        _systems.Clear();
        _noticedMissingFonts.Clear();
        MissingFontNotices.Clear();
        foreach (var writingSystem in writingSystems)
            _systems.TryAdd(writingSystem.Id, writingSystem);
        _uiLocale = string.IsNullOrWhiteSpace(uiLocale) ? CultureInfo.CurrentUICulture.Name : uiLocale;
        AdvanceRevision();
    }

    /// <summary>The current default vernacular id, if the Baseline recorded one.</summary>
    public string? DefaultVernacularId => _systems.Values
        .Where(system => system.Kind == WritingSystemKind.Vernacular && system.IsDefault)
        .Select(system => system.Id)
        .FirstOrDefault();

    /// <summary>Resolves one interface or linguistic text request against the current display context.</summary>
    public ResolvedTextStyle Resolve(TextStyleRequest request)
    {
        request = request with { UiLocale = string.IsNullOrWhiteSpace(request.UiLocale) ? _uiLocale : request.UiLocale };
        if (_cache.TryGetValue(request, out var cached)) return cached;
        if (request.Role == TextStyleRole.Interface)
        {
            var direction = CultureFor(request.UiLocale).TextInfo.IsRightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            var interfaceStyle = new ResolvedTextStyle(
                UiFontFamily, DefaultInterfaceFeatures, direction, InterfaceFontSize, null, string.Empty,
                UiFontFamily.ToString(), IsInstalled(UiFontFamily.ToString()), UiFallbackFamilies(request.UiLocale),
                Revision, request.UiLocale, request.Weight, request.Emphasis);
            _cache.Add(request, interfaceStyle);
            return interfaceStyle;
        }

        var writingSystemId = request.WritingSystemId;
        var requestedStyle = string.IsNullOrWhiteSpace(request.FieldWorksStyleName)
            ? "Normal"
            : request.FieldWorksStyleName;
        _systems.TryGetValue(writingSystemId ?? string.Empty, out var writingSystem);
        var style = writingSystem is not null &&
            (writingSystem.StyleSizes.ContainsKey(requestedStyle) || writingSystem.StyleFonts.ContainsKey(requestedStyle))
                ? requestedStyle : "Normal";
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
        var result = new ResolvedTextStyle(
            family,
            features,
            writingSystem?.RightToLeft == true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            size * 96d / 72d,
            writingSystem?.Id,
            writingSystem?.Name ?? writingSystem?.Id ?? string.Empty,
            requestedFamily,
            IsInstalled(requestedFamily),
            _fallbackFamilies,
            Revision,
            request.UiLocale,
            request.Weight,
            request.Emphasis);
        _cache.Add(request, result);
        return result;
    }

    internal static TextStyles FromTrace(IReadOnlyList<TraceWritingSystem> writingSystems)
    {
        var resolver = new TextStyles();
        resolver.ReplaceContext(writingSystems.Select((system, index) =>
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

    /// <summary>Finds the first installed fallback family that can draw text for a linguistic request.</summary>
    public string? FindFallbackFamily(TextStyleRequest request, string? text)
    {
        var style = Resolve(request);
        if (style.RequestedFontInstalled || string.IsNullOrWhiteSpace(style.RequestedFontFamily)) return null;
        string?[] familyNames = [style.RequestedFontFamily, .. style.FontFallbacks];
        var family = new FontFamily(string.Join(", ", familyNames));
        var culture = CultureFor(request.WritingSystemId);
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

    /// <summary>Applies the resolved family, features, size, emphasis, and direction to a control.</summary>
    public void Apply(Control control, TextStyleRequest request)
    {
        ArgumentNullException.ThrowIfNull(control);
        var style = Resolve(request);
        if (style.FontFamily is { } family) control.SetValue(TextElement.FontFamilyProperty, family);
        if (style.FontSize is { } size) control.SetValue(TextElement.FontSizeProperty, size);
        if (style.FontFeatures is not null || request.Role == TextStyleRole.Linguistic)
            control.SetValue(TextElement.FontFeaturesProperty, style.FontFeatures);
        if (style.Weight is { } weight) control.SetValue(TextElement.FontWeightProperty, weight);
        if (style.Emphasis is { } emphasis) control.SetValue(TextElement.FontStyleProperty, emphasis);
        control.SetValue(Visual.FlowDirectionProperty, style.FlowDirection);
    }

    /// <summary>Sets the direction of a running-text layout from its language's saved settings.</summary>
    public void ApplyFlowDirection(Control control, TextStyleRequest request)
    {
        var direction = Resolve(request).FlowDirection;
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

    /// <summary>Reads shaped line metrics from the native layout used by a text control.</summary>
    public RunLineMetrics? GetLineMetrics(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var layout = control switch
        {
            TextBlock textBlock => textBlock.TextLayout,
            TextPresenter presenter => presenter.TextLayout,
            _ => control.GetVisualDescendants().OfType<TextPresenter>()
                .Select(presenter => presenter.TextLayout).FirstOrDefault(),
        };
        if (layout is null || layout.TextLines.Count == 0) return null;

        var ascent = 0d;
        var descent = 0d;
        var leading = 0d;
        var y = 0d;
        Rect? ink = null;
        foreach (var line in layout.TextLines)
        {
            ascent = Math.Max(ascent, line.Baseline);
            descent = Math.Max(descent, Math.Max(0, line.Height - line.Baseline));
            leading = Math.Max(leading, Math.Max(0, line.Height - line.Extent));
            var overhang = Math.Max(Math.Abs(line.OverhangLeading), Math.Abs(line.OverhangTrailing));
            var inkBottom = y + line.Height + line.OverhangAfter;
            var inkTop = inkBottom - line.Extent;
            var lineInk = new Rect(-overhang, inkTop,
                Math.Max(0, line.WidthIncludingTrailingWhitespace + 2 * overhang),
                Math.Max(0, inkBottom - inkTop));
            ink = ink is { } current ? current.Union(lineInk) : lineInk;
            y += line.Height;
        }

        var inkBounds = ink ?? default;
        var top = Math.Min(0, inkBounds.Top);
        var bottom = Math.Max(layout.Height, inkBounds.Bottom);
        return new RunLineMetrics(ascent, descent, leading, inkBounds, bottom - top);
    }

    internal void SetFallbackFamilies(IReadOnlyList<string> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        _fallbackFamilies = families.Where(family => !string.IsNullOrWhiteSpace(family)).ToArray();
        _noticedMissingFonts.Clear();
        MissingFontNotices.Clear();
        AdvanceRevision();
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

    internal void RegisterMissingFontNotice(Control control, TextStyleRequest request, string? text)
    {
        RegisterMissingFontNotice(control, Resolve(request), request, text);
    }

    private void RegisterMissingFontNotice(
        Control control, ResolvedTextStyle style, TextStyleRequest request, string? text)
    {
        var id = style.WritingSystemId;
        var metrics = GetLineMetrics(control);
        if (style.RequestedFontInstalled ||
            string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(style.RequestedFontFamily) || string.IsNullOrWhiteSpace(text) ||
            _noticedMissingFonts.Contains(id) ||
            !control.IsEffectivelyVisible || metrics is null ||
            !SIL.Motif.App.Controls.WritingSystemText.IsInVisibleViewport(control, metrics.Value.InkBounds) ||
            !_noticedMissingFonts.Add(id)) return;
        var fallback = FindFallbackFamily(request, text) ?? "sans-serif";
        MissingFontNotices.Add(new WritingSystemFontNotice(id,
            $"FieldWorks asks for {style.RequestedFontFamily} for {style.Name}, which isn't installed on this computer. Motif is using {fallback}."));
    }

    private void AdvanceRevision()
    {
        _cache.Clear();
        Revision++;
        ContextChanged?.Invoke(this, EventArgs.Empty);
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

/// <summary>Identifies an interface or linguistic text style and its optional emphasis.</summary>
public readonly record struct TextStyleRequest(
    TextStyleRole Role,
    string? WritingSystemId = null,
    string? FieldWorksStyleName = null,
    string? UiLocale = null,
    FontWeight? Weight = null,
    FontStyle? Emphasis = null)
{
    /// <summary>Creates a request for one tagged linguistic string and exact FieldWorks style name.</summary>
    public static TextStyleRequest Linguistic(string? writingSystemId, string? fieldWorksStyleName = null) =>
        new(TextStyleRole.Linguistic, writingSystemId, fieldWorksStyleName);

    /// <summary>Creates a request that follows the interface locale and the App's UI typography tokens.</summary>
    public static TextStyleRequest Interface(string? uiLocale = null) =>
        new(TextStyleRole.Interface, UiLocale: uiLocale);
}

/// <summary>The kind of display policy a text style request selects.</summary>
public enum TextStyleRole
{
    /// <summary>Text belonging to the interface locale and using Intent typography.</summary>
    Interface,

    /// <summary>Language text resolved from the captured FieldWorks writing-system inventory.</summary>
    Linguistic,
}

/// <summary>The resolved font, direction, size, fallback chain, and display revision for one text request.</summary>
public sealed record ResolvedTextStyle(
    FontFamily? FontFamily,
    FontFeatureCollection? FontFeatures,
    FlowDirection FlowDirection,
    double? FontSize,
    string? WritingSystemId,
    string Name,
    string? RequestedFontFamily,
    bool RequestedFontInstalled,
    IReadOnlyList<string> FontFallbacks,
    long Revision,
    string? UiLocale,
    FontWeight? Weight,
    FontStyle? Emphasis);

/// <summary>Vertical line metrics and conservative ink bounds from a control's rendered text layout.</summary>
public readonly record struct RunLineMetrics(
    double Ascent,
    double Descent,
    double Leading,
    Rect InkBounds,
    double RequiredLineBox);
