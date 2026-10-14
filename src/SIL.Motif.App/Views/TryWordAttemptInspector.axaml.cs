using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The recorded steps and details of the attempt selected on Try a Word.</summary>
public sealed partial class TryWordAttemptInspector : UserControl
{
    /// <summary>Creates the attempt Inspector over the page's existing trace.</summary>
    public TryWordAttemptInspector(TraceWordViewModel trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        DataContext = trace;
        AvaloniaXamlLoader.Load(this);
    }
}
