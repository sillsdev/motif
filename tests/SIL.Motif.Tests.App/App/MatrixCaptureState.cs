using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Checks that a screenshot's visible page and optional Texts tab match its explicit route.</summary>
internal static class MatrixCaptureState
{
    /// <summary>Validates the window, selected page, visible page view and expected Texts tab.</summary>
    internal static void Assert(MainWindow window, WorkspaceShellViewModel workspace,
        WorkspacePage expectedPage, TextsTab? expectedTab)
    {
        Xunit.Assert.Same(workspace, window.DataContext);
        Xunit.Assert.True(expectedPage == workspace.CurrentPage,
            $"Expected CurrentPage {expectedPage}, but the window is on {workspace.CurrentPage}.");
        var selectedPage = workspace.SelectedPage;
        Xunit.Assert.True(expectedPage == selectedPage.Page,
            $"Expected SelectedPage {expectedPage}, but it is {selectedPage.Page}.");
        Xunit.Assert.True(selectedPage.IsCurrent, "The selected page is not marked current.");
        var currentPages = workspace.Pages.Where(page => page.IsCurrent).ToArray();
        Xunit.Assert.True(currentPages.Length == 1,
            $"Expected one current page model, but found {currentPages.Length}.");
        Xunit.Assert.Same(selectedPage, currentPages[0]);
        Xunit.Assert.True((expectedPage == WorkspacePage.Texts) == expectedTab.HasValue,
            "A Texts capture must name its expected tab, and other pages must omit one.");

        var pageHost = Xunit.Assert.IsType<Panel>(window.FindControl<Panel>("PageHost"));
        var pageNames = Enum.GetValues<WorkspacePage>()
            .Select(page => $"{page}Page")
            .ToHashSet(StringComparer.Ordinal);
        var visiblePages = pageHost.Children.OfType<Control>()
            .Where(view => view.Name is { } name && pageNames.Contains(name) && view.IsEffectivelyVisible)
            .ToArray();
        Xunit.Assert.True(visiblePages.Length == 1,
            $"Expected exactly one visible named page in PageHost, but found {visiblePages.Length}.");
        var visiblePage = visiblePages[0];
        Xunit.Assert.True(visiblePage.Name == $"{expectedPage}Page",
            $"Expected {expectedPage}Page to be visible in PageHost, but found {visiblePage.Name}.");
        // Settings binds its view to the window's Settings model, which its page model carries.
        object? expectedContext = selectedPage.Model is SettingsPageModel settings ? settings.Settings : selectedPage.Model;
        Xunit.Assert.True(object.ReferenceEquals(expectedContext, visiblePage.DataContext),
            "The visible page view is not bound to the selected page model.");

        if (expectedTab is { } tab)
        {
            var actualTab = workspace.PageModel<TextsPageModel>().Tab;
            Xunit.Assert.True(tab == actualTab, $"Expected Texts tab {tab}, but the window shows {actualTab}.");
        }
    }

    /// <summary>Checks the explicit route before writing a rendered frame to <paramref name="path"/>.</summary>
    internal static void SaveFrame(MainWindow window, WorkspaceShellViewModel workspace,
        WorkspacePage expectedPage, TextsTab? expectedTab, string path)
    {
        Assert(window, workspace, expectedPage, expectedTab);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
