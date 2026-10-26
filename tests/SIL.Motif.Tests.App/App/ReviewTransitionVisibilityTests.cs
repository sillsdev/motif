using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewTransitionVisibilityTests
{
    [Theory]
    [InlineData(ChangeKinds.Approve, true)]
    [InlineData(ChangeKinds.IncorrectSpelling, false)]
    [InlineData(ChangeKinds.AddCandidate, true)]
    public void ResponsiveWordRowsPreserveTheirSemanticTransitionAlternative(string kind, bool hasMarks)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var reading = ReviewScreenshots.Reading(("ki-", "7"), ("tabu", "book"));
            var change = ReviewScreenshots.Change("kitabu", kind,
                kind == ChangeKinds.Approve ? "analysis/one" : null,
                new ReviewAnalysis(reading, ReadingGrade.Candidate, kind == ChangeKinds.Approve, true));
            var fake = ReviewDenseTableTests.MixedChanges();
            fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [change],
                [new ChangeFit(change.ChangeId, true, [])]));
            fake.Review = fake.Review! with
            {
                Entries = fake.Review.Entries.Select(entry => entry is WordChangeEntry word
                    ? word with { Change = change } : entry).ToArray(),
            };
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                window.Width = 1920;
                window.Height = 1080;
                workspace.CurrentPage = WorkspacePage.Review;
                await workspace.PageModel<ReviewPageModel>().LoadReviewAsync();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                foreach (var (width, compact) in new[] { (1920, false), (1040, true), (1920, false) })
                {
                    window.Width = width;
                    PageScreenshots.Settle(window);
                    Assert.Same(panel, Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>()));
                    Assert.True(list.IsEffectivelyVisible);
                    var entry = Assert.Single(list.ItemsSource!.OfType<ReviewListEntryViewModel>(),
                        item => item.Change?.ChangeId == change.ChangeId);
                    Assert.Equal(hasMarks, entry.Change!.HasOpinionTransitionMarks);
                    list.ScrollIntoView(entry);
                    PageScreenshots.Settle(window);
                    var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(entry));
                    Assert.True(row.IsEffectivelyVisible && row.Bounds.Width > 0 && row.Bounds.Height > 0);
                    var frame = Assert.Single(row.GetVisualDescendants().OfType<Border>(),
                        border => border.Classes.Contains("denseTableRow"));
                    var tracks = Assert.Single(frame.GetVisualDescendants().OfType<Grid>(),
                        grid => DenseTable.GetColumns(grid) is not null);
                    var header = panel.FindControl<Grid>("ReviewTableHeader")!;
                    Assert.Equal(compact, header.ColumnDefinitions[1].Width.Value == 0);
                    Assert.Equal(header.ColumnDefinitions.Select(column => column.Width),
                        tracks.ColumnDefinitions.Select(column => column.Width));
                    var identity = Assert.Single(frame.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        text => text.Text == "kitabu" && text.IsEffectivelyVisible);
                    Assert.True(identity.Bounds.Width > 0 && identity.Bounds.Height > 0);
                    var marks = Assert.Single(frame.GetVisualDescendants().OfType<StackPanel>(),
                        stack => stack.Children.OfType<OpinionMark>().Count() == 2);
                    Assert.Equal(new[] { entry.Change.NowOpinionMark, entry.Change.AfterOpinionMark },
                        marks.Children.OfType<OpinionMark>().Select(mark => mark.Kind));
                    var fallback = Assert.Single(frame.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        text => text.Text == entry.Change.TransitionText);
                    Assert.Equal(!compact && hasMarks, marks.IsEffectivelyVisible);
                    Assert.Equal(!compact && !hasMarks, fallback.IsEffectivelyVisible);
                    if (compact)
                    {
                        Assert.All(tracks.Children.OfType<Control>().Where(cell => Grid.GetColumn(cell) == 1),
                            cell => Assert.False(cell.IsEffectivelyVisible));
                    }
                    else
                    {
                        Control shown = hasMarks ? marks : fallback;
                        Assert.True(shown.Bounds.Width > 0 && shown.Bounds.Height > 0);
                        var bounds = new Rect(shown.TranslatePoint(default, window)!.Value, shown.Bounds.Size);
                        Assert.True(new Rect(window.ClientSize).Intersects(bounds));
                    }
                }
                Assert.Empty(fake.ReviewMarkRequests);
                Assert.Empty(fake.PendingPutRequests);
                Assert.Empty(fake.PendingRemoveRequests);
                Assert.Empty(fake.RemoveOperationsRequests);
                Assert.Empty(fake.DiscardDraftRequests);
                Assert.Empty(fake.ApplyPendingRequests);
                Assert.Empty(fake.ApplyProposalRequests);
            }
            finally
            {
                try { await workspace.DisposeAsync(); }
                finally { window.Close(); }
            }
        }, TimeSpan.FromMinutes(3));
    }
}
