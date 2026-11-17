using Avalonia.Automation;
using Avalonia.Controls;
using SIL.Motif.App.Walkthroughs;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReleasePathwayReplayTests
{
    [Fact]
    public void AClickRevealsAnActionBelowTheScrollViewport() => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var clicked = false;
        var action = new Button { Content = "Write" };
        action.Click += (_, _) => clicked = true;
        AutomationProperties.SetAutomationId(action, "replay-action");
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = new ScrollViewer
            {
                Content = new StackPanel { Children = { new Border { Height = 1200 }, action } },
            },
        };
        try
        {
            window.Show();
            PageScreenshots.Settle(window);
            ReleasePathwayReplay.Click(window, "replay-action");
            Assert.True(clicked);
        }
        finally { window.Close(); }
    });
}
