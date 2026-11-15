using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>Shows supported effects and the scope and limits of their saved evidence.</summary>
public sealed partial class ProposalEvidenceColumns : UserControl
{
    public ProposalEvidenceColumns() => AvaloniaXamlLoader.Load(this);
}
