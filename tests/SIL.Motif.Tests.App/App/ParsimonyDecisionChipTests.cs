using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the layout of a finding's decision on the rendered page: the decision is the window's chip, and it sits clear of
/// its neighbours on the same line, at least a compact gap away and centred on them vertically. The check reads the
/// visible rendered controls after a layout pass, at the 1040 width the captures use.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ParsimonyDecisionChipTests
{
    // Intent.Space.Compact, the least gap a decision keeps from the text beside it.
    private const double MinimumGap = 4;

    private const double CentreTolerance = 3;

    [Theory]
    [InlineData("pending-keep", false)]
    [InlineData("resurfaced", false)]
    [InlineData("suppressed-with-reason", true)]
    public void TheDecisionIsAChipClearOfItsNeighboursOnTheSameLine(string state, bool suppressedTab)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, state));
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var page = workspace.PageModel<ParsimonyPageModel>();
                if (state == "pending-keep")
                {
                    Assert.True(page.SelectFinding(page.Rows.First(row => !row.IsHeader).Link!));
                    page.ReasonText = "Attested in the field notes.";
                    await page.KeepCommand.ExecuteAsync(null);
                }
                if (suppressedTab) page.IsShowingSuppressed = true;
                PageScreenshots.Settle(window);

                var chip = Assert.Single(VisibleDecisionChips(window));
                var siblings = ((Panel)chip.Parent!).Children.OfType<Control>()
                    .Where(child => child != chip && child.IsEffectivelyVisible && child.Bounds.Width > 0).ToArray();
                Assert.NotEmpty(siblings);
                var chipBox = BoundsIn(chip, window);
                foreach (var sibling in siblings)
                {
                    var box = BoundsIn(sibling, window);
                    var clearLeft = box.Right + MinimumGap <= chipBox.Left;
                    var clearRight = chipBox.Right + MinimumGap <= box.Left;
                    Assert.True(clearLeft || clearRight,
                        $"{state}: the decision chip is {Gap(box, chipBox)} px from '{ContentOf(sibling)}', less than {MinimumGap}.");
                    Assert.True(Math.Abs(box.Center.Y - chipBox.Center.Y) <= CentreTolerance,
                        $"{state}: '{ContentOf(sibling)}' and the decision chip are not centred on one line.");
                }
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static IEnumerable<Control> VisibleDecisionChips(Window window) => window.GetVisualDescendants()
        .OfType<Control>()
        .Where(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == AutomationIds.ParsimonyDecision);

    private static Rect BoundsIn(Control control, Visual window) =>
        new(control.TranslatePoint(new Point(0, 0), window)!.Value, control.Bounds.Size);

    private static string ContentOf(Control control) => control switch
    {
        TextBlock text => text.Text ?? string.Empty,
        ContentControl content => content.Content?.ToString() ?? string.Empty,
        _ => control.GetType().Name,
    };

    private static double Gap(Rect sibling, Rect chip) =>
        Math.Round(Math.Max(chip.Left - sibling.Right, sibling.Left - chip.Right), 1);
}
