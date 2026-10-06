using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Review page: the one list of changes every page adds to, each with a way to take it back, before
/// anything is saved to FieldWorks.
/// </summary>
public sealed partial class ReviewPanel : UserControl
{
    public ReviewPanel(ReviewPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        _items = this.FindControl<ListBox>("ReviewItems")!;
        _items.AddHandler(KeyDownEvent, OnReviewKeyDown, RoutingStrategies.Tunnel);
        _items.GotFocus += OnReviewGotFocus;
    }

    public ReviewPageModel Page { get; }

    private readonly ListBox _items;
    private int _navigationVersion;

    private void OnReviewGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is not Control source) return;
        // Keep the focused action's list entry active for keyboard navigation.
        var ancestors = source.GetVisualAncestors().OfType<Control>().ToArray();
        foreach (var list in ancestors.OfType<ItemsControl>().Where(control => ReferenceEquals(control, _items)))
        {
            var container = ancestors.FirstOrDefault(control => list.IndexFromContainer(control) >= 0);
            if (container is not null) KeyboardNavigation.SetTabOnceActiveElement(list, container);
        }
    }

    [KeyboardShortcutHandler(
        "ReviewChanges:PreviousRow", "ReviewChanges:NextRow", "ReviewChanges:FirstItem", "ReviewChanges:LastItem")]
    private void OnReviewKeyDown(object? sender, KeyEventArgs e)
    {
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.ReviewChanges, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.PreviousRow, KeyboardShortcutBehavior.NextRow,
                KeyboardShortcutBehavior.FirstItem, KeyboardShortcutBehavior.LastItem]);
        if (entry is null || e.Source is not Control source) return;
        var row = source.GetVisualAncestors().OfType<WordRow>().FirstOrDefault();
        if (row is null || !ReferenceEquals(source, row.FindControl<Border>("Body")) ||
            row.FindAncestorOfType<ListBox>() is not { } list || !ReferenceEquals(list, _items) ||
            !KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(source),
                hasFocusedItem: true)) return;
        var container = row.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(control => list.IndexFromContainer(control) >= 0);
        if (container is null) return;
        var entries = Page.ReviewEntries;
        var changeIndices = entries.Select((item, index) => (item, index))
            .Where(pair => pair.item.IsChangeRow).Select(pair => pair.index).ToArray();
        var currentPosition = Array.IndexOf(changeIndices, list.IndexFromContainer(container));
        if (currentPosition < 0 || changeIndices.Length == 0) return;
        var targetPosition = entry.Behavior switch
        {
            KeyboardShortcutBehavior.FirstItem => 0,
            KeyboardShortcutBehavior.LastItem => changeIndices.Length - 1,
            KeyboardShortcutBehavior.PreviousRow => currentPosition - 1,
            _ => currentPosition + 1,
        };
        if (targetPosition < 0 || targetPosition >= changeIndices.Length) return;
        var targetIndex = changeIndices[targetPosition];
        var targetEntry = entries[targetIndex];

        e.Handled = true;
        var version = ++_navigationVersion;
        list.ScrollIntoView(targetIndex);
        list.UpdateLayout();
        Dispatcher.UIThread.Post(RealizeRow, DispatcherPriority.Loaded);

        bool IsCurrent() => version == _navigationVersion && TopLevel.GetTopLevel(list) is not null &&
            targetIndex < Page.ReviewEntries.Count && ReferenceEquals(Page.ReviewEntries[targetIndex], targetEntry);

        void RealizeRow()
        {
            if (!IsCurrent()) return;
            list.ScrollIntoView(targetIndex);
            list.UpdateLayout();
            Dispatcher.UIThread.Post(FocusTarget, DispatcherPriority.Loaded);
        }

        void FocusTarget()
        {
            if (!IsCurrent() || list.ContainerFromIndex(targetIndex) is not { } container) return;
            var target = container.GetVisualDescendants().OfType<WordRow>().FirstOrDefault();
            if (target is null) return;
            target.FocusRow();
            list.UpdateLayout();
            list.ScrollIntoView(targetIndex);
            list.UpdateLayout();
            list.ContainerFromIndex(targetIndex)?.GetVisualDescendants().OfType<WordRow>().FirstOrDefault()?.FocusRow();
        }
    }
}
