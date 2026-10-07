using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;
using WordRowHeader = SIL.Motif.App.Controls.WordPresentation.WordRowHeader;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordListSlotTests(AvaloniaHeadlessFixture avalonia)
{
    public static IEnumerable<object[]> ActualPageColumnSets =>
    [
        ["Matrix", WordListSchemas.All, true],
        ["Fix first", WordListSchemas.FixFirst, true],
        ["Timing", WordListSchemas.Timing, true],
        ["Overview", WordListSchemas.Overview, true],
        ["Review", WordListSchemas.Review, true],
        ["What changed", WordListSchemas.WhatChanged, true],
        ["Warnings", WordListSchemas.Warnings, true],
        ["Analyze texts", WordListSchemas.WordList, true],
        ["Matrix without meaning", WordListSchemas.All, false],
    ];

    [Theory]
    [MemberData(nameof(ActualPageColumnSets))]
    public void ActualPageColumnsResolveAndConserveWidthThroughTheProductionSlot(
        string page,
        WordListSchema schema,
        bool showsMeaning)
    {
        avalonia.Invoke(() =>
        {
            var (slot, _, _, window) = Show(width: 1280, schema, showsMeaning);
            try
            {
                var layout = Assert.IsType<WordListLayout>(slot.CurrentLayout);
                var expectedColumns = schema.OrderedColumns(showsMeaning);
                Assert.Equal(expectedColumns, layout.Tracks.Select(track => track.Column));
                Assert.False(layout.UsesHorizontalViewport);
                Assert.Equal(layout.AvailableWidth,
                    layout.OuterChromeWidth + layout.ReservedVerticalScrollbarWidth + layout.ContentWidth,
                    precision: 6);
                var tracksWidth = layout.Tracks.Sum(track => track.Width) +
                    Math.Max(0, layout.Tracks.Length - 1) * layout.ColumnGap;
                Assert.True(tracksWidth <= layout.TrackAreaWidth + 0.001,
                    $"{page} allocated {tracksWidth:0.###} px into {layout.TrackAreaWidth:0.###} px.");
                Assert.Equal(layout.ContentWidth, layout.TrackAreaWidth, precision: 6);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FiniteSlotPublishesOneMeasuredLayoutToTheHeaderAndEveryRealizedRow()
    {
        avalonia.Invoke(() =>
        {
            var (slot, header, rows, window) = Show(width: 900);
            try
            {
                var layout = Assert.IsType<WordListLayout>(slot.CurrentLayout);
                var row = rows.GetVisualDescendants().OfType<WordRow>().First();
                Assert.Same(layout, WordListLayoutScope.GetLayout(header));
                Assert.Same(layout, WordListLayoutScope.GetLayout(row));
                Assert.True(double.IsFinite(layout.AvailableWidth));
                Assert.Equal(layout.AvailableWidth - layout.OuterChromeWidth - layout.ReservedVerticalScrollbarWidth,
                    layout.ContentWidth, precision: 6);
                Assert.False(layout.UsesHorizontalViewport);
                Assert.Equal(layout.ContentWidth, layout.TrackAreaWidth, precision: 6);
                var headerLine = Assert.Single(header.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Classes.Contains("wordPresentationHeaderLine"));
                Assert.Equal(layout.ColumnGap, headerLine.ColumnSpacing);
                var fieldWorksTrack = Array.FindIndex(layout.Tracks.ToArray(), track =>
                    track.Column == WordListColumn.FieldWorks);
                Assert.True(fieldWorksTrack >= 0);
                Assert.InRange(headerLine.ColumnDefinitions[fieldWorksTrack].Width.Value,
                    layout.WidthOf(WordListColumn.FieldWorks) - 1,
                    layout.WidthOf(WordListColumn.FieldWorks) + 1);
                var rowCells = Assert.Single(row.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Classes.Contains("wordPresentationShell"));
                Assert.InRange(rowCells.ColumnDefinitions[fieldWorksTrack].Width.Value,
                    layout.WidthOf(WordListColumn.FieldWorks) - 1,
                    layout.WidthOf(WordListColumn.FieldWorks) + 1);
                Assert.Equal(layout.TrackAreaWidth + layout.OuterChromeWidth, header.Bounds.Width, precision: 6);

                var scrollViewer = rows.GetVisualDescendants().OfType<ScrollViewer>()
                    .First(viewer => viewer.Name == "PART_ScrollViewer");
                var scrollbar = rows.GetVisualDescendants().OfType<ScrollBar>()
                    .First(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical &&
                        bar.GetVisualAncestors().OfType<ScrollViewer>().First() == scrollViewer);
                Assert.Equal(layout.ReservedVerticalScrollbarWidth, scrollbar.Bounds.Width, precision: 6);
                Assert.InRange(slot.HeadingMeasurementCount, 1, 9);

                var resolutions = slot.LayoutResolutionCount;
                var publications = slot.LayoutPublicationCount;
                var measurements = slot.HeadingMeasurementCount;
                var layoutPasses = slot.LayoutPassCount;
                for (var pass = 0; pass < 6; pass++)
                {
                    slot.InvalidateMeasure();
                    slot.Measure(new Size(layout.AvailableWidth, window.ClientSize.Height));
                    window.UpdateLayout();
                }
                Assert.True(slot.LayoutPassCount > layoutPasses);
                Assert.Equal(resolutions, slot.LayoutResolutionCount);
                Assert.Equal(publications, slot.LayoutPublicationCount);
                Assert.Equal(measurements, slot.HeadingMeasurementCount);
                Assert.Same(layout, slot.CurrentLayout);

                slot.Measure(new Size(double.PositiveInfinity, window.ClientSize.Height));
                Assert.Same(layout, slot.CurrentLayout);
                Assert.Equal(resolutions, slot.LayoutResolutionCount);
                Assert.Equal(publications, slot.LayoutPublicationCount);
                Assert.True(double.IsFinite(slot.CurrentLayout!.AvailableWidth));

                window.UpdateLayout();
                var settledPasses = slot.LayoutPassCount;
                window.UpdateLayout();
                Assert.Equal(settledPasses, slot.LayoutPassCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void NarrowSlotKeepsItsChromeAndUsesOneHorizontalViewport()
    {
        avalonia.Invoke(() =>
        {
            var (slot, header, rows, window) = Show(width: 420);
            try
            {
                var layout = Assert.IsType<WordListLayout>(slot.CurrentLayout);
                var tracksWidth = layout.Tracks.Sum(track => track.Width) +
                    Math.Max(0, layout.Tracks.Length - 1) * layout.ColumnGap;
                Assert.True(layout.UsesHorizontalViewport);
                Assert.True(layout.TrackAreaWidth > layout.ContentWidth);
                Assert.Equal(tracksWidth, layout.TrackAreaWidth, precision: 6);
                Assert.Equal(layout.TrackAreaWidth + layout.OuterChromeWidth, header.Bounds.Width, precision: 6);
                Assert.Equal(layout.TrackAreaWidth + layout.OuterChromeWidth + layout.ReservedVerticalScrollbarWidth,
                    rows.Bounds.Width, precision: 6);
                Assert.Equal(ScrollBarVisibility.Auto, slot.HorizontalScrollBarVisibility);
                Assert.Same(layout, WordListLayoutScope.GetLayout(header));
                Assert.Same(layout, WordListLayoutScope.GetLayout(rows.GetVisualDescendants().OfType<WordRow>().First()));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (WordListSlot Slot, WordRowHeader Header, ListBox Rows, Window Window) Show(
        double width,
        WordListSchema? schema = null,
        bool showsMeaning = true)
    {
        var items = Enumerable.Range(0, 100).Select(index => new WordPresentation(
            new WordPresentationKey($"slot-word:{index}"), 0,
            WordRowViewModel.NotParsed($"word-{index}"), WordListOwner.WordList)).ToArray();
        var header = new WordRowHeader();
        var rows = new ListBox
        {
            Classes = { "wordRows" },
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<WordPresentation>((item, _) => new WordRow { Data = item },
                supportsRecycling: true),
        };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(rows, 1);
        content.Children.Add(header);
        content.Children.Add(rows);
        var slot = new WordListSlot
        {
            Schema = schema ?? WordListSchemas.WordList,
            ShowsMeaning = showsMeaning,
            Content = content,
        };
        var host = new Grid { Width = width, Height = 300, Children = { slot } };
        var window = new Window { Content = host, Width = width + 80, Height = 360 };
        window.Show();
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        return (slot, header, rows, window);
    }
}
