using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsListsPanelTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void RowsShowOnlyStatusesThatVaryWithinTheSelectedList()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([
                Word("approved-empty", "no-analysis", ProjectStanding.Approved),
                Word("approved-timeout", "timed-out", ProjectStanding.Approved),
                Word("candidate-capped", "capped", ProjectStanding.Candidate),
            ]);
            var lists = new TextsListsViewModel(compare);
            var panel = new TextsListsPanel(lists);
            var window = new Window { Content = panel, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<VerdictChip>(), chip => chip.IsVisible);

                lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Stopped at a limit"));
                window.UpdateLayout();

                var visible = window.GetLogicalDescendants().OfType<VerdictChip>()
                    .Where(chip => chip.IsVisible).ToArray();
                Assert.Equal(2, visible.Length);
                Assert.All(visible, chip => Assert.NotEqual("Stopped", chip.Text));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static AssessWordRowViewModel Word(string form, string outcome, string standing) =>
        new(new AssessmentWordResult(form, outcome, outcome is "timed-out" or "capped", "Search completed", 1, null)
        {
            ProjectStanding = standing,
            OccurrenceCount = 1,
        });
}
