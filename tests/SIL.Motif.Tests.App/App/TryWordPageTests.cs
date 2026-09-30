using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Worker.Store;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TryWordPageTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private readonly AvaloniaHeadlessFixture _avalonia;

    public TryWordPageTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void RegistryBuildsTheTryAWordPageWithNamedLinksAndNoApprovalOutsideTheText()
    {
        _avalonia.Invoke(() =>
        {
            var context = NewContext(out _);
            var model = new TryWordPageModel(context);
            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal("TryAWordPage", view.GetType().Name);
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Open in Texts");
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "AI Handoff for this word");
                // Opinions change only in the text, so the page offers the text rather than an approval.
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    (AutomationProperties.GetName(button) ?? string.Empty).Contains("Approve", StringComparison.Ordinal));
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Open in Analyze texts");
                // Opening a saved trace is a tool, so it waits behind the menu beside Try it.
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Open a saved diagnostic");
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Try a Word tools");
                Assert.Contains(window.GetLogicalDescendants().OfType<Expander>(), expander =>
                    Equals(expander.Header, "Aggregate parser effort by category"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ATypedWordCanBeTriedWithoutTextDataAndItsRuleRowsUseOnlyStoredPerRuleTiming()
    {
        RunOnAvalonia(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var step = new TraceStep("MorphologicalRuleAnalysis", "Plural", "dog", "dogs", null, [])
            {
                OutcomeStatus = "succeeded",
            };
            fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
                [new TraceCandidate([], true, null, "Built the word", [step])],
                new TraceStep("WordAnalysis", null, null, null, null, [])));
            fake.TimingCompletesWith(new TimingResponse("assessment-1", "selected", "rule", 1, 10, 10, [],
                [new TimingAggregateRow("Plural", 4, 0.4, 2, 1)], []));
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            var row = Assert.Single(page.RulesOnBestPath);
            Assert.Equal("Plural", row.Rule);
            Assert.Equal("Affix rule", row.Kind);
            Assert.Equal("applied", row.Outcome);
            Assert.Equal("dog → dogs", row.Explanation);
            Assert.Equal("40%", row.Share);
            Assert.Equal("Open dogs in Analyze texts", page.OpenInTextsText);
            Assert.Contains("dogs", page.RecentWords);
            var request = Assert.Single(fake.TimingRequests);
            Assert.Equal("rule", request.By);
            Assert.Equal("assessment-parse", request.AssessmentId);
            Assert.Equal(["dogs"], request.ExplicitWords);
        });
    }

    [Fact]
    public void AStoredAssessmentSuppliesRuleTimingForANewTrace()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
                [new TraceCandidate([], true, null, "Built the word", [
                    new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])])],
                new TraceStep("WordAnalysis", null, null, null, null, [])));
            fake.TimingCompletesWith(new TimingResponse("assessment-1", "selected", "rule", 1, 10, 10, [],
                [new TimingAggregateRow("Plural", 4, 0.4, 2, 1)], []));

            await context.PublishCurrentEvidenceAsync(new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow, null,
                EvidenceFreshness.Current, null, null, null, null, StoredAssessment()));
            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.Equal("4.0 ms", Assert.Single(page.RulesOnBestPath).StoredTime);
            Assert.Equal("assessment-1", Assert.Single(fake.TimingRequests).AssessmentId);
        });
    }

    [Fact]
    public void RuleTimingAndWordLinksRouteThroughTheWorkspaceContext()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var timing = new TimingPageModel(context);
            var texts = new TextsPageModel(context);
            var handoff = new AiHandoffPageModel(context);
            fake.TraceWordCompletesWith(new WordTraceResponse("typed-only", false, true, null, 1, null, 1,
                [new TraceCandidate([], false, "failure", "Stopped", [
                    new TraceStep("MorphologicalRule", "Plural", "typed-only", null, "failure", [])
                    { OutcomeStatus = "failed" }])],
                new TraceStep("WordAnalysis", null, null, null, null, [])));

            context.TryWord("typed-only");
            await page.Trace.TryCommand.ExecutionTask!;
            Assert.Single(page.RulesOnBestPath).OpenTimingCommand.Execute(null);
            Assert.Equal(WorkspacePage.Timing, context.CurrentPage);
            Assert.Equal("Plural", timing.Focus!.Rule);
            Assert.Equal(["typed-only"], timing.Focus.Words);

            page.OpenInTextsCommand.Execute(null);
            Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
            Assert.Equal(TextsTab.AnalyzeTexts, texts.Tab);

            page.HandOffCommand.Execute(null);
            Assert.Equal(WorkspacePage.AiHandoff, context.CurrentPage);
            Assert.Equal(["typed-only"], handoff.Handoff.ChosenWords);
        });
    }

    [Fact]
    public void SidebarTimingLinkNamesAndOpensTheFirstRuleOnTheBestPath()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var timing = new TimingPageModel(context);
            fake.TraceWordCompletesWith(new WordTraceResponse("verb", true, true, null, 1, null, 1,
                [new TraceCandidate([], true, null, "Built the word", [
                    new TraceStep("MorphologicalRule", "Verb template", "stem", "verb", null, [])
                    { OutcomeStatus = "succeeded" }])],
                new TraceStep("WordAnalysis", null, null, null, null, [])));

            context.TryWord("verb");
            await page.Trace.TryCommand.ExecutionTask!;

            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var title = Assert.Single(window.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    AutomationProperties.GetName(block) == "Try a Word result");
                Assert.Equal("verb", title.Text);
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    AutomationProperties.GetName(block) == "Try a Word page description");
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CopyableTextBlock>(), block =>
                    AutomationProperties.GetName(block) == "Try a Word screenshot note");
                var ruleTable = Assert.Single(window.GetLogicalDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetName(border) == "Rules on this word's best path");
                var headers = ruleTable.GetLogicalDescendants().OfType<TextBlock>()
                    .Select(block => block.Text)
                    .OfType<string>()
                    .Select(value => value.Trim())
                    .Where(value => value is "Rule" or "Kind" or "Outcome" or "Explanation" or
                        "Stored time" or "Attempts" or "Word share").ToArray();
                Assert.Equal(new[] { "Rule", "Kind", "Outcome", "Explanation", "Word share" }, headers);
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "See Verb template in Timing");

                page.OpenTimingCommand.Execute(null);

                Assert.Equal("Verb template", timing.Focus!.Rule);
                Assert.Equal(["verb"], timing.Focus.Words);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CategoryTotalsRemainAggregateAndAreNeverCopiedToARuleStep()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            var effort = new TraceEffort("Morphology", 3, 1, 0, 0, 0, 2, 8);
            fake.TraceWordCompletesWith(new WordTraceResponse("dogs", true, true, null, 1, null, 10,
                [new TraceCandidate([], true, null, "Built the word", [
                    new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, [])])],
                new TraceStep("WordAnalysis", null, null, null, null, []))
            {
                Effort = [effort],
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            var row = Assert.Single(page.RulesOnBestPath);
            Assert.Equal("Not recorded", row.StoredTime);
            Assert.Equal("—", row.Share);
            Assert.Equal("8.0 ms", Assert.Single(page.Trace.Effort).Time);
            Assert.Equal("—", row.Attempts);
        });
    }

    // Parser class names such as MorphologicalRuleSynthesis, its reason codes, and Motif's own placeholders.
    private static readonly System.Text.RegularExpressions.Regex EngineWords = new(
        @"^\?$|[A-Z][a-z]+(?:Rule|Stratum|Template)?(?:Analysis|Synthesis)(?:Input|Output)?\b|MorphologicalRule|" +
        @"NonPartialRule|recorded attempt|Not recorded|not recorded|\bMSA\b|stratum|ordinal|GUID|Projection");

    [Fact]
    public void TheSeededTraceOfMatinluReadsInPlainWords()
    {
        var visible = new List<string>();
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-matinlu.json"))).Value!);
            var page = new TryWordPageModel(context);
            context.TryWord("matinlu");
            await page.Trace.TryCommand.ExecutionTask!;
            Assert.True(page.Trace.HasResult);

            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1240, Height = 2400 };
            try
            {
                window.Show();
                for (var pass = 0; pass < 3; pass++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }
                visible.AddRange(view.GetVisualDescendants().OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
                    .Select(block => block.Text!.Trim()));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Affix rule", visible);
        Assert.Contains("unknown morpheme", visible);
        Assert.Contains("Further derivation is prohibited after a final template.", visible);
        Assert.DoesNotContain(visible, text => EngineWords.IsMatch(text));
    }

    private static void RunOnAvalonia(Func<Task> work) =>
        AvaloniaHeadlessFixture.RunUntilComplete(work, TimeSpan.FromSeconds(10));

    private static (WorkspaceContext Context, FakeCommandClient Fake) NewContext()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return (new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake), fake,
            new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(fake)), fake);
    }

    private static WorkspaceContext NewContext(out FakeCommandClient fake)
    {
        (var context, fake) = NewContext();
        return context;
    }

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(new BaselineToken("project-1", "sha256:" + new string('a', 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64)), ProjectPath, DateTimeOffset.UtcNow,
            false, false), new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
            Measurements = [new ProducedAssessmentReference("assessment-1", "ObjectTiming", "invocation/one"),
                new ProducedAssessmentReference("assessment-parse", "ParseTime", "invocation/one")],
        };

    private static AssessmentRecord StoredAssessment() => new(
        "assessment-1", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
        "whitespace", "1", "{}", Selection.Create("Default", ["dogs"]), null, null,
        "sha256:grammar", null, null, null, "2026-09-24T12:00:00.0000000+00:00", Words: []);

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(Avalonia.Input.PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, Avalonia.Input.DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
