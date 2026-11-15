using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GrammarGuideLayoutTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void LongRecordTitlesWrapInsideTheNarrowWindow(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var title = string.Join(" ", Enumerable.Repeat("A long grammar record title", 8));
            var fake = new FakeCommandClient();
            fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Success(
                new(true, [new GdrSummary("gdr/1", "1", title, "")])));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new GrammarGuidePageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new GrammarGuidePage(model);
            var window = new MotifTestWindow
            {
                Width = 640,
                Height = 600,
                Content = new LayoutTransformControl
                {
                    LayoutTransform = new ScaleTransform(zoom / 100d, zoom / 100d),
                    Child = page,
                },
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var label = Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == title);
                Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                var left = label.TranslatePoint(default, window)!.Value;
                var right = label.TranslatePoint(new Point(label.Bounds.Width, 0), window)!.Value;
                Assert.InRange(left.X, 0, window.ClientSize.Width);
                Assert.InRange(right.X, 0, window.ClientSize.Width + 1);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }
}
