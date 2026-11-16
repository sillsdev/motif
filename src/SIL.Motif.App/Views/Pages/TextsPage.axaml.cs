using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.App.Views.Words;

namespace SIL.Motif.App.Views;

/// <summary>The current Selection's text reader and source pane.</summary>
public sealed partial class TextsPage : UserControl, IWorkspacePaneProvider
{
    private readonly Control _sourcePane;

    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public TextsPage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new TextsPageModel(owner))) { }

    public TextsPage(TextsPageModel page) : this(page, null) { }

    internal TextsPage(TextsPageModel page, SettingsViewModel? settings)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        _sourcePane = new SourcesPanel(page.Sources, page.Selection, page.Words, settings);
        _sourcePane.Bind(IsEnabledProperty, new Binding(nameof(WorkspaceContext.ProjectAndSelectionEnabled))
        {
            Source = page.Context,
        });
        this.FindControl<ContentControl>("ReaderHost")!.Content = new ResultsInTextPanel(page.ResultsInText);
        page.ResultsInText.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ResultsInTextViewModel.OccurrencePanel)) ShowWordPanel(page.ResultsInText.OccurrencePanel);
        };
        SizeChanged += (_, _) => ArrangeWordPane();
        this.FindControl<Grid>("PassageBody")!.SizeChanged += (_, _) => ArrangeWordPane();
        ShowWordPanel(page.ResultsInText.OccurrencePanel);
    }

    private bool? _sideBySide;

    private void ShowWordPanel(WordOccurrencesViewModel? model)
    {
        var host = this.FindControl<ContentControl>("WordPaneHost")!;
        var pane = this.FindControl<Border>("WordPane")!;
        host.Content = model is null ? null : new WordOccurrencesPanel(model);
        pane.IsVisible = model is not null;
        ArrangeWordPane();
    }

    /// <summary>Puts the word panel beside the reader when there is room and under it when there is not.</summary>
    private void ArrangeWordPane()
    {
        if (!this.TryFindResource("Component.WordOccurrences.PanelBreakpoint", ActualThemeVariant, out var resource) ||
            resource is not double breakpoint || Bounds.Width <= 0) return;
        var body = this.FindControl<Grid>("PassageBody")!;
        var pane = this.FindControl<Border>("WordPane")!;
        var side = Bounds.Width >= breakpoint;
        if (side)
        {
            pane.ClearValue(Layoutable.MinHeightProperty);
            pane.MaxHeight = double.PositiveInfinity;
        }
        else if (body.Bounds.Height > 0)
        {
            // Under the reader the panel takes at most its share of the body, so a short zoomed window keeps words.
            var share = body.Bounds.Height * PaneLayout.StackedPaneShare;
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

    /// <summary>The source pane's title.</summary>
    public string SidePanelTitle => MotifAppStrings.BoardSources;

    /// <summary>The Selection control hosted beside the reader.</summary>
    public Control SidePanel => _sourcePane;
}
