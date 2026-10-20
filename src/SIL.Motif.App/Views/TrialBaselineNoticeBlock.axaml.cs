using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// Shows, next to a Trial's result, that the Trial was not compared against a Baseline. Its data context is the
/// notice text; it is hidden when that is <see langword="null"/> or empty.
/// </summary>
public partial class TrialBaselineNoticeBlock : UserControl
{
    /// <summary>Creates the block, hidden until it has a notice.</summary>
    public TrialBaselineNoticeBlock() => AvaloniaXamlLoader.Load(this);
}
