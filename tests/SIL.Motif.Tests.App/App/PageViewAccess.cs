using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.Tests.App;

/// <summary>Reaches a page's view without opening the page; the window builds a page view on its first visit.</summary>
internal static class PageViewAccess
{
    /// <summary>The view of <paramref name="page"/>, built now if nobody has visited the page yet.</summary>
    internal static Control PageView(this MainWindow window, WorkspacePage page) => window.EnsurePageView(page);

    /// <summary>The single <typeparamref name="TPage"/> in the window, built now if its page was never visited.</summary>
    internal static TPage PageView<TPage>(this MainWindow window) where TPage : Control =>
        (TPage)window.PageView(PageOf(typeof(TPage)));

    /// <summary>All page views, for a test that walks every page; costs what the window cost before views were lazy.</summary>
    internal static void EnsureAllPageViews(this MainWindow window)
    {
        foreach (var entry in PageRegistry.Entries) window.PageView(entry.Page);
    }

    private static WorkspacePage PageOf(Type viewType) => viewType.Name switch
    {
        nameof(AssistantConnectionsPage) => WorkspacePage.AiAssistants,
        _ => Enum.GetValues<WorkspacePage>().Single(page => $"{page}Page" == viewType.Name),
    };
}
