using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Guide;

/// <summary>The Block of a Guide section's query tables; the model is its data context.</summary>
public sealed partial class GuideQueryBlock : UserControl
{
    public GuideQueryBlock() => AvaloniaXamlLoader.Load(this);

    public GuideQueryBlock(GuideQueryBlockViewModel model) : this() => DataContext = model;
}
