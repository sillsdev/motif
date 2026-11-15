using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewMeasuresPageTests
{
    [Fact]
    public void UnsupportedCoverageAndAmbiguityDetailsStayOutOfProgress()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var overview = SampleOverview.Populated() with
            {
                Ambiguity = new OverviewAmbiguity(40, 38, 1.5, 1, [new("bata", 6, 9)]),
            };
            fake.OverviewCompletesWith(overview);
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var window = new MotifTestWindow { Width = 1000, Height = 700, Content = new ProgressPage(model) };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var text = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(item => item.IsEffectivelyVisible).Select(item => item.Text).ToArray();
                Assert.Same(overview, model.Health.Overview);
                Assert.Equal("bata", Assert.Single(model.Health.Overview!.Ambiguity!.Top).Word);
                Assert.DoesNotContain(text, item => item?.Contains("bata", StringComparison.Ordinal) == true);
                Assert.DoesNotContain(text, item => item?.Contains("Text Coverage", StringComparison.Ordinal) == true);
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
