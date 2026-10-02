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
    [InlineData("matinlu", "4 attempts failed")]
    [InlineData("captured-label", "1 attempt failed")]
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

    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void ExpandedPathOutcomesHaveSpaceBeforeTheirNames(int width)
    {
        var model = Scene("matinlu");
        model.ShowDroppedPaths = true;
        WithPanel(model, (window, _) =>
        {
            window.Width = width;
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()
                         .Where(expander => expander.Header?.ToString() == "Steps on this path"))
                expander.IsExpanded = true;
            PageScreenshots.Settle(window);
            var rows = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                .Where(block => block.IsEffectivelyVisible && block.DataContext is TraceStepViewModel step &&
                    block.Text == step.Label && block.FindAncestorOfType<Expander>()?.Header?.ToString() == "Steps on this path")
                .ToArray();
            Assert.NotEmpty(rows);
            foreach (var name in rows)
            {
                var step = (TraceStepViewModel)name.DataContext!;
                var parent = name.FindAncestorOfType<Grid>()!;
                var outcome = parent.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Single(block => block.Text == step.StatusText);
                var right = outcome.TranslatePoint(new Point(outcome.Bounds.Width, 0), window)!.Value.X;
                var left = name.TranslatePoint(default, window)!.Value.X;
                Assert.True(left - right >= 4, $"{step.StatusText} runs into {step.Label}: gap {left - right:F1} px");
            }
        });
    }

    [Fact]
    public void MissingProducerIdDoesNotReplaceTheSavedTreeAddressOrInventAProducerId()
    {
        var step = new TraceStepViewModel(new TraceStep("WordAnalysis", null, null, null, null, [])
            { StepId = "0.0" }, null);
        Assert.Equal("Recorded event: 0.0", step.RecordedEventAddress);
        Assert.Equal("Tree address: 0.0; producer event ID not recorded", step.RecordedProducerEventAddress);
        Assert.DoesNotContain(step.Captured, detail => detail.Label == "Producer event ID");
    }

    [Fact]
    public void RepeatedTreeContextLabelsShowTheirOriginalEventAddresses()
    {
        var model = MatinluContext();
        WithPanel(model, (window, _) =>
        {
            var candidate = Assert.Single(model.ClosestAttempts);
            candidate.IsTreeContextExpanded = true;
            PageScreenshots.Settle(window);
            var context = window.GetVisualDescendants().OfType<Expander>()
                .Single(expander => expander.Header?.ToString() == "Recorded tree context" && expander.IsEffectivelyVisible);
            var texts = context.GetVisualDescendants().OfType<CopyableTextBlock>()
                .Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
            var repeated = candidate.RecordedTreeContext.GroupBy(step => step.Label)
                .Where(group => group.Count() > 1).ToArray();
            Assert.NotEmpty(repeated);
            foreach (var step in repeated.SelectMany(group => group))
            {
                Assert.Contains(step.RecordedEventAddress, texts);
                Assert.Contains($"Recorded event: {step.RecordedStep.StepId}", texts);
                Assert.Contains($"Producer event ID: {step.RecordedStep.EventEvidence!.ProducerStepId} " +
                    $"(tree address {step.RecordedStep.StepId})", texts);
            }
            Assert.Contains("Membership in this derivation is not recorded.", texts);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactRecordedPathsDistinguishProducerAndCapturedNames(bool ancestor)
    {
        var diagnostic = System.Text.Json.Nodes.JsonNode.Parse(TraceEnvelope.CapturedRuleLabel)!;
        if (ancestor)
        {
            var children = diagnostic["trace"]!["children"]!.AsArray();
            var terminal = children[1]!.DeepClone();
            children.RemoveAt(1);
            children[0]!["children"]!.AsArray().Add(terminal);
        }
        var model = Model(diagnostic.ToJsonString());
        WithPanel(model, (window, _) =>
        {
            var candidate = Assert.Single(model.ClosestAttempts);
            if (!ancestor) candidate.IsTreeContextExpanded = true;
            var header = ancestor ? "Steps on this path" : "Recorded tree context";
            var expander = window.GetVisualDescendants().OfType<Expander>()
                .Single(control => control.Header?.ToString() == header);
            expander.IsExpanded = true;
            PageScreenshots.Settle(window);
            var texts = expander.GetVisualDescendants().OfType<CopyableTextBlock>()
                .Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
            Assert.Contains("Producer: Producer name", texts);
            Assert.Contains("Captured FieldWorks: Vowel harmony", texts);
        });
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
                    window.GetVisualDescendants().OfType<Expander>().Single(expander => Avalonia.Automation.AutomationProperties.GetName(expander) == "Recorded source analyses").IsExpanded = true;
                PageScreenshots.Settle(window);
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No trace frame rendered.");
                frame.Save(Path.Combine(folder, $"trace-review-{scene}-{theme}-{width}.png"), PngBitmapEncoderOptions.Default);
            });
        }
        avalonia.Invoke(() => Application.Current!.RequestedThemeVariant = ThemeVariant.Light);
    }

    private static TraceWordViewModel Scene(string scene) => scene switch
    {
        "matinlu" => Model(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))),
        "matinlu-context" => MatinluContext(),
        "zodut" => Model(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-zodut-synthetic.json"))),
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
