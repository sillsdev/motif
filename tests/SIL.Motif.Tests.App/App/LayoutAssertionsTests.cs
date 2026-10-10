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
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;
using RowFacts = SIL.Motif.Contract.Responses.WordRow;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class LayoutAssertionsTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void EveryWordPresentationSurfaceIsCheckedForTextOverflow()
    {
        var families = new (string Name, Func<TextBlock, Control> Wrap)[]
        {
            ("WordRow", text => new WordRow { Content = text }),
            ("WordRowHeader", text => new WordRowHeader { Content = text }),
            ("WordStripToken", text => new WordStripToken { Child = text }),
            ("WordCard", text => new WordCard { Content = text }),
            ("MorphemePanel", text => new MorphemePanel { Child = text }),
            ("ResultsInText analysis items", text => new ProgressiveItemsControl
            {
                Name = "ResultsInTextPanelFieldWorksAnalysesItems",
                FullItemsSource = new[] { new object() },
                ItemTemplate = new FuncDataTemplate<object>((_, _) => text),
            }),
        };

        foreach (var (name, wrap) in families)
        {
            avalonia.Invoke(() =>
            {
                var overflow = new TextBlock
                {
                    Text = $"overflow-{name}-{new string('x', 80)}",
                    Width = 20,
                    TextWrapping = TextWrapping.NoWrap,
                };
                var window = new Window { Content = wrap(overflow), Width = 180, Height = 120 };
                try
                {
                    window.Show();
                    PageScreenshots.Settle(window);
                    var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                        () => LayoutAssertions.AssertCurrent(window));
                    Assert.Contains(overflow.Text, error.Message, StringComparison.Ordinal);
                }
                finally { window.Close(); }
            });
        }
    }

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
            var key = new WordPresentationKey("layout-long-word");
            var viewModel = new WordRowViewModel(rowFacts);
            var host = new LongMorphologyHost(viewModel.FieldWorksMorphemes);
            var row = new WordRow
            {
                Data = new WordPresentation(key, 0, viewModel, WordListOwner.ReadOnly),
                Host = host,
                State = new WordInteractionState(key, IsOpen: true),
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
                var columns = Enum.GetValues<WordListColumn>()
                    .Select(column => column == WordListColumn.Next
                        ? new WordColumnSizing(column, 130, 130, 300, 50)
                        : new WordColumnSizing(column, 40, 110, 300, 50));
                var sizing = new WordListSizing(16, 0, 8, 360, columns);
                var schema = WordListSchema.Create(
                    WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss,
                    hasFieldWorksMorphology: true, hasPanGlossMorphology: true);
                var layout = new WordListPolicy().Resolve(window.ClientSize.Width, schema, showsMeaning: true,
                    styleRevision: 0, uiLocale: "en", sizing);
                WordListLayoutScope.SetLayout(row, layout);
                PageScreenshots.Settle(window);
                var formTip = Assert.IsType<string>(ToolTip.GetTip(row.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Classes.Contains("wordPresentationForm"))));
                Assert.StartsWith(word, formTip, StringComparison.Ordinal);
                var glossTip = Assert.IsType<string>(ToolTip.GetTip(row.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Classes.Contains("wordPresentationGloss"))));
                Assert.Equal(gloss, glossTip);
                var card = Assert.Single(row.GetVisualDescendants().OfType<WordCard>());
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == viewModel.FieldWorksMorphemes[0].Form);
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == viewModel.FieldWorksMorphemes[0].GlossOrPlaceholder);
                LayoutAssertions.AssertCurrent(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class LongMorphologyHost(IReadOnlyList<ParserReadingMorphViewModel> morphs) : IWordPresentationHost
    {
        public Task<WordCardReadResult> ReadCardAsync(
            WordPresentationKey key,
            long evidenceRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(WordCardReadResult.Read(new WordCardDocument(key, evidenceRevision,
                [new WordCardMorphology("FieldWorks", morphs)])));
        }

        public ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new WordActionResult(true));
    }

    [Fact]
    public void WordFormHitTestIncludesWhitespaceBetweenLetters()
    {
        avalonia.Invoke(() =>
        {
            var rowFacts = new RowFacts("alikula", WordRowOutcome.Same, "Kept", WordRowTone.Fine);
            var row = new WordRow
            {
                Data = new WordPresentation(new WordPresentationKey("layout-hit-test"), 0,
                    new WordRowViewModel(rowFacts), WordListOwner.ReadOnly),
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
                    .Single(text => text.Classes.Contains("wordPresentationForm"));
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

    [Fact]
    public void RejectsOverflowInAnOpenNestedPopup()
    {
        avalonia.Invoke(() =>
        {
            var heading = new TextBlock { Text = "Popup heading must fit", Width = 20 };
            var popup = new Popup { Child = heading };
            var window = new Window
            {
                Content = new Border { Child = new Grid { Children = { popup } } },
                Width = 1040,
                Height = 780,
            };
            try
            {
                window.Show();
                popup.PlacementTarget = window;
                popup.IsOpen = true;
                PageScreenshots.Settle(window);
                var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                    () => LayoutAssertions.AssertCurrent(window));
                Assert.Contains(heading.Text, error.Message, StringComparison.Ordinal);
            }
            finally
            {
                popup.IsOpen = false;
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
    public void InspectingAnImplicitGridLeavesItSafeToArrangeAgain()
    {
        avalonia.Invoke(() =>
        {
            var grid = new Grid { Children = { new Border { Height = 24 } } };
            var window = new Window { Content = grid, Width = 1040, Height = 120 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                LayoutAssertions.AssertCurrent(window);
                grid.InvalidateArrange();
                PageScreenshots.Settle(window);
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
    public void AllowsACompactWarningGlyphInsideItsGlyphBox()
    {
        avalonia.Invoke(() =>
        {
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

    [Fact]
    public void RejectsMorphemeDescendersClippedByTheirLineBoxes()
    {
        avalonia.Invoke(() =>
        {
            var row = new Border
            {
                Classes = { "stripRow", "analysisRow" },
                BorderThickness = new Thickness(0, 1, 0, 0),
                Height = 29,
                ClipToBounds = true,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Classes = { "morphemePanelForm" },
                            Text = "gel -ye",
                            FontSize = 11,
                            LineHeight = 14,
                            TextWrapping = TextWrapping.NoWrap,
                        },
                        new TextBlock
                        {
                            Classes = { "morphemePanelGloss" },
                            Text = "past tense",
                            FontSize = 10.5,
                            LineHeight = 14,
                            TextWrapping = TextWrapping.NoWrap,
                        },
                    },
                },
            };
            var window = new Window { Content = row, Width = 260, Height = 80 };
            try
            {
                window.SetValue(TextElement.FontFamilyProperty,
                    new FontFamily("fonts:Motif#Andika"));
                window.Show();
                PageScreenshots.Settle(window);
                var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(
                    () => LayoutAssertions.AssertMorphemeGlyphsFitAnalysisRows(window));
                Assert.Contains("Morpheme text 'gel -ye' needs", error.Message, StringComparison.Ordinal);
                Assert.Contains("at line height 14 px", error.Message, StringComparison.Ordinal);
                Assert.Contains("arranged text bounds are 14 px", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AllowsAndikaMorphemesWhenTheirLinesFitTheAnalysisRow()
    {
        avalonia.Invoke(() =>
        {
            var row = new Border
            {
                Classes = { "stripRow", "analysisRow" },
                BorderThickness = new Thickness(0, 1, 0, 0),
                Height = 37,
                ClipToBounds = true,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Classes = { "morphemePanelForm" },
                            Text = "gel -ye",
                            FontSize = 11,
                            LineHeight = 18,
                            TextWrapping = TextWrapping.NoWrap,
                        },
                        new TextBlock
                        {
                            Classes = { "morphemePanelGloss" },
                            Text = "past tense",
                            FontSize = 10.5,
                            LineHeight = 18,
                            TextWrapping = TextWrapping.NoWrap,
                        },
                    },
                },
            };
            var window = new Window { Content = row, Width = 260, Height = 80 };
            try
            {
                window.SetValue(TextElement.FontFamilyProperty,
                    new FontFamily("fonts:Motif#Andika"));
                window.Show();
                PageScreenshots.Settle(window);
                LayoutAssertions.AssertMorphemeGlyphsFitAnalysisRows(window);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
