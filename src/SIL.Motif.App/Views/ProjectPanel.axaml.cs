using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Where the project stands: its Baseline, its latest Assessment, its grammar and its history, bound to the
/// Overview page's model so the summary follows every page.
/// </summary>
public sealed partial class ProjectPanel : UserControl
{
    public ProjectPanel(OverviewPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public OverviewPageModel Page { get; }

    public ProjectViewModel Project => Page.Project;

    public BaselineViewModel Baseline => Page.Baseline;

    public ProjectHistoryViewModel History => Page.History;

    private void OnOpenResultsClick(object? sender, RoutedEventArgs e) => Page.Context.OpenTexts(TextsTab.Words);

    private void OnOpenStatisticsClick(object? sender, RoutedEventArgs e) => Page.Context.OpenPage(WorkspacePage.Timing);

    private void OnOpenTimeLimitClick(object? sender, RoutedEventArgs e) => Page.Context.OpenTexts(TextsTab.Texts);

    private void OnOpenGrammarClick(object? sender, RoutedEventArgs e) => Page.Context.OpenPage(WorkspacePage.Warnings);
}
