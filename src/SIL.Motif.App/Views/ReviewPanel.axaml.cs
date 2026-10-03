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
        _groups = this.FindControl<ItemsControl>("ReviewGroups")!;
        _groups.AddHandler(KeyDownEvent, OnReviewKeyDown, RoutingStrategies.Tunnel);
        _groups.GotFocus += OnReviewGotFocus;
    }

    public ReviewPageModel Page { get; }

    private readonly ItemsControl _groups;
    private int _navigationVersion;

    private void OnReviewGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is not Control source) return;
        // The focused action links need their row and group to survive the next layout.
        var ancestors = source.GetVisualAncestors().OfType<Control>().ToArray();
        foreach (var list in ancestors.OfType<ItemsControl>()
            .Where(control => ReferenceEquals(control, _groups) || control.Name == "ReviewPanelItemsItems"))
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
        var groups = _groups;
        var row = source.GetVisualAncestors().OfType<WordRow>().FirstOrDefault();
        if (row is null || !ReferenceEquals(source, row.FindControl<Border>("Body")) ||
            row.FindAncestorOfType<ItemsControl>() is not { Name: "ReviewPanelItemsItems" } list ||
            !KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(source),
                hasFocusedItem: true)) return;
        var rowContainer = row.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(control => list.IndexFromContainer(control) >= 0);
        var groupContainer = list.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(control => groups.IndexFromContainer(control) >= 0);
        if (rowContainer is null || groupContainer is null) return;
        var groupIndex = groups.IndexFromContainer(groupContainer);
        var currentGroupIndex = groupIndex;
        var rowIndex = list.IndexFromContainer(rowContainer);
        if (groupIndex < 0 || rowIndex < 0) return;

        var movePrevious = entry.Behavior is KeyboardShortcutBehavior.PreviousRow or KeyboardShortcutBehavior.FirstItem;
        var moveToBoundary = entry.Behavior is KeyboardShortcutBehavior.FirstItem or KeyboardShortcutBehavior.LastItem;
        var step = movePrevious ? -1 : 1;
        if (moveToBoundary)
        {
            groupIndex = entry.Behavior == KeyboardShortcutBehavior.FirstItem ? 0 : groups.ItemCount - 1;
            rowIndex = entry.Behavior == KeyboardShortcutBehavior.FirstItem
                ? 0
                : ((ReviewChangeGroupViewModel)groups.Items[groupIndex]!).Items.Count - 1;
        }
        else rowIndex += step;
        while (groupIndex >= 0 && groupIndex < groups.ItemCount)
        {
            var count = ((ReviewChangeGroupViewModel)groups.Items[groupIndex]!).Items.Count;
            if (rowIndex >= 0 && rowIndex < count) break;
            groupIndex += step;
            if (groupIndex < 0 || groupIndex >= groups.ItemCount) return;
            rowIndex = step < 0 ? ((ReviewChangeGroupViewModel)groups.Items[groupIndex]!).Items.Count - 1 : 0;
        }

        e.Handled = true;
        var version = ++_navigationVersion;
        var targetGroup = groups.Items[groupIndex];
        if (groups.ContainerFromIndex(groupIndex) is null || groupIndex != currentGroupIndex)
        {
            groups.ScrollIntoView(groupIndex);
            groups.UpdateLayout();
        }
        Dispatcher.UIThread.Post(RealizeRow, DispatcherPriority.Loaded);

        bool IsCurrent() => version == _navigationVersion && TopLevel.GetTopLevel(groups) is not null &&
            groupIndex < groups.ItemCount && ReferenceEquals(groups.Items[groupIndex], targetGroup);

        ItemsControl? ChangeList() => groups.ContainerFromIndex(groupIndex)?.GetVisualDescendants().OfType<ItemsControl>()
            .FirstOrDefault(control => control.Name == "ReviewPanelItemsItems");

        void RealizeRow()
        {
            if (!IsCurrent() || ChangeList() is not { } targetList) return;
            targetList.ScrollIntoView(rowIndex);
            groups.UpdateLayout();
            Dispatcher.UIThread.Post(FocusTarget, DispatcherPriority.Loaded);
        }

        void FocusTarget()
        {
            if (!IsCurrent() || ChangeList() is not { } targetList ||
                targetList.ContainerFromIndex(rowIndex) is not { } container) return;
            var target = container.GetVisualDescendants().OfType<WordRow>().FirstOrDefault();
            if (target is null) return;
            target.FocusRow();
            groups.UpdateLayout();
            ChangeList()?.ScrollIntoView(rowIndex);
            groups.UpdateLayout();
            ChangeList()?.ContainerFromIndex(rowIndex)?.GetVisualDescendants().OfType<WordRow>().FirstOrDefault()?.FocusRow();
        }
    }
}
