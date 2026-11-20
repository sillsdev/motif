using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Guide;

/// <summary>The Tables of a Guide section's query tables; the model is its data context.</summary>
public sealed partial class GuideQueryTables : UserControl
{
    public GuideQueryTables() => AvaloniaXamlLoader.Load(this);

    public GuideQueryTables(GuideQueryTablesViewModel model) : this() => DataContext = model;
}
