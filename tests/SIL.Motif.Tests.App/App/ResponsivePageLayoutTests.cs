using Avalonia;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ResponsivePageLayoutTests(ResponsivePageLayoutTests.SampleWindow fixture)
    : IClassFixture<ResponsivePageLayoutTests.SampleWindow>
{
    public static IEnumerable<object[]> Routes
    {
        get
        {
            foreach (var entry in PageRegistry.Entries)
            {
                var routes = PageScreenshots.Views().Where(view => view.Page == entry.Page).ToArray();
                if (routes.Length == 0)
                    throw new InvalidOperationException($"No responsive fixture is declared for {entry.Page}.");
                foreach (var view in routes) yield return [view.Name, view.Page, view.Tab];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void StandardPagesFitTheExistingResponsiveCheckpoints(string scene, WorkspacePage page, TextsTab tab)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await fixture.GetAsync();
            try
            {
                workspace.Context.Diagnostics = true;

                workspace.CurrentPage = page;
                if (page == WorkspacePage.Texts)
                    await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                PageScreenshots.AssertSceneHasExpectedErrorState(window, workspace, scene);
                LayoutAssertions.AssertResponsiveLayouts(window);
                MatrixCaptureState.Assert(window, workspace, page, page == WorkspacePage.Texts ? tab : null);
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            }
        }, TimeSpan.FromMinutes(3));
    }

    /// <summary>Shares the unchanged sample between page layout checks in this class.</summary>
    public sealed class SampleWindow : IDisposable
    {
        private WorkspaceShellViewModel? _workspace;
        private MainWindow? _window;

        internal async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> GetAsync()
        {
            if (_window is null)
            {
                (_workspace, _window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
                WindowLeakNetAttribute.Keep(_window);
            }
            return (_workspace!, _window);
        }

        public void Dispose()
        {
            if (_window is null) return;
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                _window.Close();
                await _workspace!.DisposeAsync();
            }, TimeSpan.FromMinutes(1));
        }
    }
}
