using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Where the project stands: its Baseline, its latest Assessment, its grammar and its history, bound to the
/// workspace so the summary follows every stage.
/// </summary>
public sealed partial class ProjectPanel : UserControl
{
    public ProjectPanel(HandoffWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Workspace = workspace;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public HandoffWorkspaceViewModel Workspace { get; }

    public ProjectViewModel Project => Workspace.Project;

    public BaselineViewModel Baseline => Workspace.Baseline;

    public ProjectHistoryViewModel History => Workspace.ProjectHistory;

    private void OnOpenResultsClick(object? sender, RoutedEventArgs e) => Open(WorkflowStage.Results, ResultsView.Words);

    private void OnOpenStatisticsClick(object? sender, RoutedEventArgs e) => Open(WorkflowStage.Results, ResultsView.Statistics);

    private void OnOpenTimeLimitClick(object? sender, RoutedEventArgs e) => Open(WorkflowStage.Texts, Workspace.ResultsView);

    private void OnOpenGrammarClick(object? sender, RoutedEventArgs e) => Open(WorkflowStage.Grammar, Workspace.ResultsView);

    private void Open(WorkflowStage stage, ResultsView view)
    {
        Workspace.ResultsView = view;
        Workspace.CurrentStage = stage;
    }
}
