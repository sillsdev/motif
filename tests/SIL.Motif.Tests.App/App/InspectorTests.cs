using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// The inspector opens beside the page on a click of a name, from Analyze texts, Try a Word and Timing alike, and
/// its breadcrumb and Esc step back to where it opened.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class InspectorTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Fact]
    public void AGlosslessSenseNamesTheAnalysisGlossShownOnItsCard()
    {
        var facts = new ObjectFacts { Senses = [new ObjectFactsSense("sense-a", "1")] };

        var sense = Assert.Single(InspectorFactViewModel.Rows(facts, "3PL"));

        Assert.Equal("Sense", sense.Label);
        Assert.Equal("1 has no gloss; the analysis glosses it 3PL", sense.Value);
    }

    [Fact]
    public void AMorphemeChipInAnalyzeTextsOpensTheInspectorOnThatMorphemeAndThePageStaysPut()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = default(FakeCommandClient)!;
            var (workspace, window) = await AnalyzeTexts(configure: (client, _) => fake = client);
            try
            {
                var chip = CardChip(window, "a-");
                var morph = Assert.IsType<ParserReadingMorphViewModel>(chip.Tag);
                Click(window, chip);
                await workspace.Inspector.Loading;
                Settle(window);

                var inspector = InspectorPanel(window);
                Assert.True(inspector.IsEffectivelyVisible);
                Assert.False(SelectionHost(window).IsEffectivelyVisible);
                var asked = Assert.Single(fake.InspectRequests).Subject;
                Assert.Equal(morph.AllomorphId, asked.AllomorphId);
                Assert.Equal(morph.GrammaticalInfoId, asked.GrammaticalInfoId);
                Assert.Equal("a-", workspace.Inspector.Title);
                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.NotNull(AnalyzeTextsLayoutTests.OpenCard(window));
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheFirstBreadcrumbNamesTheWordAndReturnsToItsCardWithTheKeyboardOnTheChip()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTexts();
            try
            {
                var chip = CardChip(window, "kul");
                Click(window, chip);
                await workspace.Inspector.Loading;
                Settle(window);

                var origin = Crumbs(window)[0];
                Assert.Equal("alikula", ((InspectorCrumbViewModel)origin.DataContext!).Label);
                Click(window, origin);
                Settle(window);

                Assert.False(workspace.Inspector.IsOpen);
                Assert.Null(workspace.Context.Inspector);
                Assert.False(InspectorPanel(window).IsEffectivelyVisible);
                Assert.True(SelectionHost(window).IsEffectivelyVisible);
                Assert.NotNull(AnalyzeTextsLayoutTests.OpenCard(window));
                Assert.True(CardChip(window, "kul").IsFocused, "Closing puts the keyboard back on the chip it opened from.");
            }
            finally
            {
                window.Close();
            }

        }, Deadline);
    }

    [Fact]
    public void EscapeStepsBackOneCrumbAtATime()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTexts(configure: (fake, _) => fake.OnInspect((request, _) =>
                Task.FromResult(CommandOutcome<InspectResponse>.Success(TwoAllomorphs(request.Subject)))));
            try
            {
                Click(window, CardChip(window, "a-"));
                await workspace.Inspector.Loading;
                Settle(window);
                var other = Assert.Single(InspectorPanel(window).GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("inspectLink") && AutomationProperties.GetName(button) == "Inspect yu-");
                Click(window, other);
                await workspace.Inspector.Loading;
                Settle(window);
                Assert.Equal(["alikula", "a-", "yu-"], workspace.Inspector.Crumbs.Select(crumb => crumb.Label));
                Assert.Equal("yu-", workspace.Inspector.Title);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await workspace.Inspector.Loading;
                Settle(window);
                Assert.Equal(["alikula", "a-"], workspace.Inspector.Crumbs.Select(crumb => crumb.Label));
                Assert.Equal("a-", workspace.Inspector.Title);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Settle(window);
                Assert.False(workspace.Inspector.IsOpen);
                Assert.True(CardChip(window, "a-").IsFocused);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TryAWordsAnalysisAndRecordedRuleAndTimingsRuleRowOpenTheSameInspector()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = default(FakeCommandClient)!;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (client, assessment) =>
            {
                fake = client;
                OverviewTimingScreenshots.ReadOverviewAndTiming(client, assessment);
                var trace = PageScreenshots.TraceWithIdentities();
                var morph = new TraceMorph("morph", "ma-", null, "prefix", null, null, null, null, null, null)
                {
                    FormId = "11111111-1111-1111-1111-111111111111",
                    MsaId = "22222222-2222-2222-2222-222222222222", IdentityQuality = "authored",
                };
                client.TraceWordCompletesWith(trace with
                {
                    Reading = trace.Reading with
                    {
                        Analyses = [new TraceAnalysis("analysis", 0, "matinlu", "available", [morph])],
                        LogicalAnalyses = [new TraceLogicalAnalysis("analysis", [0])],
                    },
                });
            });
            try
            {
                window.Width = 1240;
                window.Height = 1500;
                workspace.Context.TryWord("matinlu");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                Settle(window);
                var inspector = InspectorPanel(window);

                var chip = TraceChip(window);
                var morph = Assert.IsType<TraceMorphViewModel>(chip.DataContext);
                Click(window, chip);
                await workspace.Inspector.Loading;
                Settle(window);
                Assert.NotNull(morph.InspectSubject!.AllomorphId);
                Assert.Equal(morph.InspectSubject.AllomorphId, fake.InspectRequests[^1].Subject.AllomorphId);
                Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);
                Assert.Same(inspector, InspectorPanel(window));
                Assert.True(inspector.IsEffectivelyVisible);

                var timingKey = new TraceTimingKey("morph_rule", OverviewTimingScreenshots.SubjectAgreementRuleKey);
                var link = RecordedRuleLink(window, timingKey);
                var rule = Assert.IsType<TraceStepViewModel>(link.DataContext);
                Click(window, link);
                await workspace.Inspector.Loading;
                Settle(window);
                Assert.Equal(rule.Reference!.TimingKey, fake.InspectRequests[^1].Subject.TimingKey);
                Assert.Equal(InspectorSubjectKind.Rule, fake.InspectRequests[^1].Subject.Kind);
                Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);

                workspace.Context.CloseInspector();
                workspace.CurrentPage = WorkspacePage.Timing;
                await Until(window, () => HasRuleLink(window, "Timing by rule"));
                Click(window, RuleLinkIn(window, "Timing by rule", timingKey));
                await workspace.Inspector.Loading;
                Settle(window);
                var asked = fake.InspectRequests[^1].Subject;
                Assert.Equal(InspectorSubjectKind.Rule, asked.Kind);
                Assert.Equal(rule.InspectSubject!.TimingKey, asked.TimingKey);
                Assert.Equal(WorkspacePage.Timing, workspace.CurrentPage);
                Assert.Same(inspector, InspectorPanel(window));
                Assert.Equal("Timing", workspace.Inspector.Crumbs[0].Label);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheSectionsComeInTheOrderALinguistAsksEachNamingItsSourceAndKeptShort()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTexts(configure: (fake, _) =>
            {
                fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(Busy(request.Subject))));
            });
            try
            {
                Click(window, CardChip(window, "a-"));
                await workspace.Inspector.Loading;
                Settle(window);

                var sections = InspectorPanel(window).GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("inspectorSection") && border.IsEffectivelyVisible)
                    .ToArray();
                Assert.Equal(
                    ["What it is", "Your words that use it", "Time in your words", "Grammar warnings about it", "In FieldWorks"],
                    sections.Select(AutomationProperties.GetName));
                Assert.All(sections, section => Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Classes.Contains("inspectorSource") && text.Text is { Length: > 0 }));
                Assert.Equal(["From the Baseline", "From the last Parse all words", "From the last Parse all words",
                        "From the last grammar check", "From the Baseline"],
                    sections.Select(section => section.GetVisualDescendants().OfType<TextBlock>()
                        .First(text => text.Classes.Contains("inspectorSource")).Text));

                var uses = workspace.Inspector.Uses!;
                Assert.Equal(3, uses.Shown.Count);
                Assert.Equal("Show all 5", uses.ShowAllText);
                Assert.Equal("Not counting 2 disapproved", uses.NotCountingText);
                var tryLinks = InspectorPanel(window).GetVisualDescendants().OfType<HyperlinkButton>()
                    .Where(link => Equals(link.Content, "Try a Word")).ToArray();
                Assert.NotEmpty(tryLinks);
                Assert.All(tryLinks, link =>
                {
                    Assert.Equal(0, link.Opacity);
                    Assert.True(link.Focusable);
                    Assert.Contains("revealOnHover", link.Classes);
                });
                tryLinks[0].Focus(NavigationMethod.Tab);
                Settle(window);
                Assert.True(tryLinks[0].Opacity > 0);

                uses.ShowAllCommand.Execute(null);
                Assert.Equal(5, uses.Shown.Count);

                Assert.Equal("Allomorph can't be split into phonemes", Assert.Single(workspace.Inspector.Warnings).Title);
                var factItems = InspectorPanel(window).FindControl<ItemsControl>("InspectorFactsItems")!;
                factItems.ScrollIntoView(factItems.ItemCount - 1);
                Settle(window);
                var facts = sections[^1].GetVisualDescendants().OfType<HyperlinkButton>()
                    .Where(link => link is not InspectLink && link.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(facts);
                Assert.All(facts, link => Assert.EndsWith(" ↗", Assert.IsType<string>(link.Content)));
                Assert.Contains(facts, link => (string)link.Content! == "Lexicon Edit ↗");
                Assert.Contains(facts, link => (string)link.Content! == "Category Edit ↗");
                Assert.All(facts, link => Assert.Contains("revealControl", link.Classes));
                Assert.All(facts, link => Assert.True(link.Focusable, "A link hidden until hover is still reachable by keyboard."));
                Assert.All(facts, link => Assert.Contains("revealOnHover", link.Classes));
                Assert.All(facts, link => Assert.Equal(0, link.Opacity));
                facts[0].Focus(NavigationMethod.Tab);
                Settle(window);
                Assert.True(facts[0].Opacity > 0);
                Assert.All(sections, section => Assert.Contains("hoverReveal", section.Classes));

                var mark = Assert.Single(sections[^1].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(),
                    dot => dot.Classes.Contains("freshDot"));
                Assert.Contains("current", mark.Classes);
                Assert.Contains("hasn't changed", Assert.IsType<string>(ToolTip.GetTip(mark)), StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void FactsFromABaselineFieldWorksHasSinceChangedWearTheTopBarsStaleMark()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTexts(configure: (fake, _) => fake.OnInspect((request, _) =>
                Task.FromResult(CommandOutcome<InspectResponse>.Success(Busy(request.Subject) with { IsStale = true }))));
            try
            {
                Click(window, CardChip(window, "a-"));
                await workspace.Inspector.Loading;
                Settle(window);

                var marks = InspectorPanel(window).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>()
                    .Where(dot => dot.Classes.Contains("freshDot") && dot.IsEffectivelyVisible).ToArray();
                Assert.Equal(2, marks.Length);
                Assert.All(marks, mark =>
                {
                    Assert.Contains("stale", mark.Classes);
                    Assert.DoesNotContain("current", mark.Classes);
                    Assert.Equal("FieldWorks has changed since your last Refresh; these facts are from then.", ToolTip.GetTip(mark));
                });
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> AnalyzeTexts(
        Action<FakeCommandClient, AssessCommandResponse>? configure = null)
    {
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: configure);
        window.Width = 1240;
        window.Height = 780;
        workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
        workspace.CurrentPage = WorkspacePage.Texts;
        Settle(window);
        return (workspace, window);
    }

    internal static Inspector InspectorPanel(Window window) => Assert.Single(window.GetLogicalDescendants().OfType<Inspector>());

    private static ContentControl SelectionHost(Window window) =>
        Assert.Single(window.GetLogicalDescendants().OfType<TextsPage>())
            .FindControl<ContentControl>("SelectionHost")!;

    internal static Border CardChip(Window window, string form) =>
        AnalyzeTextsLayoutTests.OpenCard(window).GetVisualDescendants().OfType<Border>().First(border =>
            border.Classes.Contains("morph") && border.IsEffectivelyVisible &&
            border.Tag is ParserReadingMorphViewModel morph && morph.Form == form);

    private static IReadOnlyList<Button> Crumbs(Window window) =>
        InspectorPanel(window).GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("crumb")).ToArray();

    private static InspectLink TraceChip(Window window) =>
        window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(items => AutomationProperties.GetName(items) == "Recorded analyses" && items.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<InspectLink>().First(link => link.IsEffectivelyVisible);

    private static InspectLink RecordedRuleLink(Window window, TraceTimingKey? timingKey = null) =>
        window.GetVisualDescendants().OfType<ListBox>()
            .Single(list => AutomationProperties.GetName(list) == "Plain trace steps")
            .GetVisualDescendants().OfType<InspectLink>().First(link => link.IsEffectivelyVisible &&
                (timingKey is null || link.DataContext is TraceStepViewModel step &&
                    step.Reference?.TimingKey == timingKey));

    private static Button RuleLinkIn(Window window, string list, TraceTimingKey? timingKey = null) =>
        window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(items => AutomationProperties.GetName(items) == list && items.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("inspectLink") && button.IsEffectivelyVisible &&
                (timingKey is null || button.DataContext is TimingRuleRow row &&
                    row.InspectSubject.TimingKey == timingKey));

    private static bool HasRuleLink(Window window, string list) =>
        window.GetVisualDescendants().OfType<ItemsControl>()
            .Any(items => AutomationProperties.GetName(items) == list && items.IsEffectivelyVisible &&
                items.GetVisualDescendants().OfType<Button>().Any(button => button.Classes.Contains("inspectLink")));

    private static async Task Until(Window window, Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Settle(window);
        }
        Assert.True(condition(), "The window never reached the state the test waits for.");
    }

    private static void Click(Window window, Control target)
    {
        target.BringIntoView();
        Settle(window);
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Settle(window);
    }

    private static void Settle(Window window) => AnalyzeTextsLayoutTests.Settle(window);

    private static TraceFieldWorksTarget Tool(string tool, string name, string id) =>
        new(tool, name, id, $"silfw://localhost/link?tool={tool}&guid={id}");

    private static InspectResponse TwoAllomorphs(InspectorSubject asked) => new(asked, InspectorResolution.Resolved)
    {
        AssessmentId = "assessment/one",
        Uses = InspectorSection<ObjectUseWords>.Of(new ObjectUseWords([], [])),
        Facts = InspectorSection<ObjectFacts>.Of(new ObjectFacts
        {
            Entry = new ObjectFactsEntry("entry-a", "a-") { MorphType = "prefix", FieldWorks = Tool("lexiconEdit", "Lexicon Edit", "entry-a") },
            Allomorphs =
            [
                new ObjectFactsAllomorph(asked.AllomorphId ?? "a-form", "a-") { IsAsked = asked.Label == "a-" },
                new ObjectFactsAllomorph("yu-form", "yu-") { IsAsked = asked.Label == "yu-" },
            ],
        }),
    };

    private static InspectResponse Busy(InspectorSubject asked)
    {
        ObjectUseWord Word(string word) => new(new SIL.Motif.Contract.Responses.WordRow(word, WordRowOutcome.NoParse, "Lost", WordRowTone.Problem));
        var words = new[] { "walikata", "anakata", "wamekata", "hawajafika", "hatujaona" }.Select(Word).ToArray();
        return new InspectResponse(asked, InspectorResolution.Resolved)
        {
            AssessmentId = "assessment/one",
            TimingKey = new TraceTimingKey("morph_rule", "msa-a"),
            Uses = InspectorSection<ObjectUseWords>.Of(
                new ObjectUseWords(words, [new ObjectUseMeaning("Lost", WordRowTone.Problem, 5)]) { NotCountingDisapproved = 2 }),
            RanIn = InspectorSection<ObjectUseWords>.Of(new ObjectUseWords([words[0] with { Calls = 4, ElapsedNs = 1_200_000 }],
                [new ObjectUseMeaning("Lost", WordRowTone.Problem, 1)])),
            Warnings = InspectorSection<IReadOnlyList<GrammarWarning>>.Of([NamingWarning()]),
            Facts = InspectorSection<ObjectFacts>.Of(new ObjectFacts
            {
                Entry = new ObjectFactsEntry("entry-a", "a-") { MorphType = "prefix", FieldWorks = Tool("lexiconEdit", "Lexicon Edit", "entry-a") },
                Senses = [new ObjectFactsSense("sense-a", "1") { Gloss = "3SG", FieldWorks = Tool("lexiconEdit", "Lexicon Edit", "entry-a") }],
                GrammaticalInfo = new ObjectFactsGrammaticalInfo("msa-a", "inflectionalAffix")
                {
                    Category = new ObjectFactsNamed("verb", "Verb") { FieldWorks = Tool("posEdit", "Category Edit", "verb") },
                },
                Allomorphs = [new ObjectFactsAllomorph(asked.AllomorphId!, "a-") { IsAsked = true }],
            }),
        };
    }

    [Fact]
    public void RanInCallSummaryMarksTheRecordedTotalWhenSomeWordsHaveNoCallCount()
    {
        var first = new ObjectUseWord(new SIL.Motif.Contract.Responses.WordRow("one", WordRowOutcome.NoParse, "Lost", WordRowTone.Problem))
        {
            Calls = 5,
        };
        var second = new ObjectUseWord(new SIL.Motif.Contract.Responses.WordRow("two", WordRowOutcome.NoParse, "Lost", WordRowTone.Problem));

        var ranIn = InspectorWordsViewModel.ForRanIn(new ObjectUseWords([first, second], []), _ => { });

        Assert.Equal("Ran in 2 words · 5 recorded calls · some call counts unavailable", ranIn.Heading);
    }

    [Fact]
    public void ASectionWhoseSourceIsMissingSaysWhyAndTheBaselinesFactsStillShow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTexts(configure: (fake, _) => fake.OnInspect((request, _) =>
                Task.FromResult(CommandOutcome<InspectResponse>.Success(Busy(request.Subject) with
                {
                    AssessmentId = null,
                    Uses = InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Absent, "Parse all words to see your words."),
                    RanIn = InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Absent, "Parse all words to see your words."),
                    Warnings = InspectorSection<IReadOnlyList<GrammarWarning>>.Not(InspectorSectionStatus.Absent, "Not checked."),
                }))));
            try
            {
                Click(window, CardChip(window, "a-"));
                await workspace.Inspector.Loading;
                Settle(window);

                var inspector = workspace.Inspector;
                Assert.Null(inspector.Uses);
                Assert.Equal("Parse all words to see your words.", inspector.UsesNote);
                Assert.True(inspector.HasUses);
                Assert.Equal("Parse all words to see your words.", inspector.RanInNote);
                Assert.True(inspector.HasWarnings);
                Assert.Contains(InspectorPanel(window).GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Not checked." && text.IsEffectivelyVisible);
                Assert.True(inspector.HasFacts);
                Assert.NotEmpty(inspector.Facts);
                Assert.Contains(InspectorPanel(window).GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Parse all words to see your words." && text.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Theory]
    [InlineData("before-refresh", true)]
    [InlineData("after-refresh", false)]
    [InlineData(null, false)]
    public void FromATraceAfterARefreshWithNoParseTheTracesDetailsStayApartFromTheBaselinesFacts(string? tracedOn, bool refreshedSince)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, configure: (fake, _) =>
            {
                fake.TraceWordCompletesWith(PageScreenshots.TraceWithIdentities() with
                {
                    HostCapture = tracedOn is null ? null : new TraceHostCapture(null, null, null, tracedOn, null, null, []),
                });
                fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(
                    new InspectResponse(request.Subject, InspectorResolution.Resolved)
                    {
                        BaselineDigest = "after-refresh",
                        Facts = InspectorSection<ObjectFacts>.Of(new ObjectFacts
                        {
                            Rule = new ObjectFactsRule(request.Subject.TimingKey!.Key, "affixRule", "ma")
                                { FieldWorks = Tool("lexiconEdit", "Lexicon Edit", "ma") },
                        }),
                        Uses = InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Unsupported, "Only a morpheme is used by words."),
                        RanIn = InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Absent, "Parse all words to see your words."),
                    })));
            });
            try
            {
                window.Width = 1240;
                window.Height = 1500;
                workspace.Context.TryWord("matinlu");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                Settle(window);
                Click(window, RecordedRuleLink(window));
                await workspace.Inspector.Loading;
                Settle(window);

                var inspector = workspace.Inspector;
                Assert.Equal("In this trace of matinlu", inspector.TraceTitle);
                Assert.Contains(inspector.TraceDetails, detail => detail.Label == "Outcome");
                Assert.Equal(refreshedSince, inspector.HasTraceNote);
                Assert.Equal("ma", Assert.Single(inspector.Facts).Value);
                Assert.Null(inspector.RanIn);
                Assert.Equal("Parse all words to see your words.", inspector.RanInNote);
                Assert.False(inspector.HasUses);
                var sections = InspectorPanel(window).GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("inspectorSection") && border.IsEffectivelyVisible)
                    .Select(border => AutomationProperties.GetName(border) ?? string.Empty).ToArray();
                Assert.Equal(["In this trace", "What it is", "Words it ran in", "In FieldWorks"], sections);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Theory]
    [InlineData("numobel", "step-8", "mpr1", "mpr2", null)]
    [InlineData("kumata", "step-11", "owner-payload-not-captured", "", 0)]
    public void SelectedCompoundOperandsAndProducerEventStayInTheInspectorsTraceSection(
        string word, string producerId, string firstOperand, string secondOperand, int? subrule)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var raw = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,
                "TestFixtures", $"trace-details-v3-{word}.json"));
            var response = WordTraceQuery.LoadDiagnostic(raw).Value!;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var step = Steps(response.Reading.Root).Single(item => item.EventEvidence?.ProducerStepId == producerId);
                var selected = new TraceStepViewModel(step, null, refs: response.Reading.Refs.ToDictionary(item => item.Id));
                Assert.NotNull(selected.InspectSubject);
                workspace.Context.OpenInspector(selected.InspectSubject!, word,
                    new InspectorTrace(word, null), selected.Captured);
                await workspace.Inspector.Loading;
                Settle(window);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.Equal($"In this trace of {word}", workspace.Inspector.TraceTitle);
                var details = workspace.Inspector.TraceDetails;
                Assert.Contains(details, detail => detail.Label == "Producer event ID" && detail.Value == producerId);
                Assert.Contains(details, detail => detail.Value.Contains(firstOperand, StringComparison.Ordinal) &&
                    detail.Value.Contains(secondOperand, StringComparison.Ordinal));
                if (subrule is not null)
                    Assert.Contains(details, detail => detail.Label == "Subrule" && detail.Value == subrule.ToString());
                Assert.DoesNotContain(workspace.Inspector.Facts, detail => detail.Value.Contains(firstOperand, StringComparison.Ordinal));
            }
            finally { window.Close(); }
        }, Deadline);

        static IEnumerable<TraceStep> Steps(TraceStep step) => new[] { step }.Concat(step.Children.SelectMany(Steps));
    }

    [Fact]
    public void ContradictoryNamesShowNoFactsAndSayWhy()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTexts(configure: (fake, _) => fake.OnInspect((request, _) =>
                Task.FromResult(CommandOutcome<InspectResponse>.Success(new InspectResponse(request.Subject, InspectorResolution.Contradictory)
                {
                    Facts = InspectorSection<ObjectFacts>.Not(InspectorSectionStatus.Absent,
                        "The names given belong to different FieldWorks objects, so Motif shows neither."),
                }))));
            try
            {
                Click(window, CardChip(window, "a-"));
                await workspace.Inspector.Loading;
                Settle(window);

                Assert.Empty(workspace.Inspector.Facts);
                Assert.Equal("The names given belong to different FieldWorks objects, so Motif shows neither.",
                    workspace.Inspector.FactsNote);
                Assert.False(workspace.Inspector.WhatItIsFromBaseline);
                Assert.DoesNotContain(InspectorPanel(window).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(),
                    dot => dot.Classes.Contains("freshDot") && dot.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    private static GrammarWarning NamingWarning() => new(
        GrammarDiagnosticLevel.Warning, "Allomorph can't be split into phonemes",
        [new GrammarWarningPart("a-", GrammarWarningPartRole.Object)
        {
            Reach = new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [SampleAllomorph("a-")] },
        }],
        [], "Allomorph can't be split into phonemes")
    {
        Group = "Allomorph can't be split into phonemes",
        Description = "Its first letter matches no phoneme.",
    };

    private static string SampleAllomorph(string form) =>
        PageScreenshots.SampleMorph(form).AllomorphId!;

    [Theory]
    [InlineData(InspectorResolution.NoBaseline)]
    [InlineData(InspectorResolution.NotInBaseline)]
    public void AbsentFactsNeverWearACurrentFreshnessMark(InspectorResolution resolution)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTexts(configure: (fake, _) => fake.OnInspect((request, _) =>
                Task.FromResult(CommandOutcome<InspectResponse>.Success(new InspectResponse(request.Subject, resolution)
                {
                    Facts = InspectorSection<ObjectFacts>.Not(InspectorSectionStatus.Absent, "No facts to read."),
                }))));
            try
            {
                Click(window, CardChip(window, "a-"));
                await workspace.Inspector.Loading;
                Settle(window);

                Assert.DoesNotContain(InspectorPanel(window).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(),
                    dot => dot.Classes.Contains("freshDot") && dot.IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, Deadline);
    }

    [Theory]
    [InlineData("lex_entry", false, "Lexical entry")]
    [InlineData("morph_rule", false, "Inflectional affix")]
    [InlineData(null, false, "Inflectional affix")]
    [InlineData(null, true, "Inflectional affix")]
    public void WhatItIsNamesALexicalEntryAndClaimsAnAllomorphOnlyWhenOneWasAskedFor(
        string? timingKind, bool asksAllomorph, string kind)
    {
        var fake = new FakeCommandClient();
        fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(Busy(request.Subject) with
        {
            Facts = InspectorSection<ObjectFacts>.Of(Busy(request.Subject).Facts.Value! with
            {
                Entry = new ObjectFactsEntry("entry-a", "headword") { MorphType = "prefix" },
                Allomorphs = [new ObjectFactsAllomorph("a-form", "a-") { IsAsked = asksAllomorph }],
            }),
        })));
        var context = ContextFor(fake);
        var inspector = new InspectorViewModel(context, _ => "Timing");
        var subject = timingKind is null ? InspectorSubject.Morpheme(asksAllomorph ? "a-form" : null, "msa-a", "a-")!
            : InspectorSubject.Rule(new TraceTimingKey(timingKind, "entry-a"), "a-", "authored");

        context.OpenInspector(subject);
        Assert.True(inspector.Loading.IsCompletedSuccessfully);

        Assert.Contains(kind, inspector.Subtitle, StringComparison.Ordinal);
        Assert.Equal(asksAllomorph, inspector.WhatItIs.Contains("An allomorph of headword"));
    }

    [Fact]
    public async Task NavigationCancelsSupersededReadsAndTheirLateAnswersNeverReplaceTheCurrentCrumb()
    {
        var fake = new FakeCommandClient();
        var reads = new List<(InspectorSubject Subject, CancellationToken Token, TaskCompletionSource<CommandOutcome<InspectResponse>> Answer)>();
        fake.OnInspect((request, token) =>
        {
            var answer = new TaskCompletionSource<CommandOutcome<InspectResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            reads.Add((request.Subject, token, answer));
            return answer.Task;
        });
        var context = ContextFor(fake);
        var inspector = new InspectorViewModel(context, _ => "Texts");
        var first = InspectorSubject.Morpheme("a-form", "msa-a", "a-")!;
        var second = first with { AllomorphId = "yu-form", Label = "yu-" };
        var tasks = new List<Task>();
        void Track() => tasks.Add(inspector.Loading);

        context.OpenInspector(first);
        Track();
        inspector.Push(second);
        Track();
        Assert.True(reads[0].Token.IsCancellationRequested);
        inspector.Back();
        Track();
        Assert.True(reads[1].Token.IsCancellationRequested);
        inspector.Push(second);
        Track();
        Assert.True(reads[2].Token.IsCancellationRequested);
        inspector.GoToCommand.Execute(inspector.Crumbs[1]);
        Track();
        Assert.True(reads[3].Token.IsCancellationRequested);

        reads[4].Answer.SetResult(CommandOutcome<InspectResponse>.Success(Busy(first)));
        await tasks[4];
        reads[0].Answer.SetException(new OperationCanceledException(reads[0].Token));
        reads[1].Answer.SetResult(CommandOutcome<InspectResponse>.Success(Busy(second)));
        reads[2].Answer.SetException(new OperationCanceledException(reads[2].Token));
        reads[3].Answer.SetResult(CommandOutcome<InspectResponse>.Success(Busy(second)));
        await Task.WhenAll(tasks);
        Assert.Equal("a-", inspector.Title);

        inspector.Push(second);
        var closedLoad = inspector.Loading;
        inspector.Close();
        Assert.True(reads[5].Token.IsCancellationRequested);
        reads[5].Answer.SetException(new OperationCanceledException(reads[5].Token));
        await closedLoad;
        Assert.False(inspector.IsOpen);
        Assert.False(inspector.IsLoading);
        Assert.Empty(inspector.Crumbs);
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("structural", "structural")]
    [InlineData("synthetic", "synthetic")]
    [InlineData("authored", "authored")]
    public void ATraceMorphCarriesItsRecordedIdentityQualityIntoTheInspector(string? recorded, string expected)
    {
        var morph = new TraceMorph(null, "a-", null, null, null, null, null, null, null, null)
        {
            FormId = "form-id",
            IdentityQuality = recorded,
        };

        Assert.Equal(expected, new TraceMorphViewModel(morph, allowLiveLink: false).InspectSubject!.IdentityQuality);
    }

    [Fact]
    public void AnInspectorIdentityIsUnknownUntilItsSourceSaysOtherwise()
    {
        Assert.Equal("unknown", new InspectorSubject(InspectorSubjectKind.Feature).IdentityQuality);
        Assert.Equal("unknown", InspectorSubject.Morpheme("form-id", null)!.IdentityQuality);
        Assert.Equal("unknown", InspectorSubject.Rule(new TraceTimingKey("phon_rule", "rule-id")).IdentityQuality);
    }

    private static WorkspaceContext ContextFor(FakeCommandClient fake)
    {
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake), fake,
            new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(fake)) { ProjectPath = "sample.fwdata" };
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(DragDropEffects.None);
    }
}
