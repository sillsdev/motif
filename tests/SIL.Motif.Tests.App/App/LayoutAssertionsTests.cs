using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;
using RowFacts = SIL.Motif.Contract.Responses.WordRow;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class LayoutAssertionsTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void LongFormsLargeGlossesAndNarrowRowsKeepTheirFullTextAccessible(int width)
    {
        avalonia.Invoke(() =>
        {
            var word = new string('m', 140);
            var gloss = string.Join(' ', Enumerable.Repeat("meaning", 80));
            var morph = new ParserReadingMorph(new string('m', 90), new string('g', 120), "n", null, false, null);
            var rowFacts = new RowFacts(word, WordRowOutcome.Same, "Kept", WordRowTone.Fine)
            {
                Gloss = gloss,
                FieldWorksMorphemes = [morph],
            };
            var columns = WordRowColumnSets.WordList;
            var row = new WordRow
            {
                Row = new WordRowViewModel(rowFacts),
                Columns = columns,
                Width = 520,
            };
            var window = new Window
            {
                Content = new ScrollViewer { Content = row },
                Width = width,
                Height = 900,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Equal(word, ToolTip.GetTip(row.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Classes.Contains("wordRowForm"))));
                LayoutAssertions.AssertCurrent(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WordFormHitTestIncludesWhitespaceBetweenLetters()
    {
        avalonia.Invoke(() =>
        {
            var rowFacts = new RowFacts("alikula", WordRowOutcome.Same, "Kept", WordRowTone.Fine);
            var row = new WordRow
            {
                Row = new WordRowViewModel(rowFacts),
                Columns = WordRowColumnSets.WordList,
                Width = 520,
            };
            var window = new Window
            {
                Content = new ScrollViewer { Content = row },
                Width = 1040,
                Height = 300,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var wordForm = row.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Single(text => text.Classes.Contains("wordRowForm"));
                var center = wordForm.TranslatePoint(
                    new Point(wordForm.Bounds.Width / 2, wordForm.Bounds.Height / 2), window)!.Value;

                Assert.Same(wordForm, window.InputHitTest(center));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RejectsAHeadingThatCannotFitItsAllocatedRectangle()
    {
        avalonia.Invoke(() =>
        {
            var heading = new TextBlock
            {
                Text = "WHERE IT APPEARS",
                TextWrapping = TextWrapping.NoWrap,
                Width = 28,
            };
            var window = new Window { Content = heading, Width = 1040, Height = 120 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutAssertions.AssertCurrent(window));
                Assert.Contains("WHERE IT APPEARS", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataGridScrollingDoesNotExcuseClippedCellsOrDisabledScrollbars(bool clipCell)
    {
        avalonia.Invoke(() =>
        {
            var grid = new DataGrid
            {
                ItemsSource = Enumerable.Range(0, 10).Select(index => $"word-{index}").ToArray(),
                RowHeight = 32,
                VerticalScrollBarVisibility = clipCell ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            };
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Word",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = new FuncDataTemplate<object>((item, _) => new CopyableTextBlock
                {
                    Text = item.ToString(),
                    MinHeight = clipCell ? 80 : 0,
                }),
            });
            var window = new Window { Content = grid, Width = 1040, Height = 90 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutAssertions.AssertCurrent(grid));
                Assert.Contains("is clipped outside a scroll viewport", error.Message, StringComparison.Ordinal);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void RejectsAColumnChildThatCrossesItsAssignedColumn()
    {
        avalonia.Invoke(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("80,80"),
                Children = { new Border { Width = 140, Height = 24 } },
            };
            var window = new Window { Content = grid, Width = 1040, Height = 120 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutAssertions.AssertCurrent(window));
                Assert.Contains("crosses column", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RejectsAnUnscrollableLongMorpheme()
    {
        avalonia.Invoke(() =>
        {
            var morphs = Enumerable.Range(1, 10).Select(index => new ParserReadingMorph(
                index == 1 ? new string('m', 90) : $"morpheme{index}", $"gloss{index}", "n", null, false, null)).ToArray();
            var rowFacts = new RowFacts("word", WordRowOutcome.Same, "Kept", WordRowTone.Fine)
            {
                FieldWorksMorphemes = morphs,
            };
            var row = new WordRow
            {
                Row = new WordRowViewModel(rowFacts),
                Columns = WordRowColumns.FieldWorks | WordRowColumns.FieldWorksMorphemes,
            };
            var window = new Window { Content = row, Width = 520, Height = 320 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var strip = window.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(viewer => AutomationProperties.GetAutomationId(viewer) == AutomationIds.FieldWorksMorphemeScroll);
                strip.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                PageScreenshots.Settle(window);
                var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutAssertions.AssertCurrent(window));
                Assert.Contains("morpheme strip", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AllowsACompactWarningGlyphInsideItsGlyphBox()
    {
        avalonia.Invoke(() =>
        {
            Walkthrough.WalkthroughFonts.Register();
            var glyph = new Border
            {
                Width = 14,
                Height = 14,
                Classes = { "severityGlyph", "warning" },
                Child = new TextBlock { Text = "⚠" },
            };
            var window = new Window { Content = glyph, Width = 120, Height = 80 };
            try
            {
                window.SetValue(TextElement.FontFamilyProperty, new FontFamily("fonts:MotifWalkthrough#Andika"));
                window.Show();
                PageScreenshots.Settle(window);
                LayoutAssertions.AssertCurrent(window);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
