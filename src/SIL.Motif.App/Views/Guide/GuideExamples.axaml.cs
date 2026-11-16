using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Guide;

/// <summary>
/// The "Examples from Your Texts" block of a Grammar Guide section. It shows only what a reviewed source supplied, so
/// while none exists it is not on screen at all: no empty heading, count or placeholder.
/// </summary>
public sealed partial class GuideExamples : UserControl
{
    public GuideExamples() : this(new GrammarGuideExamplesViewModel()) { }

    public GuideExamples(GrammarGuideExamplesViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
    }
}
