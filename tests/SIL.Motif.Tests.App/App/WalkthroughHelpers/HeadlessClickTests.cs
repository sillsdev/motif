using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;
using Xunit.Sdk;

using SIL.Motif.Tests.App.Walkthrough;

namespace SIL.Motif.Tests.App.WalkthroughHelpers;

/// <summary>
/// Pins that a walkthrough click either reaches the control it aimed at or fails saying so, rather than landing
/// on whatever took the control's place and leaving the test to time out waiting for an effect that never came.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HeadlessClickTests
{
    [Fact]
    public void AClickOnAButtonThatStaysPutClicksIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (window, _, target) = NewWindow();
            var clicks = 0;
            target.Click += (_, _) => clicks++;
            try
            {
                HeadlessClick.Click(window, target, "Target");
                Assert.Equal(1, clicks);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AClickOnAButtonThatMovesAfterItWasAimedAtFailsNamingTheMiss()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (window, spacer, target) = NewWindow();
            var clicks = 0;
            target.Click += (_, _) => clicks++;
            // The pointer arriving is the last thing before the press, so growing the spacer then moves the aim.
            target.PointerEntered += (_, _) => spacer.Height = 200;
            try
            {
                var failure = Assert.ThrowsAny<XunitException>(() => HeadlessClick.Click(window, target, "Target"));
                Assert.Contains("missed 'Target'", failure.Message, StringComparison.Ordinal);
                Assert.Equal(0, clicks);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    private static (Window Window, Border Spacer, Button Target) NewWindow()
    {
        var spacer = new Border { Height = 0 };
        var target = new Button { Content = "Target", Height = 40 };
        var window = new Window { Width = 400, Height = 400, Content = new StackPanel { Children = { spacer, target } } };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (window, spacer, target);
    }
}
