using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Motif's error window: a plain summary of an error that escaped the UI thread, its details folded away, and
/// an action that opens a report preview before anything is shared.
/// </summary>
public sealed partial class CrashWindow : Window
{
    private readonly CrashWindowServices _services;
    private ProblemReportPreviewWindow? _currentProblemReportPreview;

    /// <summary>Shows <paramref name="report"/> and opens report previews through <paramref name="services"/>.</summary>
    /// <param name="report">The error and what surrounds it.</param>
    /// <param name="services">The clipboard and browser adapters for the report preview.</param>
    public CrashWindow(CrashReport report, CrashWindowServices services)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        AvaloniaXamlLoader.Load(this);
        Model = new CrashReportViewModel(report);
        DataContext = Model;
    }

    /// <summary>What the window shows, and the actions its buttons run.</summary>
    public CrashReportViewModel Model { get; }

    /// <summary>The report preview currently owned by this window, if one is open.</summary>
    internal ProblemReportPreviewWindow? CurrentProblemReportPreview => _currentProblemReportPreview;

    private async void OnReportProblemClick(object? sender, RoutedEventArgs e)
    {
        var preview = new ProblemReportPreviewWindow(Model.ProblemReport, _services.PreviewServices);
        _currentProblemReportPreview = preview;
        try
        {
            await preview.ShowDialog(this).ConfigureAwait(true);
        }
        finally
        {
            _currentProblemReportPreview = null;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
