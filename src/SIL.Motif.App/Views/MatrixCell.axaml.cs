using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>A Compare cell whose colour and pending marker reflect its project and parser meaning.</summary>
public sealed partial class MatrixCell : UserControl
{
    /// <summary>Shows the cell label, places, and refusal details.</summary>
    public static readonly Avalonia.StyledProperty<bool> ShowsDetailsProperty =
        Avalonia.AvaloniaProperty.Register<MatrixCell, bool>(nameof(ShowsDetails), true);

    public static readonly Avalonia.StyledProperty<bool> CompactProperty =
        Avalonia.AvaloniaProperty.Register<MatrixCell, bool>(nameof(Compact));

    public MatrixCell() => AvaloniaXamlLoader.Load(this);

    /// <summary>Whether this cell shows its label, places, and refusal details.</summary>
    public bool ShowsDetails
    {
        get => GetValue(ShowsDetailsProperty);
        set
        {
            SetValue(ShowsDetailsProperty, value);
            this.FindControl<StackPanel>("CellDetails")!.IsVisible = value;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.FindAncestorOfType<ComparePanel>() is { } panel)
            ShowsDetails = !panel.Classes.Contains("shortMatrix");
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }
}
