using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalsRequestsLayoutTests
{
    [Fact]
    public void AskingThenOpeningProposalsShowsTheNewRequest()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            ProposalsCaptureFixture.Configure(fake);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await AskAssistantAction.RunAsync(workspace.Context, new AskAssistantTarget("word", "kita", "kita", "Check."));
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                Assert.Equal("Word: kita", Assert.Single(workspace.PageModel<ProposalsPageModel>().Requests.Rows).TargetText);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "Word: kita");
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ManyRequestsScrollWhileProposalsRemainReachable()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            ProposalsCaptureFixture.Configure(fake);
            fake.ListRequestsCompletesWith(Enumerable.Range(0, 12).Select(i => new RequestItem(
                $"request/{i}", "word", $"word{i}", $"word{i}", "Check.", "open",
                "2026-01-01T00:00:00Z", null, null)).ToArray());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                foreach (var cell in LayoutInvariantFixtures.QuickCells.Append(new LayoutCell(900, 600, 100)))
                {
                    LayoutInvariantFixtures.Apply(window, cell);
                    PageScreenshots.Settle(window);
                    var scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
                        .Single(control => control.Name == "ProposalsRequestsScroll");
                    var proposals = window.GetVisualDescendants().OfType<ListBox>()
                        .Single(control => control.Name == "ProposalsRowsItems");
                    Assert.True(scroller.Viewport.Height >= 20, $"{cell}: Request viewport is too small: {scroller.Viewport.Height}");
                    Assert.True(scroller.Extent.Height > scroller.Viewport.Height, $"{cell}: Requests do not scroll");
                    Assert.True(proposals.IsEffectivelyVisible && proposals.Bounds.Height > 0, $"{cell}: no Proposal list");
                    var bounds = new Rect(proposals.Bounds.Size).TransformToAABB(proposals.TransformToVisual(window)!.Value);
                    Assert.True(bounds.Top < window.ClientSize.Height && bounds.Bottom <= window.ClientSize.Height + 1,
                        $"{cell}: Proposal list is outside the window: {bounds}");
                    scroller.Offset = new Vector(0, scroller.Extent.Height);
                    Assert.True(scroller.Offset.Y > 0, $"{cell}: Requests cannot scroll");
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }
}
