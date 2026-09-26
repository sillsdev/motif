using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Motif's error window: a plain summary of an error that escaped the UI thread, its details folded away, and
/// Copy details, Save report, Email maintainer and Close.
/// </summary>
public sealed partial class CrashWindow : Window
{
    /// <summary>Shows <paramref name="report"/>, with actions that go through <paramref name="services"/>.</summary>
    /// <param name="report">The error and what surrounds it.</param>
    /// <param name="services">The clipboard, save dialog and mail launcher; each one unset is this window's own.</param>
    public CrashWindow(CrashReport report, CrashWindowServices services)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(services);
        AvaloniaXamlLoader.Load(this);
        Model = services.ModelFor(report, this);
        DataContext = Model;
    }

    /// <summary>What the window shows, and the actions its buttons run.</summary>
    public CrashReportViewModel Model { get; }

    private async void OnCopyDetailsClick(object? sender, RoutedEventArgs e) => await Model.CopyDetailsAsync();

    private async void OnSaveReportClick(object? sender, RoutedEventArgs e) => await Model.SaveReportAsync();

    private async void OnEmailMaintainerClick(object? sender, RoutedEventArgs e) => await Model.EmailMaintainerAsync();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
