using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Shows the exact problem report text before a person copies it or opens an issue form.</summary>
public sealed partial class ProblemReportPreviewWindow : Window
{
    /// <summary>Creates the preview using the supplied adapters or this window's native clipboard and browser.</summary>
    /// <param name="report">The report to review.</param>
    /// <param name="services">Optional clipboard and browser adapters.</param>
    public ProblemReportPreviewWindow(ProblemReport report, ProblemReportWindowServices? services = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        AvaloniaXamlLoader.Load(this);
        Model = (services ?? new ProblemReportWindowServices()).ModelFor(report, this);
        DataContext = Model;
    }

    /// <summary>The report text and explicit actions shown by the preview.</summary>
    public ProblemReportPreviewViewModel Model { get; }

    private async void OnCopyReportClick(object? sender, RoutedEventArgs e) => await Model.CopyReportAsync();

    private async void OnOpenIssueClick(object? sender, RoutedEventArgs e) => await Model.OpenIssueAsync();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
