using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Project choice and Baseline state, bound to its own <see cref="Project"/> and <see cref="Baseline"/>.</summary>
public sealed partial class ProjectPanel : UserControl
{
    public ProjectPanel(ProjectViewModel project, BaselineViewModel baseline)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(baseline);
        Project = project;
        Baseline = baseline;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public ProjectViewModel Project { get; }

    public BaselineViewModel Baseline { get; }
}
