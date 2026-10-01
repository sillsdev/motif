using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The card an opened Lists row shows: FieldWorks' analysis over PanGloss's, a column per aligned segment so a
/// morpheme sits over the pieces PanGloss reads it as, then where the two part and the way to compare them.
/// </summary>
public sealed partial class ListWordCard : UserControl
{
    public ListWordCard()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Show(DataContext switch
        {
            CompareWordViewModel word => new ListWordCardViewModel(word.WordRow),
            WordRowViewModel row => new ListWordCardViewModel(row),
            _ => null,
        });
    }

    /// <summary>The card's facts for the word it shows, or <see langword="null"/> before it has one.</summary>
    public ListWordCardViewModel? Card { get; private set; }

    private void Show(ListWordCardViewModel? card)
    {
        Card = card;
        var root = this.FindControl<StackPanel>("Root")!;
        root.DataContext = card;
        var host = this.FindControl<Panel>("AlignmentHost")!;
        host.Children.Clear();
        if (card is not null) host.Children.Add(Alignment(card));
    }

    private static Grid Alignment(ListWordCardViewModel card)
    {
        var grid = new Grid { Classes = { "listCardAlignment" }, RowDefinitions = new RowDefinitions("Auto,Auto") };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var columns = Math.Max(card.Segments.Count, 1);
        for (var index = 0; index < columns; index++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        Place(grid, Label("FIELDWORKS"), 0, 0);
        Place(grid, Label("PANGLOSS"), 1, 0);
        for (var index = 0; index < card.Segments.Count; index++)
        {
            var segment = card.Segments[index];
            if (card.HasFieldWorksAnalysis) Place(grid, Span(segment.FieldWorks, parted: segment.IsParted, blue: false), 0, index + 1);
            if (card.ShowsPanGlossMorphemes) Place(grid, Span(segment.PanGloss, parted: false, blue: true), 1, index + 1);
        }
        if (!card.HasFieldWorksAnalysis)
            Place(grid, new CopyableTextBlock { Text = card.FieldWorksAbsentText, Classes = { "muted", "listCardAbsent" } },
                0, 1, columns);
        if (!card.ShowsPanGlossMorphemes)
            Place(grid, new MarkChip
            {
                Mark = card.OutcomeMark, Text = card.OutcomeWord, Compact = true,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            }, 1, 1, columns);
        if (card.HasPanGlossNote)
            Place(grid, new CopyableTextBlock { Text = card.PanGlossNote, Classes = { "muted", "listCardNote" } },
                1, columns + 1);
        return grid;
    }

    private static TextBlock Label(string text) => new CopyableTextBlock
        { Text = text, Classes = { "wordRowCardLabel" }, VerticalAlignment = VerticalAlignment.Center };

    // Only PanGloss's differing pieces are blue; FieldWorks' morpheme where they part takes the span's edge.
    private static Border Span(IReadOnlyList<ListWordCardMorphViewModel> morphs, bool parted, bool blue)
    {
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "listCardChips" } };
        foreach (var morph in morphs) chips.Children.Add(Chip(morph, blue && morph.IsDifferent));
        var span = new Border { Child = chips, Classes = { "listCardSpan" } };
        if (parted && morphs.Count > 0) span.Classes.Add("parted");
        return span;
    }

    private static Border Chip(ListWordCardMorphViewModel item, bool different)
    {
        var morph = item.Morph;
        var words = new StackPanel();
        words.Children.Add(morph.HasLink ? FormLink(morph)
            : new CopyableTextBlock { Text = morph.Form, Classes = { "wordRowMorphForm" } });
        words.Children.Add(new CopyableTextBlock { Text = morph.GlossOrPlaceholder, Classes = { "wordRowMorphGloss" } });
        var chip = new Border { Child = words, Classes = { "wordRowMorph", "listCardMorph" } };
        if (different) chip.Classes.Add("different");
        return chip;
    }

    private static HyperlinkButton FormLink(ParserReadingMorphViewModel morph)
    {
        var words = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "morphFormLinkWords" } };
        words.Children.Add(new TextBlock { Text = morph.Form, Classes = { "wordRowMorphForm" } });
        words.Children.Add(new TextBlock { Text = "↗", Classes = { "morphLinkMark" } });
        var link = new HyperlinkButton
        {
            Content = words,
            NavigateUri = morph.Link,
            Classes = { "morphFormLink", "morphLink", "listCardFormLink" },
        };
        AutomationProperties.SetName(link, morph.LinkName);
        ToolTip.SetTip(link, morph.FormLinkTip);
        return link;
    }

    private static void Place(Grid grid, Control control, int row, int column, int span = 1)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, span);
        grid.Children.Add(control);
    }
}
