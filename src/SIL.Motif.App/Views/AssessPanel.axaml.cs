using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The run strip and the Words view of an Assessment, bound to its own <see cref="Assess"/> view model.</summary>
public sealed partial class AssessPanel : UserControl
{
    public AssessPanel(AssessViewModel assess)
    {
        ArgumentNullException.ThrowIfNull(assess);
        Assess = assess;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public AssessViewModel Assess { get; }

    private void OnTryWordClick(object? sender, RoutedEventArgs e)
    {
        if (Assess.Words.SelectedRow is { } row) Assess.OpenTryWord?.Invoke(row.Word);
    }
}
