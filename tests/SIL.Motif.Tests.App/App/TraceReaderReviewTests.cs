using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TraceReaderReviewTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void SortedAggregateDoesNotChangeWindowRepresentatives()
    {
        var model = Model(TraceEnvelope.AnalysisRecords());
        Assert.Equal(["A", "B"], model.Analyses.Select(analysis => Assert.Single(analysis.Morphs).Form));
        Assert.Equal([2, 1], model.Analyses.Select(analysis => analysis.RecordCount));
        Assert.Equal(["a0", "b1", "a2"], model.SourceAnalyses.Select(analysis => analysis.AnalysisId));
        WithPanel(model, (window, texts) =>
        {
            Assert.Contains("A", texts);
            Assert.Contains("B", texts);
            Assert.Contains("Analysis 2", texts);
            Assert.Contains("No terminal attempt was recorded.", texts);
            Assert.DoesNotContain(texts, text => text.Contains("tree progress", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public void EqualRenderingsDoNotHideDifferentOrUnavailableMorphology(bool available, int count)
    {
        var model = Model(TraceEnvelope.AnalysisRecords(equalRendering: true, available));
        Assert.Equal(count, model.Analyses.Count);
        Assert.Equal(1, model.Analyses[1].RecordCount);
        WithPanel(model, (window, texts) =>
        {
            Assert.Contains($"Analysis {count}", texts);
            Assert.Contains(available ? "Parsed: 2 analyses, 3 source records" : "Parsed: 3 analyses", texts);
            if (!available) Assert.DoesNotContain("Recorded twice", texts);
        });
    }

    [Theory]
    [InlineData("matinlu", "4 attempts stopped")]
    [InlineData("captured-label", "1 attempt stopped")]
    public void UnattributedTerminalOutcomesHaveNoRuleCauseClaim(string scene, string expected)
    {
        var model = Scene(scene);
        model.ShowDroppedPaths = true;
        WithPanel(model, (window, texts) =>
        {
            Assert.Contains(texts, text => text.Contains(expected, StringComparison.Ordinal));
            Assert.Contains("Stopping rule not recorded", texts);
            Assert.DoesNotContain(texts, text => text.Contains("rules stopped", StringComparison.Ordinal));
            Assert.DoesNotContain("no rule", texts);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoTerminalNoticeReportsCompletionWithoutGuessingALexicalCause(bool capped)
    {
        var model = Model(TraceEnvelope.Of("", TraceEnvelope.InterruptedTree, capped));
        Assert.Empty(model.Candidates);
        Assert.NotEmpty(model.Reading!.Root.Children);
        WithPanel(model, (window, texts) =>
        {
            Assert.Contains(capped
                ? "No terminal attempt was recorded before the search stopped. Recorded progress is retained."
                : "No terminal attempt was recorded.", texts);
            Assert.DoesNotContain(texts, text => text.Contains("no lexicon entry", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void BroadTraceContextStaysUnmaterializedUntilOpened()
    {
        static WordTraceResponse Response(int count)
        {
            var branches = string.Join(",", Enumerable.Range(0, count).Select(index =>
                $"{{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"rule-{index}\",\"children\":[{{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"children\":[]}}]}}"));
            return WordTraceQuery.LoadDiagnostic(TraceEnvelope.Of("", "{\"type\":\"WordAnalysis\",\"children\":[" + branches + "]}")).Value!;
        }
        static (TraceWordViewModel Model, long Bytes) Measure(WordTraceResponse response)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var model = new TraceWordViewModel { Result = response };
            return (model, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Measure(Response(1));
        var small = Measure(Response(500));
        var large = Measure(Response(1000));
        Assert.True(large.Bytes < 2.5 * small.Bytes, $"500 branches: {small.Bytes}; 1000: {large.Bytes}");
        Assert.All(large.Model.Candidates, candidate => Assert.Empty(candidate.RecordedTreeContext));
        var last = large.Model.Candidates[^1];
        Assert.True(last.HasTreeContext);
        last.IsTreeContextExpanded = true;
        Assert.Equal(999, last.RecordedTreeContext.Count);
        Assert.Equal("0.998", last.RecordedTreeContext[^1].RecordedStep!.StepId);
        Assert.Single(last.RecordedTreeContext[^1].Children);
    }

    [ScreenshotFact]
    public void CaptureTraceReaderReviewScenes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        foreach (var scene in new[] { "matinlu", "captured-label", "interrupted", "completed-no-terminal", "interleaved", "equal-identities", "unavailable", "matinlu-context", "zodut", "missing", "unknown" })
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var width in new[] { 1040, 1240 })
        {
            var model = Scene(scene);
            model.ShowDroppedPaths = true;
            if (scene is "interrupted" or "completed-no-terminal") model.SearchText = "plural";
            if (scene == "zodut") model.SearchText = "vem";
            WithPanel(model, (window, texts) =>
            {
                Application.Current!.RequestedThemeVariant = theme;
                window.Width = width;
                if (scene is "captured-label" or "matinlu-context")
                {
                    foreach (var candidate in model.ClosestAttempts) candidate.IsTreeContextExpanded = true;
                }
                if (scene is "interrupted" or "completed-no-terminal" or "zodut" or "missing" or "unknown")
                {
                    var fullTree = window.GetVisualDescendants().OfType<Expander>()
                        .Single(expander => Avalonia.Automation.AutomationProperties.GetName(expander) == "Full derivation tree");
                    fullTree.IsExpanded = true;
                    if (scene is "missing" or "unknown") model.SelectedStep = model.FilteredRoots[0];
                    if (scene == "zodut") model.SelectedStep = model.FilteredRoots[0].Children[0].Children[0];
                }
                if (scene == "unavailable")
                    window.GetVisualDescendants().OfType<Expander>().Single(expander => expander.Header?.ToString() == "Recorded source analyses").IsExpanded = true;
                PageScreenshots.Settle(window);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No trace frame rendered.");
                frame.Save(Path.Combine(folder, $"trace-review-{scene}-{theme}-{width}.png"), PngBitmapEncoderOptions.Default);
            });
        }
        avalonia.Invoke(() => Application.Current!.RequestedThemeVariant = ThemeVariant.Light);
    }

    private static TraceWordViewModel Scene(string scene) => scene switch
    {
        "matinlu" => Model(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-matinlu.json"))),
        "matinlu-context" => MatinluContext(),
        "zodut" => Model(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-zodut-synthetic.json"))),
        "missing" => Model(TraceEnvelope.Of("", "{\"type\":\"Failed\",\"children\":[]}")),
        "unknown" => Model(TraceEnvelope.Of("", "{\"type\":\"Failed\",\"failureReason\":\"FutureReason\",\"children\":[]}")),
        "captured-label" => Model(TraceEnvelope.CapturedRuleLabel),
        "interrupted" => Model(TraceEnvelope.Of("", TraceEnvelope.InterruptedTree, capped: true)),
        "completed-no-terminal" => Model(TraceEnvelope.Of("", TraceEnvelope.InterruptedTree)),
        "interleaved" => Model(TraceEnvelope.AnalysisRecords()),
        "equal-identities" => Model(TraceEnvelope.AnalysisRecords(equalRendering: true)),
        "unavailable" => Model(TraceEnvelope.AnalysisRecords(equalRendering: true, available: false)),
        _ => throw new ArgumentOutOfRangeException(nameof(scene)),
    };

    private static TraceWordViewModel MatinluContext()
    {
        var model = Scene("matinlu");
        model.ShowDroppedPaths = true;
        var index = model.Reading!.StopGroups.ToList().FindIndex(group => group.Attempts.Any(attempt => attempt.AttemptId == "0.2.0.3.2"));
        model.SelectStopGroupCommand.Execute(model.StopGroups[index]);
        return model;
    }

    private static TraceWordViewModel Model(string json) => new() { Result = WordTraceQuery.LoadDiagnostic(json).Value! };

    private void WithPanel(TraceWordViewModel model, Action<Window, string[]> check) => avalonia.Invoke(() =>
    {
        var files = new ScriptedDiagnosticFiles();
        var tools = new DiagnosticToolsViewModel(model, new RecordingClipboard(), files, files);
        var panel = new DiagnosticPanel(tools);
        var window = new Window { Content = new ScrollViewer { Content = panel }, Width = 1240, Height = 1700 };
        try
        {
            window.Show();
            PageScreenshots.Settle(window);
            var texts = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "")
                .Concat(window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "")).Distinct().ToArray();
            check(window, texts);
        }
        finally { window.Close(); }
    });
}
