using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Review page: the one list of changes every page adds to, each with a way to take it back, before
/// anything is saved to FieldWorks.
/// </summary>
public sealed partial class ReviewPanel : UserControl, IWorkspacePaneProvider
{
    public ReviewPanel(ReviewPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        _items = this.FindControl<ListBox>("ReviewItems")!;
        DenseTable.Attach(_items);
        PresentationHost = new ReviewWordPresentationHost(page);
        _footer = BuildPane("ApplyFooterTemplate");
        _sidePanel = BuildPane("ReviewNavigatorTemplate");
        InspectorContent = new ReviewInspector(page, PresentationHost);
        _items.SelectionChanged += (_, _) =>
        {
            if (_items.SelectedItem is ReviewListEntryViewModel { Review: { } entry }) Page.SelectEntry(entry);
        };
        foreach (var pane in new[] { _sidePanel, InspectorContent, _footer })
            pane.AddHandler(KeyDownEvent, OnEntryKeyDown, RoutingStrategies.Tunnel);
        AddHandler(MorphemePanel.OverflowRequestedEvent, OnMorphologyRequested);
        AddHandler(KeyDownEvent, OnEntryKeyDown, RoutingStrategies.Tunnel);
        _items.GotFocus += OnReviewGotFocus;
    }

    public ReviewPageModel Page { get; }

    public string SidePanelTitle => "Review changes";

    public Control SidePanel => _sidePanel;

    public Control InspectorContent { get; }

    public Control InspectorFooter => _footer;

    private Control BuildPane(string key)
    {
        var pane = ((IDataTemplate)Resources[key]!).Build(Page)!;
        pane.DataContext = Page;
        return pane;
    }

    [KeyboardShortcutHandler(
        "ReviewChanges:PreviousEntry", "ReviewChanges:NextEntry", "ReviewChanges:KeepEntry", "ReviewChanges:UndoEntry")]
    private void OnEntryKeyDown(object? sender, KeyEventArgs e)
    {
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.ReviewChanges, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.PreviousEntry, KeyboardShortcutBehavior.NextEntry,
                KeyboardShortcutBehavior.KeepEntry, KeyboardShortcutBehavior.UndoEntry]);
        if (entry is null || !KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(e.Source),
                hasFocusedItem: true)) return;
        if ((entry.Behavior is KeyboardShortcutBehavior.PreviousEntry or KeyboardShortcutBehavior.NextEntry) &&
            e.Source is Control source && (ReferenceEquals(source, _items) || source.GetVisualAncestors().Contains(_items)))
            return;
        var command = entry.Behavior switch
        {
            KeyboardShortcutBehavior.PreviousEntry => (System.Windows.Input.ICommand)Page.PreviousCommand,
            KeyboardShortcutBehavior.NextEntry => Page.NextCommand,
            KeyboardShortcutBehavior.KeepEntry => Page.KeepCommand,
            _ => Page.UndoCommand,
        };
        if (!command.CanExecute(null)) return;
        command.Execute(null);
        e.Handled = true;
    }

    public IWordPresentationHost PresentationHost { get; }

    private readonly ListBox _items;
    private readonly Control _footer;
    private readonly Control _sidePanel;

    private void OnReviewGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is not Control source) return;
        // Keep the focused action's list entry active for keyboard navigation.
        var ancestors = source.GetSelfAndVisualAncestors().OfType<Control>().ToArray();
        foreach (var list in ancestors.OfType<ItemsControl>().Where(control => ReferenceEquals(control, _items)))
        {
            var container = ancestors.FirstOrDefault(control => list.IndexFromContainer(control) >= 0);
            if (container is not null)
            {
                KeyboardNavigation.SetTabOnceActiveElement(list, container);
                if (container.DataContext is ReviewListEntryViewModel { Review: { } entry }) Page.SelectEntry(entry);
            }
        }
    }

    private void OnDetailsRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ReviewEntryViewModel entry }) return;
        Page.SelectEntry(entry);
        if (Page.SelectedTableEntry is { } selected)
            _items.ContainerFromItem(selected)?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("denseTableMenu"))?.Flyout?.Hide();
        WorkspaceLayoutHost.RequestPageInspector(this);
    }

    private void OnMorphologyRequested(object? sender, MorphemeOverflowRequestedEventArgs e)
    {
        var entry = Page.Entries.FirstOrDefault(entry => entry.Change?.ReviewMorphologyRequest == e.Request);
        if (entry is null) return;
        Page.SelectEntry(entry);
        WorkspaceLayoutHost.RequestPageInspector(this);
        InspectorContent.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
            button.IsEffectivelyVisible && button.IsEnabled)?.Focus();
        e.Handled = true;
    }
}
