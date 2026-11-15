using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ChapterProgressTests
{
    [Fact]
    public void ParseCompletionAndApprovedWordformsDoNotBecomeChapterOrWeeklyApproval()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var overview = SampleOverview.Populated();
            fake.OverviewCompletesWith(overview with
            {
                WordCoveragePercent = 100,
                TextCoverage = overview.TextCoverage with { ParsedOccurrences = 611, OccurrenceCoveragePercent = 100 },
                Accuracy = overview.Accuracy with { ApprovedWordsKept = 142, ApprovedWordCount = 142 },
            });
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProgressPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var window = new MotifTestWindow { Width = 640, Height = 480, Content = new ProgressPage(model) };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var text = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(item => item.IsEffectivelyVisible).Select(item => item.Text ?? string.Empty).ToArray();
                foreach (var unsupported in new[] { "%", "chapter", "This Week", "approved", "steps", "NaN" })
                    Assert.DoesNotContain(text, item => item.Contains(unsupported, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("chapter"));
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
