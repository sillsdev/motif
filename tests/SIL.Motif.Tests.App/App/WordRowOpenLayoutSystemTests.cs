using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class WordRowOpenLayoutSystemTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void OpenRowsKeepMorphemesOnOneScrollableLineAndUseTheAnalyzeWordCard(int width)
    {
        using var layoutLog = new LayoutLoopLogScope(output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = width;
                window.Height = 1600;
                var texts = workspace.PageModel<TextsPageModel>();
                texts.Tab = TextsTab.AnalyzeTexts;
                texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                var row = window.GetVisualDescendants().OfType<WordRow>()
                    .Single(item => item.IsEffectivelyVisible && item.List == "word-list" &&
                        item.Row?.Word == "hawajafika");
                row.IsOpen = true;
                PageScreenshots.Settle(window);
                LayoutAssertions.AssertCurrent(window);
                var rowIndex = texts.Words.Rows.Select((item, index) => (item, index))
                    .Single(pair => pair.item.Listed.Row.Word == "hawajafika").index;
                var pageList = window.FindControl<ListBox>("PageList")!;
                Assert.Equal(ScrollBarVisibility.Visible,
                    ScrollViewer.GetVerticalScrollBarVisibility(pageList));
                var wordsList = window.GetVisualDescendants().OfType<ListBox>()
                    .Single(list => list.Name == "TextWordsPanelRowsItems");
                wordsList.ScrollIntoView(rowIndex);
                PageScreenshots.Settle(window);
                row = window.GetVisualDescendants().OfType<WordRow>()
                    .Single(item => item.IsEffectivelyVisible && item.List == "word-list" &&
                        item.Row?.Word == "hawajafika");
                row.IsOpen = true;
                PageScreenshots.Settle(window);

                var fieldWorksCell = Part(row, "wordRowFieldWorks");
                var morphemes = fieldWorksCell.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("wordRowMorph")).ToArray();
                Assert.Equal(5, morphemes.Length);
                Assert.Single(morphemes.Select(morpheme => Math.Round(BoundsIn(morpheme, row).Y, 1)).Distinct());
                var fieldWorksScroll = row.FindControl<ScrollViewer>("FieldWorksMorphemeScroll")!;
                Assert.Equal(ScrollBarVisibility.Auto, fieldWorksScroll.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, fieldWorksScroll.VerticalScrollBarVisibility);

                var gloss = row.FindControl<CopyableTextBlock>("WordGloss")!;
                Assert.True(gloss.IsEffectivelyVisible);

                var card = row.GetVisualDescendants().OfType<WordRowCard>().Single();
                Assert.NotNull(card.CardToken);
                Assert.Contains(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == "In FieldWorks · now");
                Assert.Contains(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == "Time by rule");
                Assert.DoesNotContain(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == "WHERE IT APPEARS");
                Assert.DoesNotContain(layoutLog.Messages,
                    message => message.Contains("Layout cycle detected", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    private static Control Part(WordRow row, string className) => row.GetVisualDescendants().OfType<Control>()
        .First(control => control.Classes.Contains(className));

    private sealed class LayoutLoopLogScope : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogSink? _previousSink;
        private readonly LayoutLoopLogSink _sink;

        public LayoutLoopLogScope(ITestOutputHelper output)
        {
            _output = output;
            _previousSink = Logger.Sink;
            _sink = new LayoutLoopLogSink(_previousSink);
            Logger.Sink = _sink;
        }

        public IReadOnlyCollection<string> Messages => _sink.Messages;

        public void Dispose()
        {
            Logger.Sink = _previousSink;
            foreach (var message in _sink.Messages) _output.WriteLine(message);
        }
    }

    private sealed class LayoutLoopLogSink(ILogSink? previous) : ILogSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public bool IsEnabled(LogEventLevel level, string area) =>
            area == LogArea.Layout && level >= LogEventLevel.Warning || previous?.IsEnabled(level, area) == true;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Record(level, area, source, messageTemplate, []);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues) => Record(level, area, source, messageTemplate, propertyValues);

        private void Record(LogEventLevel level, string area, object? source, string messageTemplate,
            object?[] propertyValues)
        {
            if (area == LogArea.Layout && level >= LogEventLevel.Warning)
            {
                var values = string.Join(", ", propertyValues.Select(Describe));
                _messages.Enqueue($"Avalonia layout {level}; source {Describe(source)}; {messageTemplate}; {values}");
            }

            if (previous?.IsEnabled(level, area) == true)
                previous.Log(level, area, source, messageTemplate, propertyValues);
        }

        private static string Describe(object? value) => value switch
        {
            Control control => DescribeControl(control),
            null => "<null>",
            _ => $"{value.GetType().Name}: {value}",
        };

        private static string DescribeControl(Control control)
        {
            var ancestors = string.Join(" ← ", control.GetVisualAncestors().OfType<Control>().Take(8)
                .Select(parent => $"{parent.GetType().Name} name='{parent.Name}' bounds={parent.Bounds}"));
            return $"{control.GetType().Name} name='{control.Name}' " +
                $"automationId='{Avalonia.Automation.AutomationProperties.GetAutomationId(control)}' " +
                $"bounds={control.Bounds}; ancestors={ancestors}";
        }
    }
}
