using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class UnparsedLargeProjectScaleTests(ITestOutputHelper output)
{
    [Fact]
    public void LargeSelectionOpensScrollsAndReopensWithinMemoryBudgetsWithoutParsing()
    {
        var measurements = new ScaleMeasurements(output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var project = new LargeProjectFixture(representativeScale: true);
            output.WriteLine($"Representative master: {project.CreationTime.TotalSeconds:F3} s; " +
                $"{new FileInfo(project.FwDataPath).Length / 1048576d:F1} MiB; " +
                $"{project.Words.Count} wordforms, {project.TextIds.Count} Texts, {project.TotalOccurrenceCount} occurrences");
            Assert.Equal(22948, project.Words.Count);
            Assert.Equal(129, project.TextIds.Count);
            Assert.Equal(21604, project.SelectedWordCount);
            Assert.Equal(76761, project.TotalOccurrenceCount);
            var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
            var client = RealCommandClient.Create(project.ManagedRoot, parser);
            await measurements.MeasureAsync("Unparsed: capture Baseline", 60, async () =>
            {
                var result = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
                Assert.True(result.Succeeded, result.Refusal?.Message);
            }, managedMiB: 768, workingSetMiB: 1024);
            var configured = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Large Selection", project.TextIds, []), CancellationToken.None);
            Assert.True(configured.Succeeded, configured.Refusal?.Message);
            var skipped = await client.SkipSetupAsync(new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
            await OpenAndReadAsync(project, parser, measurements);
            using var reopened = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            await measurements.MeasureAsync("Unparsed: reopen project", 45,
                () => OpenAsync(reopened));
            Assert.Null(reopened.Workspace.Assess.Result);
            Capture(reopened, "reopened");
            Assert.DoesNotContain(FakeParser.Invocations(parser), command => command is "batch" or "parse" or "trace");
        }, TimeSpan.FromMinutes(10));
        measurements.AssertBudgets();
    }

    private async Task OpenAndReadAsync(LargeProjectFixture project, string parser, ScaleMeasurements measurements)
    {
        using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
        await measurements.MeasureAsync("Unparsed: open configured Selection", 45, () => OpenAsync(window));
        Assert.Null(window.Workspace.Assess.Result);
        Capture(window, "opened");
        var texts = window.Workspace.PageModel<TextsPageModel>();
        await measurements.MeasureAsync("Unparsed: show all 21604 Word list rows", 30, () =>
        {
            window.ShowPage(WorkspacePage.Texts);
            window.ShowTextsTab(TextsTab.AnalyzeTexts);
            texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
            window.WaitUntil(() => !texts.Words.IsLoading && texts.Words.Rows.Count == project.SelectedWordCount,
                TimeSpan.FromSeconds(30), "the large Word list did not load");
            PageScreenshots.Settle(window.Window);
            var list = Assert.Single(window.Window.GetVisualDescendants().OfType<ListBox>(),
                control => control.IsEffectivelyVisible && ReferenceEquals(control.ItemsSource, texts.Words.Rows));
            list.ScrollIntoView(texts.Words.Rows.Count - 1);
            PageScreenshots.Settle(window.Window);
            var rows = window.Window.GetVisualDescendants().OfType<WordRow>().ToArray();
            Assert.InRange(rows.Length, 1, 64);
            Assert.InRange(texts.Words.MaterializedRowCount, 1, 128);
            output.WriteLine($"Word list retained {texts.Words.MaterializedRowCount} projected rows out of {project.SelectedWordCount}");
            Assert.Contains(rows, row => row.Row?.Word == texts.Words.Rows[^1].Form);
        });
        Capture(window, "word-list-end");
        await measurements.MeasureAsync("Unparsed: read and scroll 224-line Text", 30, async () =>
        {
            texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.TextReader);
            var model = texts.ResultsInText;
            window.WaitUntil(() => model.Texts.Count == project.TextIds.Count,
                TimeSpan.FromSeconds(30), "the large Text inventory did not load");
            await model.ReadStateRefresh;
            Assert.Equal(project.TotalOccurrenceCount, model.AllCount);
            var longest = model.Texts.MaxBy(text => text.Lines.Sum(line => line.Tokens.Count))!;
            Assert.Equal(224, longest.Lines.Count);
            Assert.Equal(project.LongestTextLength, longest.Lines.Sum(line => line.Tokens.Count));
            model.SelectedText = longest;
            PageScreenshots.Settle(window.Window);
            var panel = window.Window.GetLogicalDescendants().OfType<ResultsInTextPanel>().Single();
            var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                viewer => viewer.IsEffectivelyVisible && viewer.Content is ItemsControl);
            reader.Offset = new Vector(0, Math.Max(0, reader.Extent.Height - reader.Viewport.Height));
            PageScreenshots.Settle(window.Window);
            var final = longest.Lines[^1].Tokens[^1];
            var strip = Assert.Single(panel.GetVisualDescendants().OfType<Border>(),
                border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, final));
            var origin = strip.TranslatePoint(new Point(), reader);
            Assert.NotNull(origin);
            Assert.True(new Rect(reader.Viewport).Intersects(new Rect(origin!.Value, strip.Bounds.Size)));
        });
        Assert.Null(window.Workspace.Assess.Result);
        Capture(window, "text-reader-end");
    }

    private static async Task OpenAsync(WalkthroughWindow window)
    {
        window.Window.Show();
        window.OpenRecentProjectByClick(window.ProjectPath);
        await window.Workspace.Context.EvidencePublication;
        window.WaitUntilProjectIsQuiet(TimeSpan.FromSeconds(45), "the unparsed project did not finish opening");
        PageScreenshots.Settle(window.Window);
    }

    private static void Capture(WalkthroughWindow window, string state)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { Length: > 0 } root) return;
        var folder = Path.Combine(root, "large-project-unparsed");
        Directory.CreateDirectory(folder);
        PageScreenshots.Save(window.Window, Path.Combine(folder, state + ".png"));
    }
}
