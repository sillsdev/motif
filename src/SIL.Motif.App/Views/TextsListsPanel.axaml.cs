using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

public sealed partial class TextsListsPanel : UserControl
{
    public TextsListsPanel(TextsListsViewModel lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        Lists = lists;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        var scroll = this.FindControl<ScrollViewer>("TextsListsScroll")!;
        var host = this.FindControl<Grid>("TextsListsHost")!;
        var controls = new Control[]
        {
            this.FindControl<ItemsControl>("TextsListsPanelListsItems")!,
            this.FindControl<DockPanel>("ListHeader")!,
            this.FindControl<WrapPanel>("ListControls")!,
        };
        void FitHeight()
        {
            if (!this.TryFindResource("Component.TextsPage.ListWordsMinHeight", out var resource) ||
                resource is not double minimum) return;
            var controlsHeight = controls.Where(control => control.IsVisible).Sum(control => control.Bounds.Height);
            host.Height = Math.Max(scroll.Bounds.Height, minimum + controlsHeight + controls.Length * host.RowSpacing +
                host.Margin.Top + host.Margin.Bottom);
        }
        scroll.SizeChanged += (_, _) => FitHeight();
        foreach (var control in controls) control.SizeChanged += (_, _) => FitHeight();
        FitHeight();
        PresentationHost = new CompareWordPresentationHost(lists.Compare,
            this.FindControl<ListBox>("TextsListsPanelWordsItems")!, WordListOwner.Lists);
        _ = new VisibleControlLifetime(this, () => Lists.Compare.ShowVisibleRows(this),
            () => Lists.Compare.ReleaseVisibleRows(this));
    }

    public TextsListsViewModel Lists { get; }

    public IWordPresentationHost PresentationHost { get; }
}
