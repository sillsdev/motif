using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins that a Proposal's actions stay inside the window however much the Inspector says about it.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalsInspectorActionsTests
{
    [Fact]
    public void TryAgainIsInsideTheViewportAt1280By720WithTheLongestRealisticDetails()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(LongStandings());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                LayoutInvariantFixtures.Apply(window, new LayoutCell(1280, 720, 100));
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                model.SelectedProposal = model.Rows.OfType<ProposalCardViewModel>().Single(row => row.ProposalId == "proposal/long");
                PageScreenshots.Settle(window);

                var tryAgain = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == model.SelectedProposal!.InspectorTryAgainAutomationId);
                Assert.True(tryAgain.IsEffectivelyVisible, "Try Again is hidden");
                var box = Box(tryAgain, window);
                Assert.True(box.Top >= 0 && box.Bottom <= window.ClientSize.Height,
                    $"Try Again spans {box.Top} to {box.Bottom} in a {window.ClientSize.Height} high window");
                foreach (var scroller in tryAgain.GetVisualAncestors().OfType<ScrollViewer>())
                {
                    var viewport = Box(scroller, window);
                    Assert.True(box.Top >= viewport.Top - 1 && box.Bottom <= viewport.Bottom + 1,
                        $"Try Again spans {box.Top} to {box.Bottom}, below the {viewport.Top} to {viewport.Bottom} it scrolls in");
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static Rect Box(Control control, Visual window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);

    private static ProposalStandings LongStandings()
    {
        var tried = new TrialSummary("job/long", new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero),
            TrialFreshness.BaselineMoved, TrialWordScope.AllWords,
            new Paired<double?>(60, 64.5, true, true, true, null), new Paired<int>(10, 9, true, true, true, null),
            new Paired<int>(12, 12, true, true, false, null), new Paired<double?>(2500, 2600, true, true, false, null),
            new Paired<int?>(3, 7, true, true, true, null), 4, 3)
        {
            Work = new TrialWork(40, 1000, 1600, 60, [], 0, 0),
        };
        return new ProposalStandings(
        [
            new("proposal/long", "Nasal assimilation before labial stops across prefix boundaries", "proposed",
                ProposalGroup.NeedsAnotherTry, false,
                [new("rule", 4, "4 rules"), new("allomorph", 3, "3 allomorphs"), new("affix", 2, "2 affixes"),
                    new("gloss", 6, "6 glosses"), new("parse-approved", 9, "9 parses approved")],
                TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved), tried,
                ["proposal/other", "proposal/third"]),
            new("proposal/other", "Reduplicated plural stems with a lengthened first vowel", "proposed",
                ProposalGroup.WaitingForYou, false, [new("rule", 1, "1 rule")], TrialStanding.Current, null, []),
            new("proposal/third", "Verbal prefix agreement with the animate class marker", "proposed",
                ProposalGroup.NotTried, false, [new("affix", 1, "1 affix")], TrialStanding.NotTried, null, []),
        ], 3);
    }
}
