using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordRowActionVisibilityTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(640, 600)]
    [InlineData(960, 780)]
    [InlineData(1920, 1080)]
    [InlineData(960, 600)]
    public void ClosedWordRowsShowReachableActionsBeforeHoverOrFocus(int width, int height)
    {
        avalonia.Invoke(() =>
        {
            var key = new WordPresentationKey("actions:kitabu");
            var row = new WordRow
            {
                Data = new WordPresentation(key, 0, WordRowViewModel.NotParsed("kitabu"), WordListOwner.ReadOnly),
                State = new WordInteractionState(key),
            };
            var window = new MotifTestWindow { Content = row, Width = width, Height = height };
            try
            {
                window.Show();
                window.MouseMove(new Point(-10, -10));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.False(row.State.IsOpen);
                Assert.False(row.IsPointerOver);
                Assert.False(row.IsKeyboardFocusWithin);
                foreach (var name in new[] { "Open kitabu in Analyze texts", "Try kitabu in Try a Word" })
                {
                    var action = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                        button => AutomationProperties.GetName(button) == name);
                    Assert.True(action.IsEffectivelyVisible);
                    Assert.Equal(1, action.Opacity);
                    var centre = action.TranslatePoint(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2),
                        window);
                    Assert.NotNull(centre);
                    var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(centre.Value));
                    Assert.True(ReferenceEquals(hit, action) || hit.GetVisualAncestors().Contains(action),
                        $"The closed row's {name} action is clipped or covered.");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }
}
