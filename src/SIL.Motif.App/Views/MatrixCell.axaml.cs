using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>A Compare cell whose colour and pending marker reflect its project and parser meaning.</summary>
public sealed partial class MatrixCell : UserControl
{
    public static readonly Avalonia.StyledProperty<bool> CompactProperty =
        Avalonia.AvaloniaProperty.Register<MatrixCell, bool>(nameof(Compact));

    public MatrixCell() => AvaloniaXamlLoader.Load(this);

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }
}
