using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Captures the top bar's project chip, its ≡ menu and the project card, narrow and wide, in both themes.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TopBarScreenshots
{
    [ScreenshotFact]
    public void CaptureTheTopBarNarrowWithTheCardOpenAndWide()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.CurrentPage = WorkspacePage.Overview;
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    LayoutInvariantFixtures.Apply(window, new LayoutCell(640, 600, 150));
                    PageScreenshots.Settle(window);
                    PageScreenshots.Save(window, Path.Combine(folder, $"topbar-narrow-640x600-150-{theme}.png"));

                    if (theme == "light")
                    {
                    var menuButton = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single();
                    menuButton.IsSubMenuOpen = true;
                    window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen = true;
                    PageScreenshots.Settle(window);
                    PageScreenshots.Save(window, Path.Combine(folder, $"topbar-narrow-menu-640x600-150-{theme}.png"));
                    window.FindControl<MenuItem>("EditMenu")!.IsSubMenuOpen = false;
                    menuButton.IsSubMenuOpen = false;
                    PageScreenshots.Settle(window);
                    }

                    ProjectCardAccess.Open(window);
                    PageScreenshots.Save(window, Path.Combine(folder, $"topbar-narrow-card-640x600-150-{theme}.png"));
                    ProjectCardAccess.Close(window);

                    LayoutInvariantFixtures.Apply(window, new LayoutCell(1240, 780, 100));
                    PageScreenshots.Settle(window);
                    PageScreenshots.Save(window, Path.Combine(folder, $"topbar-wide-1240x780-100-{theme}.png"));
                    ProjectCardAccess.Open(window);
                    PageScreenshots.Save(window, Path.Combine(folder, $"topbar-wide-card-1240x780-100-{theme}.png"));
                    ProjectCardAccess.Close(window);
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
