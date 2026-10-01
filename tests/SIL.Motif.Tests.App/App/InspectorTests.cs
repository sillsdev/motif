using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
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
    public void TryAWordsChipAndRuleRowAndTimingsRuleRowOpenTheSameInspector()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = default(FakeCommandClient)!;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (client, assessment) =>
            {
                fake = client;
                OverviewTimingScreenshots.ReadOverviewAndTiming(client, assessment);
                client.TraceWordCompletesWith(PageScreenshots.TraceWithIdentities());
            });
            try
            {
                window.Width = 1240;
                window.Height = 1500;
                workspace.Context.TryWord("matinlu");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                Settle(window);
                var inspector = InspectorPanel(window);

                workspace.Assess.Trace.ShowDroppedPaths = true;
                Settle(window);
                var chip = TraceChip(window);
                var morph = Assert.IsType<ParserReadingMorphViewModel>(chip.Tag);
                Click(window, chip);
                await workspace.Inspector.Loading;
                Settle(window);
                Assert.NotNull(morph.AllomorphId);
                Assert.Equal(morph.AllomorphId, fake.InspectRequests[^1].Subject.AllomorphId);
                Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);
                Assert.Same(inspector, InspectorPanel(window));
                Assert.True(inspector.IsEffectivelyVisible);

                var rule = Assert.IsType<TryWordRuleRowViewModel>(RuleLinkIn(window, "Best path rules").DataContext);
                Click(window, RuleLinkIn(window, "Best path rules"));
                await workspace.Inspector.Loading;
                Settle(window);
                Assert.Equal(rule.InspectSubject!.TimingKey, fake.InspectRequests[^1].Subject.TimingKey);
                Assert.Equal(InspectorSubjectKind.Rule, fake.InspectRequests[^1].Subject.Kind);
                Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);

                workspace.CurrentPage = WorkspacePage.Timing;
                await Until(window, () => HasRuleLink(window, "Timing by rule"));
                Click(window, RuleLinkIn(window, "Timing by rule"));
                await workspace.Inspector.Loading;
                Settle(window);
                var asked = fake.InspectRequests[^1].Subject;
                Assert.Equal(InspectorSubjectKind.Rule, asked.Kind);
                Assert.Equal(new TraceTimingKey("morph_rule", "Subject agreement"), asked.TimingKey);
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
                    ["What it is", "Your words that use it", "Rules that ran on it", "Grammar warnings about it", "In FieldWorks"],
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
                uses.ShowAllCommand.Execute(null);
                Assert.Equal(5, uses.Shown.Count);

                Assert.Equal("Allomorph can't be split into phonemes", Assert.Single(workspace.Inspector.Warnings).Title);
                var facts = sections[^1].GetVisualDescendants().OfType<HyperlinkButton>()
                    .Where(link => link is not InspectLink && link.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(facts);
                Assert.All(facts, link => Assert.EndsWith(" ↗", Assert.IsType<string>(link.Content)));
                Assert.Contains(facts, link => (string)link.Content! == "Lexicon Edit ↗");
                Assert.Contains(facts, link => (string)link.Content! == "Category Edit ↗");
                Assert.All(facts, link => Assert.Contains("revealControl", link.Classes));
                Assert.All(facts, link => Assert.True(link.Focusable, "A link hidden until hover is still reachable by keyboard."));
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

    internal static Border CardChip(Window window, string form) =>
        AnalyzeTextsLayoutTests.OpenCard(window).GetVisualDescendants().OfType<Border>().First(border =>
            border.Classes.Contains("morph") && border.IsEffectivelyVisible &&
            border.Tag is ParserReadingMorphViewModel morph && morph.Form == form);

    private static IReadOnlyList<Button> Crumbs(Window window) =>
        InspectorPanel(window).GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("crumb")).ToArray();

    private static Border TraceChip(Window window) =>
        window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(items => AutomationProperties.GetName(items) == "Attempts that got furthest" && items.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("inspectable") && border.IsEffectivelyVisible);

    private static Button RuleLinkIn(Window window, string list) =>
        window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(items => AutomationProperties.GetName(items) == list && items.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("inspectLink") && button.IsEffectivelyVisible);

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
                Assert.False(inspector.HasWarnings);
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
}
