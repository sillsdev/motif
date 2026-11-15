using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Diagnostics;

/// <summary>The Why This Analysis fold of one word; it sits in the word panel's diagnostic slot.</summary>
public sealed partial class WhyThisAnalysisPanel : UserControl
{
    public WhyThisAnalysisPanel() => AvaloniaXamlLoader.Load(this);

    public WhyThisAnalysisPanel(WhyThisAnalysisViewModel model) : this() => DataContext = model;
}
