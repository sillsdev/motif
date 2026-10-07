using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class WordRowOpenLayoutSystemTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(1);

    [Fact]
    public void OpenWordListRowsFitAcrossWidthsZoomsAndThemesAndUseTheModuleCard()
    {
        using var layoutLog = new LayoutLoopLogScope(output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            var application = Application.Current!;
            var originalTheme = application.RequestedThemeVariant;
            var originalZoom = WindowZoomPolicy.Transform.ScaleX;
            try
            {
                window.Height = 1600;
                var texts = workspace.PageModel<TextsPageModel>();
                texts.Tab = TextsTab.AnalyzeTexts;
                texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                var rowIndex = texts.Words.Rows.Select((item, index) => (item, index))
                    .Single(pair => pair.item.Presentation.Facts.Word == "hawajafika").index;
                var pageList = window.FindControl<ListBox>("PageList")!;
                Assert.Equal(ScrollBarVisibility.Visible,
                    ScrollViewer.GetVerticalScrollBarVisibility(pageList));
                var wordsList = window.GetVisualDescendants().OfType<ListBox>()
                    .Single(list => list.Name == "TextWordsPanelRowsItems");
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var zoom in new[] { 100, 125, 150 })
                foreach (var width in new[] { 1040, 1240 })
                {
                    application.RequestedThemeVariant = theme;
                    window.RequestedThemeVariant = theme;
                    WindowZoomPolicy.Apply(application, zoom);
                    window.Width = width;
                    wordsList.ScrollIntoView(rowIndex);
                    PageScreenshots.Settle(window);

                    var row = window.GetVisualDescendants().OfType<WordRow>().Single(item =>
                        item.IsEffectivelyVisible && item.Data?.Facts.Word == "hawajafika");
                    row.State = row.State! with { IsOpen = true };
                    PageScreenshots.Settle(window);
                    LayoutAssertions.AssertCurrent(row);

                    var fieldWorksCell = row.GetVisualDescendants().OfType<MorphemePanel>()
                        .First(panel => panel.Classes.Contains("wordPresentationMorphology"));
                    var morphemes = fieldWorksCell.GetVisualDescendants().OfType<StackPanel>()
                        .Where(part => part.Tag is ParserReadingMorphViewModel).ToArray();
                    Assert.Equal(row.Data!.Facts.FieldWorksMorphemes.Count, morphemes.Length);
                    Assert.All(morphemes, part => Assert.True(part.IsEffectivelyVisible));
                    Assert.Contains(row.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                        text.Classes.Contains("wordPresentationGloss") && text.IsEffectivelyVisible);

                    var card = row.GetVisualDescendants().OfType<WordCard>().Single();
                    var analysis = Assert.Single(card.Document!.Sections.OfType<WordCardAnalysis>());
                    Assert.NotNull(analysis.Token);
                    Assert.Contains(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        block => block.IsEffectivelyVisible && block.Text == "In FieldWorks · now");
                    Assert.Contains(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        block => block.IsEffectivelyVisible && block.Text == "Time by rule");
                    Assert.Contains(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        block => block.IsEffectivelyVisible && block.Text == "WHERE IT APPEARS");
                }
                Assert.DoesNotContain(layoutLog.Messages,
                    message => message.Contains("Layout cycle detected", StringComparison.Ordinal));
            }
            finally
            {
                application.RequestedThemeVariant = originalTheme;
                WindowZoomPolicy.Apply(application, (int)Math.Round(originalZoom * 100));
                window.Close();
            }
        }, Deadline);
    }

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
