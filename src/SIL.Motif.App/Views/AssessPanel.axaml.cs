using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The run strip, bound to its own <see cref="Assess"/> view model.</summary>
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

    // Try a Word's focus mode moves it to column 0 spanning all three columns instead of sharing column 2.
    public static readonly IValueConverter FocusedColumn =
        new FuncValueConverter<bool, int>(focused => focused ? 0 : 2);

    public static readonly IValueConverter FocusedSpan =
        new FuncValueConverter<bool, int>(focused => focused ? 3 : 1);

    // Each candidate's step row carries itself as Tag: the click drives the shared "Selected step" detail.
    private void OnTraceStepClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TraceStepViewModel step }) Assess.Trace.SelectedStep = step;
    }
}
