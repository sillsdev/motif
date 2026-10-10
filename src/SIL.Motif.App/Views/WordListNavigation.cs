using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;

namespace SIL.Motif.App.Views;

internal static class WordListNavigation
{
    internal static async Task<WordRow?> RealizeAsync(ItemsControl list, int target,
        CancellationToken cancellationToken)
    {
        if (target < 0 || target >= list.ItemCount) return null;
        for (var pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.ScrollIntoView(target);
            list.UpdateLayout();
            var viewport = list.GetVisualDescendants().OfType<ScrollViewer>()
                .FirstOrDefault(viewer => viewer.Name == "PART_ScrollViewer");
            // A newly measured row can move the estimated end beyond ScrollIntoView's first offset.
            if (target == list.ItemCount - 1) viewport?.ScrollToEnd();
            else if (target == 0) viewport?.ScrollToHome();
            await Dispatcher.UIThread.InvokeAsync(list.UpdateLayout, DispatcherPriority.Loaded);
            cancellationToken.ThrowIfCancellationRequested();
            var container = list.ContainerFromIndex(target);
            var row = container as WordRow ?? container?.GetVisualDescendants().OfType<WordRow>().FirstOrDefault();
            if (row is null) continue;
            row.BringIntoView();
            return row;
        }
        return null;
    }
}
