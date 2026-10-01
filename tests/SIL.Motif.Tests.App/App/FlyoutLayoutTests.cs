using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a pop-up opened from a button shows all of itself: the Help pop-up keeps its title and the start of
/// every line, and the Try a Word tools menu shows each command's label, at both widths the pages are drawn at.
/// A pop-up wider than the presenter allows used to scroll sideways, hiding its left side or its labels.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FlyoutLayoutTests
{
    public static TheoryData<string, int> Flyouts() => new()
    {
        { "help", 1040 }, { "help", 1240 }, { "help-texts", 1040 }, { "tools", 1040 }, { "tools", 1240 },
    };

    [Theory]
    [MemberData(nameof(Flyouts))]
    public void APopUpShowsAllOfItselfWithoutScrollingSideways(string which, int width)
    {
        var failures = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            try
            {
                window.Width = width;
                window.Height = 780;
                Button button;
                if (which == "tools")
                {
                    workspace.Context.TryWord("matinlu");
                    await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                    workspace.CurrentPage = WorkspacePage.TryAWord;
                    PageScreenshots.Settle(window);
                    button = Visible<Button>(window).Single(candidate =>
                        AutomationProperties.GetName(candidate) == "Try a Word tools");
                }
                else
                {
                    workspace.CurrentPage = which == "help" ? WorkspacePage.Overview : WorkspacePage.Texts;
                    PageScreenshots.Settle(window);
                    button = window.FindControl<Button>("HelpButton")!;
                }
                HeadlessClick.Click(window, button, which);
                for (var pass = 0; pass < 50 && button.Flyout?.IsOpen != true; pass++)
                {
                    await Task.Yield();
                    PageScreenshots.Settle(window);
                }
                Assert.True(button.Flyout!.IsOpen, $"the {which} pop-up did not open");
                PageScreenshots.Settle(window);

                var presenter = Assert.Single(Visible<FlyoutPresenter>(window));
                var scroll = presenter.GetVisualDescendants().OfType<ScrollViewer>().First();
                if (scroll.Offset.X > 0.5) failures.Add($"scrolled {scroll.Offset.X:F0} px sideways");
                if (scroll.Extent.Width > scroll.Viewport.Width + 0.5)
                    failures.Add($"{scroll.Extent.Width:F0} px of content in a {scroll.Viewport.Width:F0} px pop-up");
                var shown = new Rect(scroll.TranslatePoint(default, window)!.Value, scroll.Viewport);
                var words = presenter.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text));
                foreach (var text in words)
                {
                    var at = new Rect(text.TranslatePoint(default, window)!.Value, text.Bounds.Size);
                    if (at.Left < shown.Left - 0.5 || at.Right > shown.Right + 0.5)
                        failures.Add($"'{text.Text}' spans x {at.Left:F0}-{at.Right:F0}, outside {shown.Left:F0}-{shown.Right:F0}");
                }
                button.Flyout.Hide();
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(failures.Count == 0, $"{which} at {width}:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static IEnumerable<T> Visible<T>(Window window) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);
}
