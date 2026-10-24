using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>The project card that opens from the top bar's chip and tells the story of the open project.</summary>
public sealed partial class ProjectCardView : UserControl
{
    /// <summary>Builds the card; the window gives it the workspace as its data context.</summary>
    public ProjectCardView() => AvaloniaXamlLoader.Load(this);
}
