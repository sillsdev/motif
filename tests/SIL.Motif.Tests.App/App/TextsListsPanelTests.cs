using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsListsPanelTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void EachListedWordIsTheOneWordRow_WithEveryMarkInItsColumn()
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
                lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Stopped at a limit"));
                window.UpdateLayout();

                var box = panel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Words in the selected list");
                var rows = box.GetVisualDescendants().OfType<WordRow>().ToArray();
                Assert.Equal(["approved-timeout", "candidate-capped"], rows.Select(row => row.Row!.Word).Order());
                Assert.All(rows, row =>
                {
                    Assert.Equal("lists", row.List);
                    Assert.True(row.ShowsTick);
                    var chips = row.GetVisualDescendants().OfType<MarkChip>().Where(chip => chip.IsEffectivelyVisible)
                        .Select(chip => chip.Mark?.Kind).ToArray();
                    Assert.Contains(MarkKind.Outcome, chips);
                    Assert.Contains(MarkKind.Meaning, chips);
                });
                Assert.Single(panel.GetVisualDescendants().OfType<WordRowHeader>());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TickingARowTicksTheWordForTheListsHandoff()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([Word("approved-empty", "no-analysis", ProjectStanding.Approved)]);
            var lists = new TextsListsViewModel(compare);
            var window = new Window { Content = new TextsListsPanel(lists), Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                var row = window.GetVisualDescendants().OfType<WordRow>().Single();
                row.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;

                Assert.True(compare.Words.Single().IsChecked);
                Assert.Equal("Hand off the 1 selected word", lists.HandOffCheckedWordsLabel);
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
