using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Warnings tab's stored evidence and actions.</summary>
public sealed partial class WarningsTab : UserControl
{
    public static readonly StyledProperty<Control?> RowsContentProperty =
        AvaloniaProperty.Register<WarningsTab, Control?>(nameof(RowsContent));

    public static readonly StyledProperty<double> ColumnsBreakpointProperty =
        AvaloniaProperty.Register<WarningsTab, double>(nameof(ColumnsBreakpoint));

    public double ColumnsBreakpoint
    {
        get => GetValue(ColumnsBreakpointProperty);
        set => SetValue(ColumnsBreakpointProperty, value);
    }

    public Control? RowsContent
    {
        get => GetValue(RowsContentProperty);
        set => SetValue(RowsContentProperty, value);
    }

    public WarningsTab()
    {
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, _) => ArrangeEvidence();
        DataContextChanged += (_, _) =>
            this.FindControl<ContentControl>("WarningEvidence")!.Content = DataContext is DiagnosticsPageModel model
                ? new WarningsInspectorPanel(model.Warnings.Grammar) : null;
    }

    private bool? _wide;

    private void ArrangeEvidence()
    {
        if (ColumnsBreakpoint <= 0) return;
        var wide = Bounds.Width >= ColumnsBreakpoint;
        if (_wide == wide) return;
        _wide = wide;
        var body = this.FindControl<Grid>("WarningBody")!;
        body.ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "*");
        body.RowDefinitions = new RowDefinitions(wide ? "*" : "*,*");
        var evidence = this.FindControl<ContentControl>("WarningEvidence")!;
        Grid.SetRow(evidence, wide ? 0 : 1);
        Grid.SetColumn(evidence, wide ? 1 : 0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowsContentProperty)
            this.FindControl<ContentControl>("RowsHost")!.Content = RowsContent;
        if (change.Property == ColumnsBreakpointProperty && this.FindControl<Grid>("WarningBody") is not null)
            ArrangeEvidence();
    }
}
