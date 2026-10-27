using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TryWordActionFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleResultActionsKeepTheWordAndOpinionsClearAndOpenTiming(bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            var theme = Application.Current!.RequestedThemeVariant;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = 640;
                window.Height = 600;
                SettingsTestAccess.Model(window).ZoomPercent = 150;
                workspace.Context.TryWord("hawajafika");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                PageScreenshots.Settle(window);
                var controls = window.GetVisualDescendants().OfType<Control>().Where(control =>
                    control.IsEffectivelyVisible).ToArray();
                var result = Assert.Single(controls, control =>
                    AutomationProperties.GetAutomationId(control) == AutomationIds.TryWordResult);
                var opinions = Assert.Single(controls.OfType<ItemsControl>(), control =>
                    control.Name == "TryWordPanelResultOpinionsItems");
                var actions = controls.OfType<Button>().Where(button =>
                    AutomationProperties.GetName(button) == MotifAppStrings.TryWordOpenInAnalyzeTextsAccessibleName ||
                    AutomationProperties.GetName(button) == MotifAppStrings.TimingForThisWordAccessibleName ||
                    AutomationProperties.GetAutomationId(button) == AutomationIds.TryWordHandOff).ToArray();
                Assert.Equal(3, actions.Length);
                foreach (var action in actions)
                {
                    var bounds = BoundsIn(action, window);
                    Assert.False(bounds.Intersects(BoundsIn(result, window)), "Action overlaps the word.");
                    Assert.False(bounds.Intersects(BoundsIn(opinions, window)), "Action overlaps opinions.");
                    Assert.Equal(1, action.Opacity);
                    Assert.True(ReachabilityAssertions.IsReachable(action, window));
                }
                var timing = Assert.Single(actions, action => AutomationProperties.GetName(action) == "Timing for this word");
                HeadlessClick.Click(window, timing, "Timing for this word");
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Timing, workspace.CurrentPage);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = theme;
                WindowZoomPolicy.Apply(Application.Current, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
    private static Rect BoundsIn(Control control, Window window) => new(
        control.TranslatePoint(default, window)!.Value,
        control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value);
}
