using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.LCModel;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class LargeProjectScaleTests(ITestOutputHelper output)
{
    [Fact]
    public void LargeProjectCompletesTheFakeParserWorkflowWithinTimeAndMemoryBudgets()
    {
        var measurements = new ScaleMeasurements(output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            measurements.Checkpoint("Large: before fixture project/cache and ICU initialization");
            using var project = new LargeProjectFixture();
            measurements.Checkpoint("Large: after fixture project/cache and ICU initialization");
            output.WriteLine($"Large master creation: {project.CreationTime.TotalSeconds:F3} s");
            var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
            FakeParser.BehaveBesideExecutable(parser, new
            {
                words = project.Words.Select((word, index) => new
                {
                    word = word.Form,
                    outcome = index % 5 == 0 ? "capped" : index % 5 == 1 ? "no-analysis" : "complete",
                    elapsedMs = 1 + index % 20,
                    analyses = index % 5 < 2 ? Array.Empty<object>() : new object[]
                    {
                        new { morphs = new[] { new
                        {
                            form = word.AllomorphId.ToString("D"), msa = word.MsaId.ToString("D"),
                            inflType = (string?)null, guessedString = (string?)null,
                        } } },
                    },
                }),
            });
            var client = RealCommandClient.Create(project.ManagedRoot, parser);
            await measurements.MeasureAsync("Open and capture Baseline", 10, async () =>
            {
                measurements.Checkpoint("Large: before explicit cache open");
                using (var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath))
                {
                    measurements.Checkpoint("Large: after explicit cache open and ICU use");
                    Assert.Equal(LargeProjectFixture.EntryCount,
                        cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().Count());
                    Assert.Equal(LargeProjectFixture.EntryCount,
                        cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances().Count());
                    var opinions = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances()
                        .GroupBy(analysis => analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent)).ToArray();
                    Assert.Equal(3, opinions.Length);
                    Assert.All(opinions, group => Assert.Equal(1000, group.Count()));
                    Assert.All(cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances(),
                        entry => Assert.Single(entry.AlternateFormsOS));
                    Assert.Equal(LargeProjectFixture.TextCount,
                        cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances().Count());
                }
                var capture = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
                Assert.True(capture.Succeeded, capture.Refusal?.Message);
            });
            var configured = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Large Selection", project.TextIds, []), CancellationToken.None);
            Assert.True(configured.Succeeded, configured.Refusal?.Message);
            var skipped = await client.SkipSetupAsync(new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
            AssessCommandResponse assessed = null!;
            await measurements.MeasureAsync("Assess all 3000 words", 10, async () =>
            {
                var result = await client.AssessAsync(new AssessRequest(project.FwDataPath),
                    new Progress<AssessmentProgress>(), CancellationToken.None);
                Assert.True(result.Succeeded, result.Refusal?.Message);
                assessed = result.Value!;
                Assert.Equal(LargeProjectFixture.EntryCount, assessed.Words.Count);
                Assert.Equal(LargeProjectFixture.EntryCount, assessed.Words.Select(word => word.Word).Distinct().Count());
                Assert.Equal(1800, assessed.Words.Count(word => word.Outcome == "analysed"));
                Assert.Equal(600, assessed.Words.Count(word => word.Outcome == "no-analysis"));
                Assert.Equal(600, assessed.Words.Count(word => word.Outcome == "capped"));
            });
            await measurements.MeasureAsync("Stage 200 pending changes", 180, async () =>
            {
                var pending = await client.LoadPendingChangesAsync(
                    new PendingChangesRequest(project.FwDataPath, MotifProductVersion.CurrentText), CancellationToken.None);
                Assert.True(pending.Succeeded, pending.Refusal?.Message);
                var revision = pending.Value!.Revision;
                foreach (var word in project.Words.Take(200))
                {
                    var put = await client.PutPendingChangeAsync(new PutPendingChangeRequest(
                        project.FwDataPath, MotifProductVersion.CurrentText, revision,
                        new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                            CanonicalId.FromGuid(word.WordformId).Value, word.Form)), CancellationToken.None);
                    Assert.True(put.Succeeded, put.Refusal?.Message);
                    revision = put.Value!.Revision;
                }
            });
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            await measurements.MeasureAsync("Open stored Overview", 20, async () =>
            {
                measurements.Checkpoint("Large: before first window show");
                window.Show();
                measurements.Checkpoint("Large: after first window show, before render");
                window.OpenRecentProjectByClick(project.FwDataPath);
                await window.Workspace.Context.EvidencePublication;
                window.WaitUntilProjectIsQuiet(TimeSpan.FromSeconds(30), "large project did not finish opening");
                window.ShowPage(WorkspacePage.Overview);
                Assert.NotNull(window.Workspace.PageModel<OverviewPageModel>().Overview);
                PageScreenshots.Settle(window.Window);
                measurements.Checkpoint("Large: after first Skia render");
            });
            measurements.AssertReadCounts("Open stored Overview", 1, 267, 3000, 162189);
            Capture(window, "overview");
            var texts = window.Workspace.PageModel<TextsPageModel>();
            await measurements.MeasureAsync("Open Matrix", 15, () =>
            {
                window.ShowPage(WorkspacePage.Texts);
                window.ShowTextsTab(TextsTab.Matrix);
                Assert.Equal(LargeProjectFixture.EntryCount, texts.Assess.Compare.Cells.Sum(cell => cell.WordCount));
                PageScreenshots.Settle(window.Window);
            });
            Capture(window, "matrix");
            await measurements.MeasureAsync("Open largest List", 5, () =>
            {
                window.ShowTextsTab(TextsTab.Lists);
                var largest = texts.TextsLists.Lists.MaxBy(list => list.WordCount)!;
                Assert.True(largest.WordCount > 0);
                texts.TextsLists.SelectListCommand.Execute(largest);
                PageScreenshots.Settle(window.Window);
                output.WriteLine($"Largest List: {largest.Name}, {largest.WordCount} words");
            });
            Capture(window, "lists");
            await measurements.MeasureAsync("Analyze longest Text and scroll to end", 6, async () =>
            {
                window.ShowTextsTab(TextsTab.AnalyzeTexts);
                window.WaitUntil(() => texts.ResultsInText.Texts.Count == LargeProjectFixture.TextCount,
                    TimeSpan.FromSeconds(30), "the large Texts were not loaded");
                var readerModel = texts.ResultsInText;
                await readerModel.ReadStateRefresh;
                Assert.Equal(LargeProjectFixture.OccurrenceCount, readerModel.AllCount);
                var longest = readerModel.Texts.Single(text => text.TextId == project.TextIds[0]);
                Assert.Equal(50, longest.Summary!.LineCount);
                readerModel.SelectedText = longest;
                var panel = window.Window.GetLogicalDescendants().OfType<ResultsInTextPanel>().Single();
                var finalPosition = window.Workspace.Context.SelectionReads.Summary!.SourcePositions.Last(position =>
                    position.Location.Anchor.TextId == longest.TextId);
                Assert.True(await panel.FocusOccurrenceAsync(finalPosition.Location.Anchor));
                await AnalyzeTextsLayoutTests.SettleReaderAsync(window.Workspace, window.Window);
                var diagnostics = window.Workspace.Context.SelectionReads.Reader!.Diagnostics;
                Assert.InRange(diagnostics.LiveLineModels, 1, 48);
                Assert.InRange(diagnostics.LiveTokenModels, 1, 512);
                var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => viewer.IsEffectivelyVisible && viewer.Content is ItemsControl);
                var strip = Assert.Single(AnalyzeTextsLayoutTests.Strips(panel), strip =>
                    ResultsInTextPanel.TokenOf(strip)?.Occurrence == finalPosition.Location.Anchor);
                var origin = strip.TranslatePoint(new Point(), reader);
                Assert.NotNull(origin);
                Assert.True(new Rect(reader.Viewport).Intersects(new Rect(origin!.Value, strip.Bounds.Size)));
            });
            measurements.AssertReadCounts("Analyze longest Text and scroll to end", 1, 128, 1, 4096);
            Capture(window, "analyze-end");
            await measurements.MeasureAsync("Open Timing", 5, () =>
            {
                window.ShowPage(WorkspacePage.Timing);
                Assert.True(window.Workspace.PageModel<TimingPageModel>().HasStoredTiming);
                PageScreenshots.Settle(window.Window);
            });
            Capture(window, "timing");
            await measurements.MeasureAsync("Open Warnings and check grammar", 15, async () =>
            {
                window.ShowPage(WorkspacePage.Warnings);
                var warnings = window.Workspace.PageModel<WarningsPageModel>();
                await warnings.CheckGrammarCommand.ExecuteAsync(null);
                Assert.True(warnings.Grammar.HasChecked);
                Assert.True(warnings.Grammar.Warnings.HasAny);
                PageScreenshots.Settle(window.Window);
            });
            Capture(window, "warnings");
            await measurements.MeasureAsync("Write AI Handoff", 30, async () =>
            {
                var destination = Path.Combine(project.ManagedRoot, "handoff");
                var handoff = await client.HandoffAsync(new HandoffRequest(project.FwDataPath, destination,
                    new SelectionRequest(false, project.TextIds, [], false, null), true, assessed.InvocationId),
                    new Progress<AssessmentProgress>(), CancellationToken.None);
                Assert.True(handoff.Succeeded, handoff.Refusal?.Message);
                Assert.Contains("texts.json", handoff.Value!.Files);
                Assert.Contains("parse-results.json", handoff.Value.Files);
                Assert.All(handoff.Value.Files, file => Assert.True(File.Exists(Path.Combine(destination, file)), file));
            });
            await measurements.MeasureAsync("Open Review with 200 changes", 15, () =>
            {
                window.ShowPage(WorkspacePage.Review);
                Assert.Equal(200, window.Workspace.PageModel<ReviewPageModel>().Changes.Count);
                PageScreenshots.Settle(window.Window);
            });
            measurements.AssertBudgets();
            Capture(window, "review-200");
        }, TimeSpan.FromMinutes(10));
        using var process = Process.GetCurrentProcess();
        output.WriteLine($"Host lifetime peak RSS: {process.PeakWorkingSet64 / 1048576d:F1} MiB");
        measurements.AssertBudgets();
    }

    [RealParserFact]
    public async Task RealParserReportsPerWordPercentilesFor500WordsInTheLargeGrammar()
    {
        using var project = new LargeProjectFixture();
        var parser = PanGlossExecutable.TryLocate()!;
        output.WriteLine($"Parser: {parser}; master creation {project.CreationTime.TotalSeconds:F3} s");
        var client = RealCommandClient.Create(project.ManagedRoot, parser);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var capture = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath), cancellation.Token);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var clock = Stopwatch.StartNew();
        var result = await client.AssessAsync(new AssessRequest(project.FwDataPath,
            new SelectionRequest(false, [], project.Words.Take(500).Select(word => word.Form).ToArray(), false, null)),
            new Progress<AssessmentProgress>(), cancellation.Token);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(500, result.Value!.Words.Count);
        Assert.All(result.Value.Words, word => Assert.NotEmpty(word.Morphology!.Analyses));
        var id = result.Value!.Measurements.Single(measurement => measurement.Kind == "ParseTime").AssessmentId;
        var timing = await client.TimingAsync(new TimingRequest(project.FwDataPath, id, Top: 500), cancellation.Token);
        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        Assert.Equal(500, timing.Value!.Words.Count);
        var milliseconds = timing.Value.Words.Select(word => word.ElapsedNs is { } ns ? ns / 1_000_000d : word.ElapsedMs!.Value)
            .Order().ToArray();
        double Percentile(double fraction) => milliseconds[(int)Math.Ceiling(fraction * milliseconds.Length) - 1];
        output.WriteLine($"REAL SCALE | 500 words | wall {clock.Elapsed.TotalSeconds:F3} s | " +
            $"p50 {Percentile(.50):F6} ms | p95 {Percentile(.95):F6} ms | p99 {Percentile(.99):F6} ms | max {milliseconds[^1]:F6} ms");
        output.WriteLine($"Completion: {string.Join(", ", timing.Value.Words.GroupBy(word => word.Completion).Select(group => $"{group.Key}={group.Count()}"))}");
        using var process = Process.GetCurrentProcess();
        output.WriteLine($"Host lifetime peak RSS: {process.PeakWorkingSet64 / 1048576d:F1} MiB");
    }

    private static void Capture(WalkthroughWindow window, string state)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { Length: > 0 } root) return;
        var folder = Path.Combine(root, "large-project");
        Directory.CreateDirectory(folder);
        PageScreenshots.Save(window.Window, Path.Combine(folder, state + ".png"));
    }
}
