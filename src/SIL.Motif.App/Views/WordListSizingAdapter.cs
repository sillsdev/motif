using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.Views;

using ModuleWordRowHeader = SIL.Motif.App.Controls.WordPresentation.WordRowHeader;

internal sealed class WordListSizingAdapter
{
    private Control? _lastHeader;
    private WordListSchema? _lastSchema;
    private bool _lastShowsMeaning;
    private string? _lastMeaningHeader;
    private long _lastStyleRevision = -1;
    private string? _lastUiLocale;
    private WordListSizing? _lastSizing;

    internal int HeadingMeasurementCount { get; private set; }

    internal WordListSizing GetSizing(
        Control header,
        WordListSchema schema,
        bool showsMeaning,
        TextStyles? textStyles,
        out long styleRevision,
        out string uiLocale)
    {
        ArgumentNullException.ThrowIfNull(header);
        var interfaceStyle = textStyles?.Resolve(TextStyleRequest.Interface());
        styleRevision = textStyles?.Revision ?? 0;
        uiLocale = interfaceStyle?.UiLocale ?? System.Globalization.CultureInfo.CurrentUICulture.Name;
        var meaningHeader = MeaningHeaderOf(header);
        if (ReferenceEquals(_lastHeader, header) && Equals(_lastSchema, schema) &&
            _lastShowsMeaning == showsMeaning && _lastMeaningHeader == meaningHeader &&
            _lastStyleRevision == styleRevision && _lastUiLocale == uiLocale && _lastSizing is not null)
            return _lastSizing;

        var theme = header.ActualThemeVariant;
        var app = Application.Current ?? throw new InvalidOperationException("Word list sizing requires the app resources.");
        Thickness ReadThickness(string key) => app.TryGetResource(key, theme, out var value) && value is Thickness thickness
            ? thickness
            : throw new InvalidOperationException($"The word-list token {key} is missing or is not a thickness.");

        var padding = ReadThickness("Component.WordPresentation.Padding");
        var marker = ReadThickness("Intent.Stroke.MarkerStart");
        var chrome = padding.Left + padding.Right + marker.Left + marker.Right;
        var widths = new List<WordColumnSizing>(Enum.GetValues<WordListColumn>().Length);
        foreach (var column in Enum.GetValues<WordListColumn>())
        {
            var headingControl = HeadingFor(header, column);
            if (headingControl is null)
                throw new InvalidOperationException($"The word-list heading has no {column} sizing token.");
            var heading = HeadingWidth(headingControl, column, schema, showsMeaning);
            widths.Add(new WordColumnSizing(column,
                WordListSizingHints.GetMinimum(headingControl),
                WordListSizingHints.GetPreferred(headingControl),
                WordListSizingHints.GetMaximum(headingControl), heading));
        }

        var sizing = new WordListSizing(chrome,
            WordListSizingHints.GetScrollbarGutter(header),
            WordListSizingHints.GetColumnGap(header),
            WordListSizingHints.GetMinimumViewportWidth(header), widths);
        _lastHeader = header;
        _lastSchema = schema;
        _lastShowsMeaning = showsMeaning;
        _lastMeaningHeader = meaningHeader;
        _lastStyleRevision = styleRevision;
        _lastUiLocale = uiLocale;
        _lastSizing = sizing;
        return sizing;
    }

    private double HeadingWidth(Control control, WordListColumn column, WordListSchema schema, bool showsMeaning)
    {
        var flag = ColumnFlag(column);
        if (flag != WordListColumnSet.None && !schema.Columns.HasFlag(flag)) return 0;
        if (column == WordListColumn.Meaning && !showsMeaning) return 0;
        if (control is not TextBlock and not MarkGlyph)
        {
            var text = control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
            if (text is not null) control = text;
        }
        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        HeadingMeasurementCount++;
        return control.DesiredSize.Width;
    }

    private static Control? HeadingFor(Control header, WordListColumn column) =>
        header is ModuleWordRowHeader module ? module.HeadingFor(column) : null;

    private static string? MeaningHeaderOf(Control header) => header switch
    {
        ModuleWordRowHeader module => module.MeaningHeader,
        _ => null,
    };

    private static WordListColumnSet ColumnFlag(WordListColumn column) => column switch
    {
        WordListColumn.Tick => WordListColumnSet.Tick,
        WordListColumn.FieldWorks => WordListColumnSet.FieldWorks,
        WordListColumn.PanGloss => WordListColumnSet.PanGloss,
        WordListColumn.Meaning => WordListColumnSet.Meaning,
        WordListColumn.Warnings => WordListColumnSet.Warnings,
        WordListColumn.Places => WordListColumnSet.Places,
        WordListColumn.Time => WordListColumnSet.Time,
        WordListColumn.Read => WordListColumnSet.Read,
        _ => WordListColumnSet.None,
    };
}
