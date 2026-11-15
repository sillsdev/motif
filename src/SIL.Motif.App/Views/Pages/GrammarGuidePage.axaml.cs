using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Preview;
using SIL.Motif.App.ViewModels;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Views;

/// <summary>The project's Grammar Guide.</summary>
public sealed partial class GrammarGuidePage : UserControl
{
    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public GrammarGuidePage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new GrammarGuidePageModel(owner))) { }

    public GrammarGuidePage(GrammarGuidePageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        void ShowDiscussion()
        {
            var host = this.FindControl<ContentControl>("DiscussionHost")!;
            if (model.ShownRecord is not { } shown)
            {
                host.Content = null;
                return;
            }
            var discussion = GdrDiscussion.From(shown);
            host.Content = new RichTextField(discussion.Document)
            {
                WritingSystems = discussion.WritingSystems,
                StyleResolver = discussion.Styles,
                IsReadOnly = true,
            };
        }
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GrammarGuidePageModel.ShownRecord)) ShowDiscussion();
        };
        ShowDiscussion();
    }
}
