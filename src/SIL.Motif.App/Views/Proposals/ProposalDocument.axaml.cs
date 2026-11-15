using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Displays the independently retained state of one open Proposal.</summary>
public sealed partial class ProposalDocument : UserControl
{
    public ProposalDocument() => AvaloniaXamlLoader.Load(this);

    private void OnAddComment(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProposalDocumentViewModel model) return;
        model.AddCommentCommand.Execute(null);
        if (model.Comments.LastOrDefault() is { } added)
            this.FindControl<ProgressiveItemsControl>("ProposalComments")?.ShowItem(added);
    }
}
