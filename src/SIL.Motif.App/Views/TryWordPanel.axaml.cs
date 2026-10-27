using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Try a Word page: a box for any word, and the trace of it against the current Baseline's grammar. Also
/// opens a diagnostic someone saved earlier in a window of its own.
/// </summary>
public sealed partial class TryWordPanel : UserControl
{
    /// <summary>Creates a working page over the shared trace and navigation model.</summary>
    public TryWordPanel(TryWordPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        Trace = model.Trace;
        AttemptInspector = new TryWordAttemptInspector(Trace);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<LazyContent>("RichDiagnosticHost")!.Factory = () =>
        {
            var diagnostic = new DiagnosticPanel(model.Diagnostics, showResultSummary: false, showAnalyses: false,
                showAttemptSummaries: false);
            diagnostic.Bind(DiagnosticPanel.ShowFullDerivationProperty, new Avalonia.Data.Binding("IsExpert"));
            return diagnostic;
        };
        model.SavedDiagnosticOpened += OnSavedDiagnosticOpened;
    }

    /// <summary>The recorded steps of the selected attempt, supplied to the frame's Inspector.</summary>
    public TryWordAttemptInspector AttemptInspector { get; }

    private void OnTryWordSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!this.TryFindResource("Component.TryWord.ColumnsBreakpoint", out var resource) ||
            resource is not double breakpoint) return;
        var compact = e.NewSize.Width < breakpoint;
        var input = this.FindControl<Grid>("TryWordInputGrid")!;
        input.ColumnDefinitions = new ColumnDefinitions(compact ? "*,Auto" : "*,Auto,Auto,Auto");
        input.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        var mode = this.FindControl<Border>("TryWordModeButtons")!;
        var tools = this.FindControl<Button>("TryWordToolsButton")!;
        Grid.SetColumn(mode, compact ? 0 : 2);
        Grid.SetRow(mode, compact ? 1 : 0);
        Grid.SetColumn(tools, compact ? 1 : 3);
        Grid.SetRow(tools, compact ? 1 : 0);
        var comparison = this.FindControl<Grid>("TryWordComparisonGrid")!;
        var parser = this.FindControl<Border>("TryWordParserComparison")!;
        comparison.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,*");
        comparison.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        Grid.SetColumn(parser, compact ? 0 : 1);
        Grid.SetRow(parser, compact ? 1 : 0);
    }

    /// <summary>The trace whose result and selected attempt this page presents.</summary>
    public TraceWordViewModel Trace { get; }

    /// <summary>The workspace links and tools associated with the displayed trace.</summary>
    public TryWordPageModel Model { get; }

    private async void OnOpenDiagnosticClick(object? sender, RoutedEventArgs e) =>
        await Model.OpenSavedDiagnosticAsync();

    private async void OnCopyInstructionsClick(object? sender, RoutedEventArgs e) =>
        await Model.Diagnostics.CopyInstructionsAsync();

    private async void OnSaveClick(object? sender, RoutedEventArgs e) => await Model.Diagnostics.SaveAsync();

    private async void OnCopyJsonClick(object? sender, RoutedEventArgs e) => await Model.Diagnostics.CopyJsonAsync();

    private void OnSavedDiagnosticOpened(OpenedDiagnostic opened)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) new DiagnosticWindow(opened, Model.Diagnostics).Show(owner);
    }
}
