using Avalonia.Controls;
using Avalonia.Threading;

namespace SIL.Motif.App.Views;

internal static class ProgressivePanel
{
    private const int PageSize = 20;

    public static void Populate<T>(
        Panel panel,
        IReadOnlyList<T> items,
        Func<T, int, Control> create,
        Func<int, int, Control?>? createFooter = null)
        => PopulatePages(panel, items.Count, (offset, count) => Enumerable.Range(offset, count)
            .Select(index => create(items[index], index)).ToArray(), createFooter);

    public static void PopulatePages(
        Panel panel,
        int total,
        Func<int, int, IReadOnlyList<Control>> createPage,
        Func<int, int, Control?>? createFooter = null)
    {
        Show(0);

        void Show(int offset)
        {
            panel.Children.Clear();
            if (offset > 0) AddNavigation($"Previous {PageSize}", offset - PageSize, false);
            var count = Math.Min(PageSize, total - offset);
            foreach (var control in createPage(offset, count)) panel.Children.Add(control);
            var remaining = total - offset - PageSize;
            if (remaining > 0) AddNavigation($"Show {Math.Min(PageSize, remaining)} more", offset + PageSize, true);
            if (createFooter?.Invoke(offset, count) is { } footer) panel.Children.Add(footer);
        }

        void AddNavigation(string label, int offset, bool forward)
        {
            var button = new Button { Content = label, Tag = forward, Classes = { "filterChip" } };
            button.Click += (_, _) =>
            {
                var restoreFocus = button.IsFocused;
                Show(offset);
                if (restoreFocus) Dispatcher.UIThread.Post(() =>
                {
                    var buttons = panel.Children.OfType<Button>().Where(candidate => candidate.Tag is bool).ToArray();
                    var target = buttons.FirstOrDefault(candidate => Equals(candidate.Tag, forward)) ?? buttons.FirstOrDefault();
                    target?.BringIntoView();
                    target?.Focus();
                });
            };
            panel.Children.Add(button);
        }
    }
}
