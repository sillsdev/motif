using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// The inspector panel beside the page: one object's breadcrumb, name and sections, from its
/// <see cref="ViewModels.InspectorViewModel"/>. The window places it, and steps it back on Esc.
/// </summary>
public sealed partial class Inspector : UserControl
{
    public Inspector() => AvaloniaXamlLoader.Load(this);
}
