using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Project choice, Baseline state, and project history, bound to its own child view models.</summary>
public sealed partial class ProjectPanel : UserControl
{
    public ProjectPanel(ProjectViewModel project, BaselineViewModel baseline, ProjectHistoryViewModel history)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(history);
        Project = project;
        Baseline = baseline;
        History = history;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public ProjectViewModel Project { get; }

    public BaselineViewModel Baseline { get; }

    public ProjectHistoryViewModel History { get; }
}
