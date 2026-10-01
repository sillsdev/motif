using System.Globalization;
using Avalonia;
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
    public void RecentWordsSitApartSoTwoWordsNeverReadAsOne()
    {
        _avalonia.Invoke(() =>
        {
            var model = new TryWordPageModel(NewContext(out _));
            model.RecentWords.Add("kitabu");
            model.RecentWords.Add("matinlu");
            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            var window = new Window { Content = view, Width = 1240, Height = 800 };
            try
            {
                window.Show();
                window.UpdateLayout();

                var list = window.GetLogicalDescendants().OfType<ItemsControl>()
                    .Single(control => AutomationProperties.GetName(control) == "Recent words");
                var words = list.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.Text is "kitabu" or "matinlu")
                    .Select(text => new Rect(text.TranslatePoint(default, window)!.Value, text.Bounds.Size))
                    .ToArray();
                Assert.Equal(2, words.Length);
                Assert.True(Application.Current!.TryGetResource("Intent.Space.Related", null, out var gap));
                var apart = words[0].Top == words[1].Top ? words[1].Left - words[0].Right : words[1].Top - words[0].Bottom;
                Assert.True(apart >= (double)gap! - 0.5, $"the two recent words are {apart:F1} px apart");
            }
            finally
            {
                window.Close();
            }
        });
    }

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
                var tools = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Try a Word tools");
                // The flyout keeps the trace's capture details and writing systems the embedded panel no longer shows.
                var menu = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(tools.Flyout).Content);
                var names = menu.GetLogicalDescendants().OfType<CopyableTextBlock>()
                    .Select(block => AutomationProperties.GetName(block)).ToList();
                Assert.Contains("Diagnostic capture details", names);
                Assert.Contains("Writing system direction and font", names);
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
                [new TimingAggregateRow("Plural", 4, 1, 2, 1) { Kind = "morph_rule" }], [])
            {
                Words = [new TimingWordRow("dogs", 10, 2, TimingCompletion.Finished)],
            });
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.UtcNow, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            var row = Assert.Single(page.RulesOnBestPath);
            Assert.Equal("Plural", row.Rule);
            Assert.Equal("Affix rule", row.Kind);
            Assert.Equal("applied", row.Outcome);
            Assert.Equal("-s · dog → dogs", row.Explanation);
            // The stored share is of the word's whole parse time in that parse, not of the rules' recorded time.
            Assert.True(page.HasEarlierTiming);
            Assert.Equal("Share of 10 ms", page.EarlierShareHeader);
            Assert.Equal([("Plural", "Morphological rules", "4 ms", "40%"), ("Other time", "", "6 ms", "60%")],
                page.EarlierRuleTimes.Select(time => (time.Rule, time.KindLabel, time.TimeText, time.ShareText)));
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

            Assert.Equal("4 ms", Assert.Single(page.EarlierRuleTimes).TimeText);
            Assert.Equal("assessment-1", Assert.Single(fake.TimingRequests).AssessmentId);
        });
    }

    [Fact]
    public void TimingFromAnEarlierParseSaysWhenItRanAndThatItIsNotThisTry()
    {
        RunOnAvalonia(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            fake.TimingCompletesWith(DogsTiming());
            var parsedAt = new DateTimeOffset(2026, 9, 22, 9, 18, 0, TimeSpan.Zero);
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), parsedAt, false));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            var when = parsedAt.ToLocalTime().ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture);
            Assert.Equal($"Time from the parse of {when}", page.EarlierTimingTitle);
            Assert.Equal("Not from this try. In that parse dogs took 10 ms; each rule's time covers that parse's " +
                "whole search for the word, including attempts that stopped, not only the path above.",
                page.EarlierTimingSource);

            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1240, Height = 1600 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var bestPath = Assert.Single(window.GetLogicalDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetName(border) == "Rules on this word's best path");
                var texts = bestPath.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
                Assert.Contains("Rules on this word's best path", texts);
                Assert.DoesNotContain(texts, text => text is not null &&
                    (text.Contains("time", StringComparison.OrdinalIgnoreCase) || text.EndsWith('%')));
                var earlier = Assert.Single(window.GetLogicalDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetName(border) == "Time from an earlier parse");
                Assert.True(earlier.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TimingIsNotShownWhenItsParseCannotBeNamed()
    {
        RunOnAvalonia(async () =>
        {
            var (context, fake) = NewContext();
            context.ProjectPath = ProjectPath;
            context.Assess.ProjectPath = ProjectPath;
            var page = new TryWordPageModel(context);
            fake.TraceWordCompletesWith(DogsTrace());
            fake.TimingCompletesWith(DogsTiming());
            // A re-run replaced some words' times, so which parse a word's time came from is no longer one answer.
            context.PublishEvidence(new WorkspaceEvidence(Assessment() with { TimingOverrideAssessmentIds = ["rerun-1"] },
                DateTimeOffset.UtcNow, WasRerun: true));

            context.TryWord("dogs");
            await page.Trace.TryCommand.ExecutionTask!;

            Assert.False(page.HasEarlierTiming);
            Assert.Empty(page.EarlierRuleTimes);
        });
    }

    private static WordTraceResponse DogsTrace() => new("dogs", true, true, null, 1, null, 10,
        [new TraceCandidate([], true, null, "Built the word", [
            new TraceStep("MorphologicalRule", "Plural", "dog", "dogs", null, []) { OutcomeStatus = "succeeded" }])],
        new TraceStep("WordAnalysis", null, null, null, null, []));

    private static TimingResponse DogsTiming() => new("assessment-parse", "selected", "rule", 1, 10, 10, [],
        [new TimingAggregateRow("Plural", 4, 1, 2, 1) { Kind = "morph_rule" }], [])
    {
        Words = [new TimingWordRow("dogs", 10, 2, TimingCompletion.Finished)],
    };

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
                    .Where(block => block.IsVisible)
                    .Select(block => block.Text)
                    .OfType<string>()
                    .Select(value => value.Trim())
                    .Where(value => value is "Rule" or "Kind" or "Outcome" or "Explanation" or
                        "Stored time" or "Attempts" or "Word share").ToArray();
                Assert.Equal(new[] { "Rule", "Kind", "Outcome", "Explanation" }, headers);
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

            Assert.Single(page.RulesOnBestPath);
            Assert.False(page.HasEarlierTiming);
            Assert.Empty(page.EarlierRuleTimes);
            Assert.Equal("8.0 ms", Assert.Single(page.Trace.Effort).Time);
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
        var unfolded = new List<string>();
        double analysesTop = 0, rulesTop = 0;
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
            // The word parsed, so every rule on its best path applied; one attempt is left to show, in the singular.
            Assert.All(page.RulesOnBestPath, row => Assert.Equal("applied", row.Outcome));
            // The rules read in building order, outward from the stem, each with the affix's own form.
            Assert.Equal(["ma", "lu"], page.RulesOnBestPath.Select(row => row.Rule));
            Assert.Equal("ma- · tin → matin", page.RulesOnBestPath[0].Explanation);
            Assert.Equal("-lu · matin → matinlu", page.RulesOnBestPath[1].Explanation);
            // The parser's taking-apart pass is one line, the pieces in the word's own order.
            Assert.Equal("Taking the word apart found ma- · tin · -lu", page.TakingApartText);
            Assert.Equal("Show the other attempt", page.Trace.MoreAttemptsText);

            var view = PageRegistry.For(WorkspacePage.TryAWord).CreateView(page);
            var window = new Window { Content = view, Width = 1240, Height = 2400 };
            try
            {
                window.Show();
                Settle(window);
                visible.AddRange(VisibleTexts(view));
                analysesTop = TopOf(view, "Parsed: 1 analysis, found 2 ways", window);
                rulesTop = view.GetLogicalDescendants().OfType<Border>().Single(border =>
                        AutomationProperties.GetName(border) == "Rules on this word's best path")
                    .TranslatePoint(default, window)!.Value.Y;

                page.Trace.ShowDroppedPaths = true;
                Settle(window);
                unfolded.AddRange(VisibleTexts(view));
            }
            finally
            {
                window.Close();
            }
        });

        // A parsed word leads with its one analysis; the paths the parser dropped wait, folded, under a count.
        Assert.Contains("Parsed: 1 analysis, found 2 ways", visible);
        Assert.Contains("4 other paths the parser tried and dropped (normal)", visible);
        Assert.True(analysesTop < rulesTop, $"the analyses sit at {analysesTop:F0} px, below the rules at {rulesTop:F0} px");
        Assert.DoesNotContain("Why the other attempts stopped", visible);
        Assert.DoesNotContain("Further derivation is prohibited after a final template.", visible);
        Assert.Single(visible, text => text == "Analysis 1");
        Assert.DoesNotContain("Analysis 2", visible);
        Assert.Contains("Taking the word apart found ma- · tin · -lu", visible);

        Assert.Contains("Why the other attempts stopped", unfolded);
        Assert.Contains("Further derivation is prohibited after a final template.", unfolded);
        foreach (var texts in new[] { visible, unfolded })
        {
            Assert.Contains("Affix rule", texts);
            Assert.DoesNotContain("unknown morpheme", texts);
            // The parser's own morpheme names are its detail, kept for the tooltip.
            Assert.DoesNotContain(texts, text => text.Contains("MA+TIN+LU", StringComparison.Ordinal));
            Assert.DoesNotContain(texts, text => EngineWords.IsMatch(text));
        }
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static IEnumerable<string> VisibleTexts(Control view) => view.GetVisualDescendants().OfType<TextBlock>()
        .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
        .Select(block => block.Text!.Trim()).ToArray();

    private static double TopOf(Control view, string text, Window window) => view.GetVisualDescendants()
        .OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == text)
        .TranslatePoint(default, window)!.Value.Y;

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
