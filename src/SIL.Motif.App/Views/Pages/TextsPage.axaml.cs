using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Texts page: one tab per view of the words — the matrix, what changed, the word list, the texts and
/// their words, and the texts read in place — with the run action and the changes not applied yet above them.
/// </summary>
public sealed partial class TextsPage : UserControl
{
    public TextsPage(HandoffWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DataContext = workspace;
        AvaloniaXamlLoader.Load(this);

        Host("CompareHost").Content = new ComparePanel(workspace.Assess.Compare);
        Host("DifferenceHost").Content = new DifferencePanel(workspace.Assess.Difference);
        Host("AssessHost").Content = new AssessPanel(workspace.Assess);
        Host("SelectionHost").Content = new SelectionPanel(workspace.Selection, workspace.Words);
        Host("ResultsInTextHost").Content = new ResultsInTextPanel(workspace.ResultsInText);
    }

    private ContentControl Host(string name) =>
        this.FindControl<ContentControl>(name)
        ?? throw new InvalidOperationException($"TextsPage.axaml has no element named '{name}'.");
}
