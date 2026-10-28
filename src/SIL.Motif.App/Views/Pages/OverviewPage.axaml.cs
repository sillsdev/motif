using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>The project summary page, built from its response and its own page model.</summary>
public sealed partial class OverviewPage : UserControl, IWorkspacePaneProvider
{
    private readonly OverviewInspector _inspector;
    private readonly Button _inspectorAction;

    /// <summary>The stored Lost-word evidence attached to the workspace Inspector.</summary>
    public Control InspectorContent => _inspector;

    /// <summary>The pinned action for opening recorded Lost words in the Matrix.</summary>
    public Control InspectorFooter => _inspectorAction;

    /// <summary>Creates the page and its Inspector over one stored Overview model.</summary>
    public OverviewPage(OverviewPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        _inspector = new OverviewInspector(page);
        _inspectorAction = new Button
        {
            Name = "OverviewLostWordsAction",
            Content = "Open in Matrix",
            Classes = { "primary", "overviewInspectorAction" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Command = page.OpenLostWordsCommand,
            DataContext = page,
        };
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The Overview model used by its project chooser.</summary>
    public OverviewPageModel Page => (OverviewPageModel)DataContext!;

    private void OnOverviewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!this.TryFindResource("Component.Overview.ColumnsBreakpoint", out var resource) ||
            resource is not double breakpoint) return;
        var grid = this.FindControl<Grid>("OverviewDetailsGrid")!;
        var right = this.FindControl<StackPanel>("OverviewRightColumn")!;
        var columns = e.NewSize.Width >= breakpoint;
        if (grid.ColumnDefinitions.Count == (columns ? 2 : 1)) return;
        grid.ColumnDefinitions = new ColumnDefinitions(columns ? "*,*" : "*");
        grid.RowDefinitions = new RowDefinitions(columns ? "Auto" : "Auto,Auto");
        Grid.SetColumn(right, columns ? 1 : 0);
        Grid.SetRow(right, columns ? 0 : 1);
    }
}
