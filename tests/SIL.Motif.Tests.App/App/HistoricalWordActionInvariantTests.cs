using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class HistoricalWordActionInvariantTests(AvaloniaHeadlessFixture avalonia)
{
    public static TheoryData<int, int, int> Cells => new()
    {
        { 640, 600, 100 }, { 640, 600, 150 }, { 960, 780, 100 }, { 960, 780, 150 },
        { 1920, 1080, 100 }, { 1920, 1080, 150 }, { 1600, 480, 100 }, { 1600, 480, 150 },
    };

    [Theory]
    [MemberData(nameof(Cells))]
    public void ClosedWordActionsAreReachableWithoutHoverOrFocus(int width, int height, int zoom)
    {
        avalonia.Invoke(() =>
        {
            var application = Application.Current!;
            var originalZoom = (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            var key = new WordPresentationKey("invariants:kitabu");
            var row = new WordRow
            {
                Data = new WordPresentation(key, 0, WordRowViewModel.NotParsed("kitabu"), WordListOwner.ReadOnly),
                State = new WordInteractionState(key),
            };
            var window = new Window
            {
                Width = width, Height = height,
                Content = new LayoutTransformControl { LayoutTransform = WindowZoomPolicy.Transform, Child = row },
            };
            try
            {
                WindowZoomPolicy.Apply(application, zoom);
                window.Show();
                window.MouseMove(new Point(-10, -10));
                LayoutInvariantEngineTests.Settle(window);
                Assert.False(row.IsPointerOver);
                Assert.False(row.IsKeyboardFocusWithin);
                Assert.False(row.State.IsOpen);
                var context = new LayoutFindingContext("word/closed", $"{width}x{height}", zoom, "light");
                var findings = ReachabilityAssertions.InspectReachabilityAndTitleBar(window, context);
                foreach (var name in new[] { "Open kitabu in Analyze texts", "Try kitabu in Try a Word" })
                {
                    var action = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                        button => AutomationProperties.GetName(button) == name);
                    Assert.True(action.IsEffectivelyVisible && action.IsEffectivelyEnabled);
                    Assert.DoesNotContain(findings, finding => finding.Control.AutomationName == name);
                }
            }
            finally
            {
                window.Close();
                WindowZoomPolicy.Apply(application, originalZoom);
            }
        });
    }
}
