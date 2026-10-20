using Avalonia;
using Avalonia.Automation;
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
                Assert.False(layout.Stacked);
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
            var (slot, header, rows, window) = Show(width: 1280);
            try
            {
                var layout = Assert.IsType<WordListLayout>(slot.CurrentLayout);
                var row = rows.GetVisualDescendants().OfType<WordRow>().First();
                Assert.Same(layout, WordListLayoutScope.GetLayout(header));
                Assert.Same(layout, WordListLayoutScope.GetLayout(row));
                Assert.True(double.IsFinite(layout.AvailableWidth));
                Assert.Equal(layout.AvailableWidth - layout.OuterChromeWidth - layout.ReservedVerticalScrollbarWidth,
                    layout.ContentWidth, precision: 6);
                Assert.False(layout.Stacked);
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
    public void NarrowSlotStacksItsRowsInsteadOfScrollingSideways()
    {
        avalonia.Invoke(() =>
        {
            var (slot, header, rows, window) = Show(width: 420);
            try
            {
                var layout = Assert.IsType<WordListLayout>(slot.CurrentLayout);
                Assert.True(layout.Stacked);
                Assert.Empty(layout.Tracks);
                Assert.Equal(layout.ContentWidth, layout.TrackAreaWidth, precision: 6);
                Assert.Equal(layout.AvailableWidth, rows.Bounds.Width, precision: 6);
                Assert.Equal(ScrollBarVisibility.Disabled, slot.HorizontalScrollBarVisibility);
                Assert.True(slot.Extent.Width <= slot.Viewport.Width + 1,
                    $"Extent {slot.Extent.Width:0.###} px exceeds viewport {slot.Viewport.Width:0.###} px.");
                Assert.Same(layout, WordListLayoutScope.GetLayout(header));
                var row = rows.GetVisualDescendants().OfType<WordRow>().First();
                Assert.Same(layout, WordListLayoutScope.GetLayout(row));
                var shell = row.GetVisualDescendants().OfType<Grid>()
                    .First(grid => grid.Classes.Contains("wordPresentationShell"));
                Assert.True(shell.Bounds.Width <= slot.Viewport.Width + 1);
                Assert.Contains(shell.Children, child => child is WrapPanel);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void StackedRowsCaptionAndNameEachCellWithItsColumnHeadingAndWideRowsShowNone()
    {
        avalonia.Invoke(() =>
        {
            var (_, header, narrowRows, narrow) = Show(width: 420, WordListSchemas.Timing);
            var (_, _, wideRows, wide) = Show(width: 1400, WordListSchemas.Timing);
            try
            {
                var headings = Enum.GetValues<WordListColumn>()
                    .Select(header.HeadingFor).OfType<TextBlock>().Select(block => block.Text).ToHashSet();
                var captions = narrowRows.GetVisualDescendants().OfType<TextBlock>()
                    .Where(block => block.Classes.Contains("wordPresentationCaption")).ToArray();
                Assert.NotEmpty(captions);
                foreach (var caption in captions)
                {
                    Assert.Contains(caption.Text, headings);
                    Assert.True(caption.IsEffectivelyVisible);
                    var cell = caption.GetVisualAncestors().OfType<Control>()
                        .First(ancestor => !string.IsNullOrEmpty(AutomationProperties.GetName(ancestor)));
                    Assert.Equal(caption.Text, AutomationProperties.GetName(cell));
                }
                Assert.Empty(wideRows.GetVisualDescendants().OfType<TextBlock>()
                    .Where(block => block.Classes.Contains("wordPresentationCaption")));
            }
            finally
            {
                narrow.Close();
                wide.Close();
            }
        });
    }

    [Fact]
    public void StackedCaptionsCoverWarningsAndReadAndFollowThePagesMeaningHeading()
    {
        avalonia.Invoke(() =>
        {
            var (_, _, rows, window) = Show(width: 420, WordListSchemas.All, meaningHeader: "Gloss");
            try
            {
                var captions = rows.GetVisualDescendants().OfType<TextBlock>()
                    .Where(block => block.Classes.Contains("wordPresentationCaption")).Select(block => block.Text).ToHashSet();
                Assert.Contains("Warnings", captions);
                Assert.Contains("Read", captions);
                Assert.Contains("Gloss", captions);
                Assert.DoesNotContain("Meaning", captions);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SizingHintsThatArriveAfterTheFirstLayoutResizeTheirTrack()
    {
        avalonia.Invoke(() =>
        {
            var (slot, header, _, window) = Show(width: 1280);
            try
            {
                Assert.True(Assert.IsType<WordListLayout>(slot.CurrentLayout).WidthOf(WordListColumn.Next) < 260);
                var next = header.HeadingFor(WordListColumn.Next);
                WordListSizingHints.SetMaximum(next, 260);
                WordListSizingHints.SetPreferred(next, 260);
                WordListSizingHints.SetMinimum(next, 260);
                window.UpdateLayout();

                Assert.Equal(260, slot.CurrentLayout!.WidthOf(WordListColumn.Next), precision: 6);
                Assert.Same(slot.CurrentLayout, WordListLayoutScope.GetLayout(header));
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
        bool showsMeaning = true,
        string? meaningHeader = null)
    {
        var items = Enumerable.Range(0, 100).Select(index => new WordPresentation(
            new WordPresentationKey($"slot-word:{index}"), 0,
            new WordRowViewModel(WordRowViewModel.NotParsed($"word-{index}").Row with { WarningsNamed = 2, IsUnread = true }),
            WordListOwner.WordList)).ToArray();
        var header = new WordRowHeader();
        if (meaningHeader is not null) header.MeaningHeader = meaningHeader;
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
        var window = new MotifTestWindow { Content = host, Width = width + 80, Height = 360 };
        window.Show();
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        return (slot, header, rows, window);
    }
}
