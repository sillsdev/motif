using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FrameBandsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePageKeepsItsMinimumHeightBesideTheNoticeAndTheApplyFooterAtTheSmallestWindowAndZoom(bool withProjectNotice)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create(techDemoNotice: PageScreenshots.FirstRunNotice());
            try
            {
                window.Show();
                workspace.CurrentPage = WorkspacePage.Review;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(640, 600, 150));
                if (withProjectNotice)
                {
                    workspace.Context.TextStyles.MissingFontNotices.Add(
                        new WritingSystemFontNotice("x-test", "The font for this writing system is not installed."));
                    LayoutInvariantEngineTests.Settle(window);
                    Assert.True(window.FindControl<Border>("NoticeBand")!.IsEffectivelyVisible, "A project notice shows.");
                }
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
