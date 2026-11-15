using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Words;

namespace SIL.Motif.App.Views;

/// <summary>The whole Context's words, grouped by written shape, with the shared places panel for the open word.</summary>
public sealed partial class WordsPage : UserControl
{
    private const double StackedPaneShare = 0.5;
    private bool? _sideBySide;

    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public WordsPage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new WordsPageModel(owner))) { }

    public WordsPage(WordsPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("WordsHost")!.Content = new TextWordsPanel(model.Words);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WordsPageModel.OccurrencePanel)) ShowWordPanel(model.OccurrencePanel);
        };
        SizeChanged += (_, _) => ArrangeWordPane();
        this.FindControl<Grid>("WordsBody")!.SizeChanged += (_, _) => ArrangeWordPane();
        ShowWordPanel(model.OccurrencePanel);
    }

    private void ShowWordPanel(WordOccurrencesViewModel? model)
    {
        var host = this.FindControl<ContentControl>("WordPaneHost")!;
        host.Content = model is null ? null : new WordOccurrencesPanel(model);
        this.FindControl<Border>("WordPane")!.IsVisible = model is not null;
        ArrangeWordPane();
    }

    /// <summary>Puts the places panel beside the list when there is room and under it when there is not.</summary>
    private void ArrangeWordPane()
    {
        if (!this.TryFindResource("Component.WordOccurrences.PanelBreakpoint", ActualThemeVariant, out var resource) ||
            resource is not double breakpoint || Bounds.Width <= 0) return;
        var body = this.FindControl<Grid>("WordsBody")!;
        var pane = this.FindControl<Border>("WordPane")!;
        var side = Bounds.Width >= breakpoint;
        if (side)
        {
            pane.ClearValue(Layoutable.MinHeightProperty);
            pane.MaxHeight = double.PositiveInfinity;
        }
        else if (body.Bounds.Height > 0)
        {
            var share = body.Bounds.Height * StackedPaneShare;
            var minimum = this.TryFindResource("Component.WordOccurrences.PaneMinHeight", ActualThemeVariant, out var min) &&
                min is double tall ? tall : share;
            pane.MinHeight = Math.Min(minimum, share);
            pane.MaxHeight = share;
        }
        pane.Classes.Set("beside", side);
        if (_sideBySide == side) return;
        _sideBySide = side;
        Grid.SetRow(pane, side ? 0 : 1);
        Grid.SetColumn(pane, side ? 1 : 0);
        Grid.SetColumnSpan(pane, side ? 1 : 2);
        body.ColumnDefinitions = new ColumnDefinitions(side ? "*,Auto" : "*");
        body.RowDefinitions = new RowDefinitions(side ? "*" : "*,Auto");
    }
}
