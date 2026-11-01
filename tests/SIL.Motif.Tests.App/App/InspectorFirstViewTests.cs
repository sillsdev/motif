using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>At a 720-pixel window a selected change that no longer fits shows its Try again whole beside the Inspector footer.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class InspectorFirstViewTests
{
    [Fact]
    public void TryAgainSitsWhollyInsideTheInspectorBodyAtLowResolution()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(ReviewScreenshots.Blocked());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(1280, 720, 100));
                PageScreenshots.Settle(window);
                var page = workspace.PageModel<ReviewPageModel>();
                var noLongerFits = page.ReviewGroups.SelectMany(group => group.Items).Single(change => change.Word == "kitabu");
                page.SelectEntry(Assert.Single(page.Entries, entry => ReferenceEquals(entry.Change, noLongerFits)));
                PageScreenshots.Settle(window);

                var body = Assert.Single(window.GetVisualDescendants().OfType<ReviewInspector>())
                    .GetVisualDescendants().OfType<ScrollViewer>().First();
                var viewport = Box(body, window);
                // Keep and Undo follow Try again and may need a scroll where fonts are taller, so only Try again is pinned.
                var tryAgain = Assert.Single(body.GetVisualDescendants().OfType<Button>(),
                    candidate => AutomationProperties.GetName(candidate) == "Try loading word details again");
                var box = Box(tryAgain, window);
                Assert.True(box.Top >= viewport.Top && box.Bottom <= viewport.Bottom,
                    $"Try again spans {box.Top:0.#} to {box.Bottom:0.#}, outside the body's {viewport.Top:0.#} to {viewport.Bottom:0.#}.");
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static Rect Box(Control control, Visual window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
}
