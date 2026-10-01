using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a tooltip opens where it hides no other control: Refresh's tip clears Help and the notice's
/// buttons, the AI Handoff drag tip clears the files' Copy path buttons, and a Try a Word rule row's or an AI
/// Handoff question's tip clears the rows that follow it, at both widths the pages are drawn at.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TooltipPlacementTests
{
    public static TheoryData<string, int> Owners() => new()
    {
        { "refresh", 1040 }, { "refresh", 1240 }, { "drag", 1040 }, { "drag", 1240 },
        { "rule", 1040 }, { "rule", 1240 }, { "question", 1040 }, { "question", 1240 },
    };

    [Theory]
    [MemberData(nameof(Owners))]
    public void ATooltipHidesNoOtherButton(string which, int width)
    {
        var covered = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            try
            {
                window.Width = width;
                window.Height = 780;
                if (which == "rule")
                {
                    workspace.Context.TryWord("matinlu");
                    await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                }
                workspace.CurrentPage = which switch
                {
                    "refresh" => WorkspacePage.Overview,
                    "rule" => WorkspacePage.TryAWord,
                    _ => WorkspacePage.AiHandoff,
                };
                PageScreenshots.Settle(window);
                var owner = which switch
                {
                    "refresh" => Named(window, "Refresh the project"),
                    "drag" => Named(window, "Drag all AI Handoff files"),
                    "rule" => Visible<Button>(window).First(button => button.Classes.Contains("ruleRow")),
                    _ => Visible<Button>(window).First(button => button.Classes.Contains("handoffQuestion")),
                };
                Assert.NotNull(ToolTip.GetTip(owner));

                ToolTip.SetShowDelay(owner, 0);
                window.MouseMove(owner.TranslatePoint(new Point(owner.Bounds.Width / 2, owner.Bounds.Height / 2), window)!.Value);
                PageScreenshots.Settle(window);
                await Task.Yield();
                PageScreenshots.Settle(window);
                Assert.True(ToolTip.GetIsOpen(owner), $"the {which} tooltip did not open");
                var tip = Assert.Single(Visible<ToolTip>(window));
                var area = Area(tip, window);

                foreach (var other in Visible<Button>(window).Where(button => button != owner &&
                    !button.GetVisualAncestors().Contains(owner) && !owner.GetVisualAncestors().Contains(button)))
                {
                    var name = AutomationProperties.GetName(other) ?? other.Content as string ?? other.GetType().Name;
                    if (Area(other, window).Intersects(area)) covered.Add(name);
                }
                ToolTip.SetIsOpen(owner, false);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(covered.Count == 0, $"the {which} tooltip at {width} covers: {string.Join(", ", covered.Distinct())}");
    }

    private static Rect Area(Visual visual, Window window) =>
        new(visual.TranslatePoint(default, window)!.Value, visual.Bounds.Size);

    private static Button Named(Window window, string name) =>
        Visible<Button>(window).First(button => AutomationProperties.GetName(button) == name);

    private static IEnumerable<T> Visible<T>(Window window) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);
}
