using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

public sealed partial class ReviewInspector : UserControl
{
    public ReviewInspector(ReviewPageModel page, IWordPresentationHost presentationHost)
    {
        Page = page;
        DataContext = page;
        PresentationHost = presentationHost;
        AvaloniaXamlLoader.Load(this);
    }

    public ReviewPageModel Page { get; }

    public IWordPresentationHost PresentationHost { get; }
}
