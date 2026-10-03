using SIL.LCModel;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.WritingSystems;

/// <summary>Reads project fonts from LDML definitions and effective point sizes from LibLCM's stylesheet.</summary>
public static class WritingSystemDisplayReader
{
    /// <summary>
    /// Normal supplies interlinear, Lexicon Edit and text-title fields. Paragraph and Title_Main describe styled
    /// source paragraphs; Dictionary styles describe publication. Every project style is also captured.
    /// </summary>
    public static IReadOnlyList<string> Styles { get; } = Array.AsReadOnly(new[]
    {
        "Normal", "Paragraph", "Dictionary-Headword", "Dictionary-Vernacular", "Dictionary-POS", "Title_Main",
    });

    public static IReadOnlyList<WritingSystemDisplay> Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var sheet = new LcmStyleSheet();
        sheet.Init(cache, cache.LangProject.Hvo, LangProjectTags.kflidStyles);
        var styles = Styles.Concat(cache.LangProject.StylesOC.Select(style => style.Name))
            .Distinct(StringComparer.Ordinal).ToArray();
        var container = cache.ServiceLocator.WritingSystems;
        WritingSystemDisplay Describe(CoreWritingSystemDefinition ws, WritingSystemKind kind, int position)
        {
            (double Size, WritingSystemStyleFont Font, string Family, string Features) Settings(string name)
            {
                var style = sheet.Style(name);
                var common = style?.DefaultCharacterStyleInfo;
                var specific = style?.OverrideCharacterStyleInfo(ws.Handle);
                T Value<T>(IStyleProp<T>? overrideValue, IStyleProp<T>? defaultValue, T fallback) =>
                    overrideValue?.ValueIsSet == true ? overrideValue.Value :
                    defaultValue?.ValueIsSet == true ? defaultValue.Value : fallback;
                var normal = name == "Normal"
                    ? (Size: sheet.NormalFontSize / 1000d,
                        Font: new WritingSystemStyleFont(ws.DefaultFontName, ws.DefaultFontFeatures),
                        Family: StyleServices.DefaultFont, Features: "")
                    : Settings("Normal");
                var size = Value(specific?.FontSize, common?.FontSize, (int)(normal.Size * 1000)) / 1000d;
                var family = Value(specific?.FontName, common?.FontName, normal.Family);
                var usesDefaultFont = StyleServices.IsMagicFontName(family);
                var features = Value(specific?.Features, common?.Features, normal.Features);
                var resolvedFeatures = string.IsNullOrEmpty(features)
                    ? usesDefaultFont ? ws.DefaultFontFeatures : "" : features;
                if (style is null) return normal;
                return (size, new WritingSystemStyleFont(usesDefaultFont ? ws.DefaultFontName : family,
                    resolvedFeatures), family, features);
            }
            var settings = styles.ToDictionary(name => name, Settings, StringComparer.Ordinal);
            return new(ws.Id, ws.DisplayLabel, ws.Abbreviation, kind, position, position == 0,
                ws.DefaultFontName, ws.DefaultFontFeatures, ws.RightToLeftScript,
                settings.ToDictionary(pair => pair.Key, pair => pair.Value.Size, StringComparer.Ordinal))
            { StyleFonts = settings.ToDictionary(pair => pair.Key, pair => pair.Value.Font, StringComparer.Ordinal) };
        }
        return container.CurrentVernacularWritingSystems.Select((ws, i) => Describe(ws, WritingSystemKind.Vernacular, i))
            .Concat(container.CurrentAnalysisWritingSystems.Select((ws, i) => Describe(ws, WritingSystemKind.Analysis, i)))
            .ToArray();
    }
}
