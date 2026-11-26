using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Guide;

/// <summary>The interlinear examples of a Guide section, aligned in a monospace grid; the model is its data context.</summary>
public sealed partial class GuideInterlinear : UserControl
{
    public GuideInterlinear() => AvaloniaXamlLoader.Load(this);

    public GuideInterlinear(GuideInterlinearViewModel model) : this() => DataContext = model;
}
