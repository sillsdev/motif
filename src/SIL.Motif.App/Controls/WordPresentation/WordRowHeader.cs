using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>A word-list heading that consumes the same immutable layout as its rows.</summary>
public sealed class WordRowHeader : UserControl
{
    private readonly Border _frame;
    private readonly Grid _line;
    private readonly Dictionary<WordListColumn, Control> _headings;

    public static readonly StyledProperty<string> MeaningHeaderProperty =
        AvaloniaProperty.Register<WordRowHeader, string>(nameof(MeaningHeader), "Meaning");

    static WordRowHeader() =>
        WordListLayoutScope.LayoutProperty.Changed.AddClassHandler<WordRowHeader>((header, _) => header.Rebuild());

    public WordRowHeader()
    {
        _line = new Grid { Classes = { "wordPresentationHeaderLine" } };
        _frame = new Border { Classes = { "wordPresentationHeader" }, Child = _line };
        Content = _frame;
        _headings = Enum.GetValues<WordListColumn>().ToDictionary(column => column, CreateHeading);
        Rebuild();
    }

    /// <summary>The label for the page's result column.</summary>
    public string MeaningHeader
    {
        get => GetValue(MeaningHeaderProperty);
        set => SetValue(MeaningHeaderProperty, value);
    }

    internal Control HeadingFor(WordListColumn column) => _headings[column];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MeaningHeaderProperty) Rebuild();
    }

    /// <summary>Builds headings from the list owner's resolved track widths.</summary>
    public void Rebuild()
    {
        var layout = WordListLayoutScope.GetLayout(this);
        var tracks = layout?.Tracks.Select(track => track.Column).ToArray() ?? DefaultColumns();
        _line.ColumnSpacing = layout?.ColumnGap ?? 0;
        _line.Children.Clear();
        _line.ColumnDefinitions.Clear();
        foreach (var column in tracks)
        {
            var width = layout is null ? GridLength.Auto : new GridLength(layout.Tracks.Single(item => item.Column == column).Width);
            _line.ColumnDefinitions.Add(new ColumnDefinition(width));
            var heading = _headings[column];
            Grid.SetColumn(heading, _line.ColumnDefinitions.Count - 1);
            _line.Children.Add(heading);
        }
    }

    private Control CreateHeading(WordListColumn column)
    {
        Control heading = column switch
        {
            WordListColumn.Tick or WordListColumn.Read => new Panel { Classes = { HeadingClass(column) } },
            WordListColumn.Warnings => new MarkGlyph
            {
                Mark = Mark.Warning,
                Classes = { "inline", "wordPresentationColumnWarnings", "wordPresentationHeading" },
            },
            _ => new CopyableTextBlock
            {
                Text = HeadingText(column),
                Classes = { HeadingClass(column), "wordPresentationHeading" },
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            },
        };
        if (column == WordListColumn.Warnings) AutomationProperties.SetName(heading, "Warnings");
        return heading;
    }

    private string HeadingText(WordListColumn column) => column switch
    {
        WordListColumn.Word => "Word",
        WordListColumn.FieldWorks => "FieldWorks",
        WordListColumn.PanGloss => "PanGloss",
        WordListColumn.Meaning => MeaningHeader,
        WordListColumn.Places => "Places",
        WordListColumn.Time => "Time",
        WordListColumn.Next => "Next",
        _ => string.Empty,
    };

    private static string HeadingClass(WordListColumn column) => column switch
    {
        WordListColumn.Word => "wordPresentationColumnWord",
        WordListColumn.FieldWorks => "wordPresentationColumnFieldWorks",
        WordListColumn.PanGloss => "wordPresentationColumnPanGloss",
        WordListColumn.Meaning => "wordPresentationColumnMeaning",
        WordListColumn.Warnings => "wordPresentationColumnWarnings",
        WordListColumn.Places => "wordPresentationColumnPlaces",
        WordListColumn.Time => "wordPresentationColumnTime",
        WordListColumn.Read => "wordPresentationColumnRead",
        WordListColumn.Next => "wordPresentationColumnNext",
        _ => "wordPresentationColumnTick",
    };

    private static WordListColumn[] DefaultColumns() =>
        WordListSchema.Create(WordListColumnSet.All).OrderedColumns().ToArray();
}
