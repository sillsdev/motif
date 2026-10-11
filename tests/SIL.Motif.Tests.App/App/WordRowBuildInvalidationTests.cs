using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordRowBuildInvalidationTests
{
    [Fact]
    public void ReviewRowCoalescesRepeatedLayoutPublicationsForOneWordRevision()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var row = new WordRow { Data = Presentation("review:layout", WordListOwner.Review) };
            var window = new Window { Content = row, Width = 1000, Height = 180 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var initialBuilds = row.CellBuildCount;

                row.Data = row.Data! with { EvidenceRevision = 1 };
                Assert.Equal(initialBuilds + 1, row.CellBuildCount);
                var revisionBuilds = row.CellBuildCount;

                for (var revision = 1; revision <= 5; revision++)
                    WordListLayoutScope.SetLayout(row, Layout(revision));

                Assert.Equal(revisionBuilds, row.CellBuildCount);
                PageScreenshots.Settle(window);
                Assert.Equal(revisionBuilds + 1, row.CellBuildCount);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ParentLinkedWordListRowDefersNewDataUntilItReachesAVisualRoot()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var row = new WordRow { Data = Presentation("word-list:detached", WordListOwner.WordList) };
            var disconnectedParent = new StackPanel();
            disconnectedParent.Children.Add(row);
            var window = new Window { Content = disconnectedParent, Width = 1000, Height = 180 };
            try
            {
                Assert.True(row.GetVisualAncestors().Any());
                Assert.False(row.IsAttachedToVisualTree());

                row.Data = row.Data! with { EvidenceRevision = 1 };

                Assert.Equal(0, row.CellBuildCount);
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Equal(1, row.CellBuildCount);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void SelectionTogglesStayWithinOneBuildPerShownWordRevision()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var row = new WordRow
            {
                Data = Presentation("word-list:selection", WordListOwner.WordList),
            };
            var window = new Window { Content = row, Width = 1000, Height = 180 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var tick = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>());
                var buildsForShownWord = row.CellBuildCount;

                for (var toggle = 0; toggle < 8; toggle++)
                    tick.IsChecked = toggle % 2 == 0;

                Assert.Equal(false, row.State?.IsChecked);
                Assert.InRange(buildsForShownWord, 1, 2);
                Assert.Equal(buildsForShownWord, row.CellBuildCount);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }

    private static WordPresentation Presentation(string key, WordListOwner owner) => new(
        new WordPresentationKey(key), 0, WordRowViewModel.NotParsed(key), owner);

    private static WordListLayout Layout(long revision)
    {
        var schema = WordListSchemas.Review;
        var tracks = schema.OrderedColumns().Select(column => new WordListTrack(column, 100)).ToImmutableArray();
        return new WordListLayout(schema, true, tracks, 1000, 0, 0, 1000, 1000, 0,
            false, 0, "en", revision);
    }
}
