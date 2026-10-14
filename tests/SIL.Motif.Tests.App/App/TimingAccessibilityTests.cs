using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingAccessibilityTests
{
    [Fact]
    public void RuleSelectorsNameTheirRuleAndOpenItsRecordedWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.CurrentPage = WorkspacePage.Timing;
                PageScreenshots.Settle(window);
                var buttons = window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("timingRuleRow") && button.IsEffectivelyVisible).ToArray();
                Assert.True(buttons.Length >= 2);
                foreach (var button in buttons)
                {
                    var row = Assert.IsType<TimingRuleRow>(button.DataContext);
                    Assert.Equal($"Show words timed under {row.Share.Label}",
                        ControlAutomationPeer.CreatePeerForElement(button)!.GetName());
                }
                var first = buttons[0];
                var chosen = Assert.IsType<TimingRuleRow>(first.DataContext);
                first.BringIntoView();
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, first, $"Show words timed under {chosen.Share.Label}");
                PageScreenshots.Settle(window);
                Assert.Equal(chosen.Share.Label, workspace.PageModel<TimingPageModel>().SelectedRuleName);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }
}
