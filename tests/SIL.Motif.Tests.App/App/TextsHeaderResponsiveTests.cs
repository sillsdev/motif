using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsHeaderResponsiveTests
{
    [Theory]
    [InlineData(640, 600, 100)]
    [InlineData(640, 600, 150)]
    [InlineData(640, 900, 100)]
    public void TabsAndParseActionStayVisibleAndRespondToPointerInput(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                var settings = SettingsTestAccess.Model(window);
                settings.ZoomPercent = zoom;
                window.Width = width;
                window.Height = height;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                Assert.Equal(width, window.ClientSize.Width);
                Assert.Equal(height, window.ClientSize.Height);
                var texts = Assert.Single(window.GetVisualDescendants().OfType<TextsPage>());
                var tabs = texts.GetVisualDescendants().OfType<Button>()
                    .Where(button => AutomationProperties.GetName(button) is
                        "Matrix tab" or "Analyze texts tab" or "Lists tab" or "What changed tab")
                    .ToDictionary(button => AutomationProperties.GetName(button)!, StringComparer.Ordinal);
                var parse = texts.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Parse all words in the Selection");

                Assert.Equal(4, tabs.Count);
                foreach (var control in tabs.Values.Append(parse)) AssertInside(window, control);
                AssertNoHeaderOverlap(window, tabs.Values.Append(parse));
                if (zoom == 150)
                {
                    var tabsBottom = tabs.Values.Max(button =>
                        button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value.Y);
                    var actionTop = parse.TranslatePoint(default, window)!.Value.Y;
                    Assert.True(actionTop >= tabsBottom,
                        $"Parse all words starts at {actionTop}, before the wrapped tabs end at {tabsBottom}.");
                }

                foreach (var (name, tab) in new[]
                    {
                        ("Matrix tab", TextsTab.Matrix),
                        ("Analyze texts tab", TextsTab.AnalyzeTexts),
                        ("Lists tab", TextsTab.Lists),
                        ("What changed tab", TextsTab.WhatChanged),
                    })
                {
                    HeadlessClick.Click(window, tabs[name], name);
                    Assert.Equal(tab, workspace.PageModel<TextsPageModel>().Tab);
                }

                HeadlessClick.Click(window, parse, "Parse all words in the Selection");
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void WideTextsHeaderKeepsTabsAndActionsOnOneRow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = 1920;
                window.Height = 1080;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                var texts = Assert.Single(window.GetVisualDescendants().OfType<TextsPage>());
                var controls = texts.GetVisualDescendants().OfType<Button>()
                    .Where(button => AutomationProperties.GetName(button) is
                        "Matrix tab" or "Analyze texts tab" or "Lists tab" or "What changed tab" or
                        "Parse all words in the Selection").ToArray();

                Assert.Equal(5, controls.Length);
                Assert.All(controls, control => AssertInside(window, control));
                Assert.All(controls, control =>
                    Assert.InRange(Math.Abs(control.TranslatePoint(default, window)!.Value.Y -
                        controls[0].TranslatePoint(default, window)!.Value.Y), 0, 3));
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void AssertInside(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible, $"{AutomationProperties.GetName(control)} is hidden.");
        Assert.True(control.IsHitTestVisible, $"{AutomationProperties.GetName(control)} cannot receive pointer input.");
        var first = control.TranslatePoint(default, window)!.Value;
        var last = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(first.X >= -1 && first.Y >= -1 && last.X <= window.ClientSize.Width + 1 &&
            last.Y <= window.ClientSize.Height + 1,
            $"{AutomationProperties.GetName(control)} is outside the client area: {first} to {last}.");
    }

    private static void AssertNoHeaderOverlap(Window window, IEnumerable<Button> controls)
    {
        var bounds = controls.Select(control => (Name: AutomationProperties.GetName(control)!,
            Rect: new Rect(control.TranslatePoint(default, window)!.Value,
                control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value))).ToArray();
        for (var left = 0; left < bounds.Length; left++)
        for (var right = left + 1; right < bounds.Length; right++)
            Assert.False(bounds[left].Rect.Intersects(bounds[right].Rect),
                $"{bounds[left].Name} overlaps {bounds[right].Name}: {bounds[left].Rect} / {bounds[right].Rect}.");
    }
}
