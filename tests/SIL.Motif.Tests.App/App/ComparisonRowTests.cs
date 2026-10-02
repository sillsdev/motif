using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;
using RowView = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComparisonRowTests
{
    [Fact]
    public void MixedOpinionHeadlineAndQualificationFitTheirMeaningColumn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var comparison = new WordComparison(ProjectStanding.Candidate, WordRowOutcome.Different,
                "disapproved-rebuilt", "Rebuilt an analysis you Disapproved", WordRowTone.Problem)
            {
                Detail = "Your undecided analysis wasn't built",
            };
            var model = new WordRowViewModel(new("mixed", comparison.Outcome, comparison.Headline, comparison.Tone)
            {
                MeaningCode = comparison.MeaningCode, MeaningDetail = comparison.Detail, Comparison = comparison,
                Opinion = ProjectStanding.Candidate,
            });
            var row = new RowView { Row = model };
            var host = new StackPanel { Children = { row } };
            Grid.SetIsSharedSizeScope(host, true);
            var window = new Window { Content = host, Width = 1240, Height = 400, RequestedThemeVariant = ThemeVariant.Light };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var meaning = row.GetVisualDescendants().OfType<Control>()
                    .First(control => control.Classes.Contains("wordRowMeaning"));
                var headline = meaning.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == comparison.Headline && text.IsEffectivelyVisible);
                var detail = meaning.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == comparison.Detail);
                Assert.True(headline.IsEffectivelyVisible);
                Assert.True(detail.IsEffectivelyVisible);
                Assert.True(headline.Bounds.Width <= meaning.Bounds.Width,
                    $"Headline width {headline.Bounds.Width} exceeds column width {meaning.Bounds.Width}.");
                Assert.True(detail.Bounds.Width <= meaning.Bounds.Width);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }
}
