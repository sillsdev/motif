using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FrameBandsTests
{
    [Fact]
    public void ThePageKeepsItsMinimumHeightBesideTheNoticeAndTheApplyFooterAtTheSmallestWindowAndZoom()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create(techDemoNotice: PageScreenshots.FirstRunNotice());
            try
            {
                window.Show();
                workspace.CurrentPage = WorkspacePage.Review;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(640, 600, 150));
                var minimum = (double)window.FindResource("Component.Shell.WorkingPageMinHeight")!;

                var notice = window.FindControl<Border>("TechDemoNotice")!;
                Assert.True(notice.IsEffectivelyVisible, "The first-run notice shows.");
                var inspector = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!
                    .FindControl<Border>("InspectorPane")!;
                Assert.False(inspector.IsVisible, "The Inspector is collapsed.");
                var page = window.FindControl<Panel>("PageHost")!;
                Assert.True(page.Bounds.Height >= minimum - 0.5,
                    $"The page is {page.Bounds.Height} tall; the minimum is {minimum}.");
                var buttons = notice.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.All(buttons, button => Assert.True(button.IsEffectivelyVisible));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }
}
