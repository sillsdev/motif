using SIL.Motif.App.Services;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProgressPageTests
{
    [Theory]
    [InlineData(640, 400, 1.5, true)]
    [InlineData(1280, 800, 1.0, false)]
    public void PlainNextRowsWrapAndActivateByKeyboardInBothThemes(int width, int height, double scale, bool rtl)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings([
                new ProposalCard("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou, false, [],
                    TrialStanding.Current, null, [])], 0));
            fake.ProgressRequestCountHandler = (_, _) => Task.FromResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(2_000_000)));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new ProgressPage(model);
            var window = new MotifTestWindow
            {
                Width = width, Height = height,
                FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                RequestedThemeVariant = rtl ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = page },
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var buttons = page.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("progressNextRow")).ToArray();
                Assert.Equal(2, buttons.Length);
                var identities = buttons.Select(AutomationProperties.GetAutomationId).ToArray();
                foreach (var identity in identities)
                {
                    context.OpenPage(WorkspacePage.Progress);
                    PageScreenshots.Settle(window);
                    var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item =>
                        AutomationProperties.GetAutomationId(item) == identity);
                    button.BringIntoView();
                    button.Focus();
                    PageScreenshots.Settle(window);
                    Assert.True(button.IsFocused);
                    var bounds = new Rect(button.Bounds.Size).TransformToAABB(button.TransformToVisual(window)!.Value);
                    Assert.InRange(bounds.Left, -1, window.ClientSize.Width);
                    Assert.InRange(bounds.Right, 0, window.ClientSize.Width + 1);
                    Assert.InRange(bounds.Top, -1, window.ClientSize.Height);
                    Assert.InRange(bounds.Bottom, 0, window.ClientSize.Height + 1);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                    Assert.All(button.GetVisualDescendants().OfType<TextBlock>(),
                        text => Assert.Equal(TextWrapping.Wrap, text.TextWrapping));
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Assert.Equal(WorkspacePage.Proposals, context.CurrentPage);
                }
                Assert.Empty(page.GetVisualDescendants().OfType<HealthStrip>());
                Assert.Empty(fake.AssessRequests);
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }
}
