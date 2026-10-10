using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
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
            measurements.Checkpoint("Unparsed: before fixture project/cache and ICU initialization");
            using var project = new LargeProjectFixture(representativeScale: true);
            measurements.Checkpoint("Unparsed: after fixture project/cache and ICU initialization");
            output.WriteLine($"Representative master: {project.CreationTime.TotalSeconds:F3} s; " +
                $"{new FileInfo(project.FwDataPath).Length / 1048576d:F1} MiB; " +
                $"{project.Words.Count} wordforms, {project.TextIds.Count} Texts, {project.TotalOccurrenceCount} occurrences");
            Assert.Equal(22948, project.Words.Count);
            Assert.Equal(129, project.TextIds.Count);
            Assert.Equal(21604, project.SelectedWordCount);
            Assert.Equal(76761, project.TotalOccurrenceCount);
            var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
            var client = RealCommandClient.Create(project.ManagedRoot, parser);
            string? baselinePath = null;
            measurements.Checkpoint("Unparsed: before baseline cache open");
            await measurements.MeasureAsync("Unparsed: capture Baseline", 60, async () =>
            {
                var result = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
                Assert.True(result.Succeeded, result.Refusal?.Message);
                baselinePath = result.Value!.FwDataPath;
            }, managedMiB: 768, processMemoryMiB: 1024);
            measurements.Checkpoint("Unparsed: after baseline cache open and ICU use");
            var configured = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Large Selection", project.TextIds, []), CancellationToken.None);
            Assert.True(configured.Succeeded, configured.Refusal?.Message);
            var skipped = await client.SkipSetupAsync(new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
            await OpenAndReadAsync(project, parser, measurements, baselinePath!);
            using var reopened = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            await measurements.MeasureAsync("Unparsed: reopen project", 45,
                () => OpenAsync(reopened, measurements, "Unparsed reopened window", baselinePath!));
            Assert.Null(reopened.Workspace.Assess.Result);
            Capture(reopened, "reopened");
            measurements.AssertReadCounts("Unparsed: reopen project", 1, 64, 1, 64);
            measurements.AssertBaselineReadVolume("Unparsed: reopen project", 8L * 1048576, 0);
            Assert.DoesNotContain(FakeParser.Invocations(parser), command => command is "batch" or "parse" or "trace");
        }, TimeSpan.FromMinutes(10));
        measurements.AssertBudgets();
    }

    private async Task OpenAndReadAsync(LargeProjectFixture project, string parser, ScaleMeasurements measurements, string baselinePath)
    {
        using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
        var texts = window.Workspace.PageModel<TextsPageModel>();
        var createdRows = new ConcurrentBag<WeakReference<object>>();
        using var rowCreationObservation = texts.Words.ObserveRowCreation(createdRows.Add);
        await measurements.MeasureAsync("Unparsed: open configured Selection", 45,
            () => OpenAsync(window, measurements, "Unparsed first window", baselinePath));
        Assert.Null(window.Workspace.Assess.Result);
        measurements.AssertReadCounts("Unparsed: open configured Selection", 1, 64, 1, 64);
        measurements.AssertBaselineReadVolume("Unparsed: open configured Selection", 8L * 1048576, 0);
        Assert.Null(window.Workspace.Context.SelectionReads.Reader);
        Capture(window, "opened");
        await measurements.MeasureAsync("Unparsed: show all 21604 Word list rows", 30, async () =>
        {
            window.ShowPage(WorkspacePage.Texts);
            window.ShowTextsTab(TextsTab.AnalyzeTexts);
            texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
            await window.Workspace.Context.EvidencePublication;
            window.WaitUntil(() => !texts.Words.IsLoading && texts.Words.Rows.Count == project.SelectedWordCount,
                TimeSpan.FromSeconds(30), "the large Word list did not load");
            PageScreenshots.Settle(window.Window);
            var list = Assert.Single(window.Window.GetVisualDescendants().OfType<ListBox>(),
                control => control.IsEffectivelyVisible && ReferenceEquals(control.ItemsSource, texts.Words.DisplayRows));
            await texts.Words.SettleVisibleDetailsAsync();
            PageScreenshots.Settle(window.Window);
            list.ScrollIntoView(texts.Words.Rows.Count - 1);
            PageScreenshots.Settle(window.Window);
            await texts.Words.SettleVisibleDetailsAsync();
            list.ScrollIntoView(texts.Words.Rows.Count - 1);
            PageScreenshots.Settle(window.Window);
            var visible = list.GetVisualDescendants().OfType<WordRow>().First(row => row.IsEffectivelyVisible).Data!;
            var wordPanel = list.FindAncestorOfType<TextWordsPanel>()!;
            Assert.True((await wordPanel.PresentationHost.HandleAsync(new WordRequest(visible.Key,
                visible.EvidenceRevision, WordAction.NavigateLast), CancellationToken.None)).Handled);
            window.WaitUntil(() => list.ContainerFromIndex(list.ItemCount - 1) is not null,
                TimeSpan.FromSeconds(5), "the last Word list row did not arrive after scrolling");
            var finalContainer = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(list.ItemCount - 1));
            var finalRow = new[] { finalContainer }.Concat(finalContainer.GetVisualDescendants())
                .OfType<WordRow>().Single();
            Assert.Equal(texts.Words.Rows[^1].Form, finalRow.Data?.Facts.Word);
            var rowViewport = Assert.IsType<ScrollViewer>(finalRow.FindAncestorOfType<ScrollViewer>());
            Assert.True(new Rect(rowViewport.Viewport).Intersects(new Rect(
                finalRow.TranslatePoint(default, rowViewport)!.Value, finalRow.Bounds.Size)));
            var tree = AvaloniaScaleCounts.CaptureRealizedControls(window.Window);
            var rows = tree.MotifControl(typeof(WordRow).FullName!);
            Assert.InRange(rows.Count, 1, 64);
            var rowModels = ScaleCountHarness.AssertLiveViewModelBudget(
                createdRows, typeof(TextWordRowViewModel), 1, 128);
            output.WriteLine($"Word list realized {rows.Count} rows at depth {rows.MaximumDepth}; " +
                $"{rowModels} row models survive collection from {createdRows.Count} creation observations");
        });
        measurements.AssertReadCounts("Unparsed: show all 21604 Word list rows", 0, 128, 1, 4096);
        var openedReader = window.Workspace.Context.SelectionReads.Reader!;
        Assert.Equal(project.TotalOccurrenceCount, openedReader.Diagnostics.PhysicalOccurrences);
        Capture(window, "word-list-end");
        await measurements.MeasureAsync("Unparsed: read and scroll 224-line Text", 30, async () =>
        {
            texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.TextReader);
            var model = texts.ResultsInText;
            window.WaitUntil(() => model.Texts.Count == project.TextIds.Count,
                TimeSpan.FromSeconds(30), "the large Text inventory did not load");
            await model.ReadStateRefresh;
            Assert.Equal(project.TotalOccurrenceCount, model.AllCount);
            var longest = model.Texts.Single(text => text.TextId == project.TextIds[0]);
            Assert.Equal(224, longest.Summary!.LineCount);
            model.SelectedText = longest;
            var panel = window.Window.GetLogicalDescendants().OfType<ResultsInTextPanel>().Single();
            PageScreenshots.Settle(window.Window);
            var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                viewer => viewer.IsEffectivelyVisible && viewer.Content is ItemsControl);
            var scrolls = new List<string>();
            reader.ScrollChanged += (_, e) => scrolls.Add(
                $"offset {reader.Offset.Y:F0} ({e.OffsetDelta.Y:+0;-0;0}) extent {reader.Extent.Height:F0} ({e.ExtentDelta.Y:+0;-0;0})");
            Assert.IsType<ItemsControl>(reader.Content).ScrollIntoView(longest.Summary.LineCount - 1);
            reader.ScrollToEnd();
            reader.UpdateLayout();
            var pages = model.LinePages!;
            // Each arriving page can move the end again, so the reader settles over up to four passes.
            for (var pass = 0; pass < 4; pass++)
            {
                await pages.Pending;
                foreach (var words in panel.GetVisualDescendants().OfType<ProgressiveItemsControl>().ToArray())
                    await words.PageRefresh;
                PageScreenshots.Settle(window.Window);
            }
            var finalPosition = window.Workspace.Context.SelectionReads.Summary!.SourcePositions.Last(position =>
                position.Location.Anchor.TextId == longest.TextId);
            var diagnostics = window.Workspace.Context.SelectionReads.Reader!.Diagnostics;
            Assert.InRange(diagnostics.LiveLineModels, 1, 48);
            Assert.InRange(diagnostics.LiveTokenModels, 1, 512);
            var strip = Assert.Single(AnalyzeTextsLayoutTests.Strips(panel), strip =>
                ResultsInTextPanel.TokenOf(strip)?.Occurrence == finalPosition.Location.Anchor);
            var origin = strip.TranslatePoint(new Point(), reader);
            Assert.NotNull(origin);
            Assert.True(new Rect(reader.Viewport).Intersects(new Rect(origin!.Value, strip.Bounds.Size)),
                $"the last strip at {origin.Value} size {strip.Bounds.Size} is outside the reader viewport " +
                $"{reader.Viewport} at offset {reader.Offset} in extent {reader.Extent}; scrolls: {string.Join("; ", scrolls)}");
        });
        measurements.AssertReadCounts("Unparsed: read and scroll 224-line Text", 1, 128, 1, 4096);
        Assert.Null(window.Workspace.Assess.Result);
        Capture(window, "text-reader-end");
        texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
        PageScreenshots.Settle(window.Window);
        var list = Assert.Single(window.Window.GetVisualDescendants().OfType<ListBox>(),
            control => control.IsEffectivelyVisible && ReferenceEquals(control.ItemsSource, texts.Words.DisplayRows));
        VisitRows(texts.Words.Rows);
        list.ScrollIntoView(texts.Words.Rows.Count - 1);
        PageScreenshots.Settle(window.Window);
        await texts.Words.SettleVisibleDetailsAsync();
        PageScreenshots.Settle(window.Window);
        var retained = ScaleCountHarness.AssertLiveViewModelBudget(createdRows, typeof(TextWordRowViewModel), 1, 128);
        output.WriteLine($"All {project.SelectedWordCount} indexed Word rows were visited; {retained} models remain.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void VisitRows(IReadOnlyList<TextWordRowViewModel> rows)
    {
        for (var index = 0; index < rows.Count; index++) _ = rows[index];
    }

    private static async Task OpenAsync(
        WalkthroughWindow window, ScaleMeasurements measurements, string checkpointPrefix, string baselinePath)
    {
        var hidden = baselinePath + ".opening-hidden";
        File.Move(baselinePath, hidden);
        try
        {
            measurements.Checkpoint($"{checkpointPrefix}: before show");
            window.Window.Show();
            measurements.Checkpoint($"{checkpointPrefix}: after show, before render");
            window.OpenRecentProjectByClick(window.ProjectPath);
            await window.Workspace.Context.EvidencePublication;
            window.WaitUntilProjectIsQuiet(TimeSpan.FromSeconds(45), "the unparsed project did not finish opening");
            Assert.Null(window.Workspace.OpenRefusal);
            Assert.NotNull(window.Workspace.Context.Evidence.Stored?.ProjectSummary);
            PageScreenshots.Settle(window.Window);
            measurements.Checkpoint($"{checkpointPrefix}: after Skia render");
        }
        finally { File.Move(hidden, baselinePath); }
    }

    private static void Capture(WalkthroughWindow window, string state)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { Length: > 0 } root) return;
        var folder = Path.Combine(root, "large-project-unparsed");
        Directory.CreateDirectory(folder);
        PageScreenshots.Save(window.Window, Path.Combine(folder, state + ".png"));
    }
}
