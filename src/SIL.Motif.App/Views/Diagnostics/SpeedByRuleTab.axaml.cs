using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The SpeedByRule tab's stored evidence and actions.</summary>
public sealed partial class SpeedByRuleTab : UserControl
{
    public static readonly StyledProperty<Control?> RowsContentProperty =
        AvaloniaProperty.Register<SpeedByRuleTab, Control?>(nameof(RowsContent));

    public Control? RowsContent
    {
        get => GetValue(RowsContentProperty);
        set => SetValue(RowsContentProperty, value);
    }

    public SpeedByRuleTab()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowsContentProperty)
            this.FindControl<ContentControl>("RowsHost")!.Content = RowsContent;
    }
}
