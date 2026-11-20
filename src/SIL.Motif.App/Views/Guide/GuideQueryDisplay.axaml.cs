using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Guide;

/// <summary>The Display of a Guide section's query tables; the model is its data context.</summary>
public sealed partial class GuideQueryDisplay : UserControl
{
    public GuideQueryDisplay() => AvaloniaXamlLoader.Load(this);

    public GuideQueryDisplay(GuideQueryDisplayViewModel model) : this() => DataContext = model;
}
