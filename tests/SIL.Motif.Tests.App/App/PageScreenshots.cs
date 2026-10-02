using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Runs only when <c>MOTIF_SCREENSHOTS</c> names a folder, so the ordinary suite never writes images.</summary>
public sealed class ScreenshotFactAttribute : FactAttribute
{
    public const string FolderVariable = "MOTIF_SCREENSHOTS";

    public ScreenshotFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FolderVariable)))
            Skip = $"Set {FolderVariable} to a folder to capture every page as images.";
    }
}

/// <summary>
/// Opens the real window over realistic data, then saves every page and Texts tab as an image, at a
/// collapsed-sidebar and the default width, in the light and the dark theme. The window renders with Skia and the
/// system's fonts, as the app does, so these show what a person would see, not a stub's layout.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageScreenshots
{
    private const string ProjectPath = @"C:\Users\linguist\FieldWorks\Projects\Sample\Sample.fwdata";
    private static readonly Guid Story = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Letter = Guid.Parse("11111111-0000-0000-0000-000000000002");

    [ScreenshotFact]
    public void CaptureEveryPage()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData();
            try
            {
                var overview = workspace.PageModel<OverviewPageModel>().Overview;
                Assert.NotNull(overview);
                OverviewPageWordsTests.AssertCaptureStopCounts(overview, "every page");
                Assert.Equal(9, overview.SelectionWordCount);

                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var width in new[] { 1040, 1240 })
                    {
                        window.Width = width;
                        window.Height = 780;
                        foreach (var (name, page, tab) in Views())
                        {
                            workspace.PageModel<TextsPageModel>().Tab = tab;
                            // Choosing a word on Texts primes Try a Word afresh, so the trace is run again here.
                            if (page == WorkspacePage.TryAWord) await TryTheSampleWord(workspace);
                            workspace.CurrentPage = page;
                            AssertSceneHasExpectedErrorState(window, workspace, name);
                            Save(window, Path.Combine(folder, $"{name}-{width}-{theme}.png"));
                            if (page != WorkspacePage.TryAWord) continue;
                            window.Height = 1500;
                            AssertSceneHasExpectedErrorState(window, workspace, $"{name} tall");
                            Save(window, Path.Combine(folder, $"{name}-{width}-{theme}-tall.png"));
                            foreach (var height in new[] { 780, 1500 })
                            {
                                workspace.Assess.Trace.IsExpert = true;
                                window.Height = height;
                                Save(window, Path.Combine(folder, $"{name}-expert-{width}-{theme}-{height}.png"));
                            }
                            workspace.Assess.Trace.IsExpert = false;
                            window.Height = 780;
                        }
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureOverviewEvidenceStatesAtBothWidthsAndThemes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var states = new List<string>();

        foreach (var state in new[] { "populated", "empty", "stale" })
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await OpenOverSampleData(parse: state != "empty", configure: (fake, _) =>
                {
                    var overview = state == "empty" ? EmptyOverview() : OverviewFor(state);
                    OverviewPageWordsTests.AssertCaptureStopCounts(overview, $"overview {state}");
                    fake.OverviewCompletesWith(overview);
                    if (state == "stale")
                    {
                        var saved = DateTimeOffset.UtcNow;
                        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token(), saved.AddHours(-2), false)
                        {
                            ProjectLastWriteUtc = saved,
                        });
                    }
                });
                try
                {
                    workspace.CurrentPage = WorkspacePage.Overview;
                    foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                    {
                        Application.Current!.RequestedThemeVariant = variant;
                        foreach (var width in new[] { 1040, 1240 })
                        {
                            window.Width = width;
                            window.Height = 780;
                            AssertSceneHasExpectedErrorState(window, workspace, $"overview {state}");
                            if (state == "empty")
                            {
                                AssertParsePromptUsesPageAction(window);
                                var handoff = Assert.Single(window.GetVisualDescendants().OfType<Border>(),
                                    border => border.Classes.Contains("overviewHandoff"));
                                Assert.False(handoff.IsEffectivelyVisible);
                            }
                            if (state == "stale")
                            {
                                var renderedText = window.GetVisualDescendants().OfType<CopyableTextBlock>()
                                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
                                Assert.Contains(renderedText, text => text?.Contains("Numbers: Baseline", StringComparison.Ordinal) == true);
                                Assert.Contains(renderedText, text => text?.Contains("saved later", StringComparison.Ordinal) == true);
                                Assert.DoesNotContain(renderedText, text => text?.Contains(
                                    "FieldWorks has changed since the Baseline behind these numbers", StringComparison.Ordinal) == true);
                            }
                            var file = $"overview-{state}-{width}-{theme}.png";
                            Save(window, Path.Combine(folder, file));
                            states.Add($"{file}\tOverview {state}, {width} px, {theme} theme.");
                        }
                    }
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                    window.Close();
                }
            }, TimeSpan.FromMinutes(3));
        }

        File.WriteAllLines(Path.Combine(folder, "overview-states.txt"), states);
    }

    [ScreenshotFact]
    public void CaptureEmptyAnalyzeTextsAtBothWidthsAndThemes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData(parse: false);
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                workspace.CurrentPage = WorkspacePage.Texts;
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                        Application.Current!.RequestedThemeVariant = variant;
                        foreach (var width in new[] { 1040, 1240 })
                        {
                            window.Width = width;
                            window.Height = 780;
                            Settle(window);
                            AssertParsePromptUsesPageAction(window);
                            Save(window, Path.Combine(folder, $"09-empty-parse-analyze-{width}-{theme}.png"));
                        }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static void AssertParsePromptUsesPageAction(MainWindow window)
    {
        var visibleButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible).ToArray();
        Assert.Single(visibleButtons, button => AutomationProperties.GetName(button) == "Choose what to parse");
        Assert.Single(visibleButtons, button => AutomationProperties.GetAutomationId(button) == "motif-refresh-project");
    }

    private static OverviewResponse OverviewFor(string state) => state == "stale"
        ? SampleEvidence.Overview(Assessment()) with
        {
            IsStale = true,
            LastFieldWorksSaveUtc = DateTimeOffset.UtcNow,
        }
        : SampleEvidence.Overview(Assessment());

    private static OverviewResponse EmptyOverview()
    {
        return SampleEvidence.Overview(Assessment()) with
        {
            AssessmentId = null, AssessedUtc = null, AssessmentElapsedSeconds = null,
            TextCoverage = new OverviewTextCoverage(0, 0, 0, 0, 9, 0),
            Accuracy = new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            Timing = new OverviewTiming(null, null, [], 0), LookFirst = OverviewLookFirst.Empty,
        };
    }

    /// <summary>Every page, and every tab of the Texts page, with the file name each is saved under.</summary>
    internal static IEnumerable<(string Name, WorkspacePage Page, TextsTab Tab)> Views() =>
    [
        ("1-overview", WorkspacePage.Overview, TextsTab.Matrix),
        ("2a-texts-matrix", WorkspacePage.Texts, TextsTab.Matrix),
        ("2b-texts-analyze", WorkspacePage.Texts, TextsTab.AnalyzeTexts),
        ("2c-texts-lists", WorkspacePage.Texts, TextsTab.Lists),
        ("3-try-a-word", WorkspacePage.TryAWord, TextsTab.Matrix),
        ("4-timing", WorkspacePage.Timing, TextsTab.Matrix),
        ("5-warnings", WorkspacePage.Warnings, TextsTab.Matrix),
        ("6-review", WorkspacePage.Review, TextsTab.Matrix),
        ("7-ai-handoff", WorkspacePage.AiHandoff, TextsTab.Matrix),
    ];

    [Fact]
    public void ScreenshotTraceFixtureIsFieldWorksSnapshotForAnUnparsedWord()
    {
        using var document = JsonDocument.Parse(TraceFixture());
        var root = document.RootElement;
        var grammar = root.GetProperty("provenance").GetProperty("grammar");
        Assert.Equal("pangloss.trace-details.v3", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("synthetic", root.GetProperty("fixture").GetProperty("kind").GetString());
        var failed = Descendants(root.GetProperty("trace"))
            .Single(node => node.TryGetProperty("type", out var type) && type.GetString() == "Failed");
        var morphs = failed.GetProperty("attemptedMorphs").EnumerateArray().ToArray();
        var morph = morphs.Single(item => item.GetProperty("form").GetProperty("text").GetString() == "ja-");
        var identity = morph.GetProperty("identity");
        var trace = WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!;
        var stopped = Assert.Single(trace.Reading.StopGroups);
        var ja = trace.Reading.Refs.Single(reference => reference.Label == "ja-");

        Assert.Equal("hawajafika", root.GetProperty("word").GetString());
        Assert.Equal("snapshot", grammar.GetProperty("sourceKind").GetString());
        Assert.False(grammar.TryGetProperty("grammarHash", out _));
        Assert.Empty(root.GetProperty("result").GetProperty("analyses").EnumerateArray());
        Assert.Equal(["ha-", "wa-", "ja-"], morphs.Select(item => item.GetProperty("form").GetProperty("text").GetString()));
        Assert.Equal("RequiredSyntacticFeatureStruct", failed.GetProperty("failureReason").GetString());
        Assert.Null(stopped.Rule);
        Assert.Equal("RequiredSyntacticFeatureStruct", stopped.ReasonCode);
        Assert.Equal(1, stopped.Count);
        Assert.Equal("NEG.PERF", ja.Gloss);
        Assert.Equal("authored", ja.IdentityQuality);
        Assert.Equal("ja-", morph.GetProperty("form").GetProperty("text").GetString());
        Assert.Equal("NEG.PERF", morph.GetProperty("gloss").GetProperty("text").GetString());
        Assert.Equal("authored", identity.GetProperty("quality").GetString());
        Assert.True(Guid.TryParse(identity.GetProperty("formId").GetString(), out _));
        Assert.True(Guid.TryParse(identity.GetProperty("msaId").GetString(), out _));
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement element)
    {
        yield return element;
        if (element.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var descendant in Descendants(property.Value)) yield return descendant;
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                    foreach (var descendant in Descendants(item)) yield return descendant;
            }
        }
    }

    [Fact]
    public void ScreenshotAssessmentHasProjectStandingsAndTextOccurrences()
    {
        var assessment = Assessment();

        Assert.All(assessment.Words, word =>
        {
            Assert.False(string.IsNullOrWhiteSpace(word.ProjectStanding));
            Assert.True(word.OccurrenceCount > 0);
        });
    }

    [Fact]
    public void ScreenshotMatrixKeepsItsCellsAndItsApprovedWordsShareTheirAffixes()
    {
        var table = new AssessWordsViewModel();
        table.Load(Assessment().Words);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        var builtElse = compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoMatch);

        Assert.Equal(5, builtElse.Count);
        compare.Toggle(builtElse, additive: false);

        Assert.Equal(["a- 3SG in 2", "-a FV in 2"], compare.Shared.Select(item => $"{item.Form} {item.Gloss} {item.CountText}"));
        Assert.Equal([3, 4], compare.Words.Single(word => word.Word == "alikula").WordRow.Row.DifferingPositions);
        Assert.All(compare.Words.Where(word => word.Word != "alikula"), word => Assert.Single(word.WordRow.Row.DifferingPositions));
    }

    [Fact]
    public void ScreenshotTextWordsCountTheirOccurrences()
    {
        var words = TextWords();

        Assert.True(words.OccurrenceCount > 0);
        Assert.Equal(words.Words.Sum(word => word.Occurrences.Count), words.OccurrenceCount);
    }

    [Fact]
    public void DefaultTimingSceneShowsTheSamplesRecordedTimes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData();
            try
            {
                foreach (var (_, page, tab) in Views())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    if (page == WorkspacePage.TryAWord) await TryTheSampleWord(workspace);
                    workspace.CurrentPage = page;
                    if (page == WorkspacePage.Timing) break;
                }
                Assert.Equal(WorkspacePage.Timing, workspace.CurrentPage);
                Settle(window);
                var timing = workspace.PageModel<TimingPageModel>();
                Assert.False(timing.ShowNoTimingRecorded);
                Assert.Equal("all", timing.WordSet);
                Assert.NotNull(timing.KindTiming);
                Assert.Equal(9, timing.KindTiming.WordCount);
                Assert.Equal(Assessment().Words.Select(word => word.Word).Order(),
                    timing.KindTiming.Words.Select(word => word.Word).Order());
                Assert.Equal("795 ms", timing.HeadlineTotal);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SampleOverviewAndTimingDescribeTheSameAssessment()
    {
        var fake = new FakeCommandClient();
        var assessment = Assessment();
        OverviewTimingScreenshots.ReadOverviewAndTiming(fake, assessment);
        var overview = (await fake.OverviewAsync(new SIL.Motif.Contract.Requests.OverviewRequest(ProjectPath), default)).Value!;
        var timing = (await fake.TimingAsync(new SIL.Motif.Contract.Requests.TimingRequest(ProjectPath, WordSet: "all", By: "kind"), default)).Value!;
        Assert.Equal(assessment.Words.Count, overview.SelectionWordCount);
        Assert.Equal(assessment.Words.Sum(word => word.ElapsedMs), overview.AssessmentElapsedSeconds * 1000);
        Assert.Equal(assessment.Words.Select(word => word.Word).Order(), timing.Words.Select(word => word.Word).Order());
        Assert.Equal(overview.Timing.StepLimitedWordCount, overview.LookFirst.StepLimitedWords.Count);
        Assert.Equal(overview.Accuracy.ApprovedWordsNoParse, overview.LookFirst.ApprovedLostWords.Count);
        Assert.Equal(overview.Timing.Kinds.Select(row => row.SelfMs), timing.Aggregates.Select(row => row.SelfMs));
    }

    [Fact]
    public void SampleTraceShowsTheSameWordsEarlierAssessmentTiming()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData();
            try
            {
                await TryTheSampleWord(workspace);
                var page = workspace.PageModel<TryWordPageModel>();
                Assert.True(page.HasEarlierTiming);
                Assert.Contains("hawajafika took 48 ms", page.EarlierTimingSource);
                Assert.Equal("Share of 48 ms", page.EarlierShareHeader);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SampleTraceMorphemesShareTheAssessmentsStoredIdentities()
    {
        var trace = WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!;
        foreach (var morph in Assert.Single(trace.Reading.Attempts).Morphs)
        {
            var subject = InspectorSubject.Morpheme(morph.AllomorphId, morph.GrammaticalInfoId, morph.Form, morph.Gloss)!;
            var inspection = SampleInspection(subject);
            Assert.Contains(inspection.Uses.Value!.Words, word => word.Row.Word == "hawajafika");
        }
    }

    [Fact]
    public void SampleOpinionsAndComparisonsAgreeInEveryTextsView()
    {
        var assessment = Assessment();
        var texts = TextWords();
        foreach (var word in texts.Words)
        {
            var result = assessment.Words.Single(row => row.Word == word.Form);
            Assert.Equal(result.ProjectStanding, WordProjectStatuses.StandingOf(word));
            foreach (var token in texts.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
                         .Where(token => token.Form == word.Form))
            {
                var inText = new ResultsTokenViewModel("Sample", 1, token, result, new TextWordRowViewModel(word));
                var row = WordRowProjection.Of(result);
                Assert.Equal(row.Comparison!.MeaningCode, inText.Comparison.MeaningCode);
                Assert.Equal(row.Outcome, inText.Comparison.Outcome);
                Assert.Equal(row.Opinion, WordProjectStatuses.StandingOf(word));
                Assert.Equal(row.FieldWorksMorphemes.Select(morph => morph.Form),
                    inText.PrimaryFieldWorksMorphs.Select(morph => morph.Form));
            }
        }
    }

    [Fact]
    public void SampleStatisticsUseTheAssessmentsRecordedTimes()
    {
        foreach (var result in Assessment().Words)
        {
            var recorded = StatisticsRows().Single(row => row.GetProperty("form").GetString() == result.Word);
            Assert.Equal(result.ElapsedMs * 1_000_000L, recorded.GetProperty("elapsed_ns").GetInt64());
        }
    }

    [Fact]
    public void SampleStatisticsCountOnlyRecordedReadings()
    {
        foreach (var word in Assessment().Words)
        {
            var recorded = StatisticsRows().Single(row => row.GetProperty("form").GetString() == word.Word);
            Assert.Equal(word.Readings!.Count, recorded.GetProperty("passes").GetInt32());
            Assert.Equal(word.Morphology!.Analyses.Count, recorded.GetProperty("passes").GetInt32());
        }
        Assert.All(StatisticsRows().Where(row => row.GetProperty("form").GetString() is "hawajafika" or "mwalimu"),
            row => Assert.Equal(0, row.GetProperty("passes").GetInt32()));
    }

    [Fact]
    public async Task SampleStatisticsHeadlineCountsOneWordWithSeveralReadings()
    {
        var fake = new FakeCommandClient();
        fake.StatsCompletesWith(new StatsCommandResponse("assessment/one", ProjectPath, "cache", null, StatisticsRows()));
        var statistics = new StatisticsViewModel(fake) { ProjectPath = ProjectPath, AssessmentId = "assessment/one" };
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, statistics.SeveralReadingsCount);
        Assert.Equal("1 word has more than one reading", statistics.PassesHeadline);
    }

    [Fact]
    public void SampleInspectorReadsEachRulesOwnRecordedTimers()
    {
        var timing = SampleEvidence.Timing(Assessment(), "rule");
        foreach (var rule in timing.Aggregates.Take(3))
        {
            var key = new TraceTimingKey(rule.Kind, rule.Key) { IdentityQuality = rule.IdentityQuality, Scope = rule.Scope };
            var inspection = SampleInspection(InspectorSubject.Rule(key, rule.Name, rule.IdentityQuality));
            var ran = inspection.RanIn.Value!;
            Assert.Equal(rule.WordsTouched, ran.Words.Count);
            Assert.Equal(rule.SelfMs, ran.Words.Sum(word => word.ElapsedNs) / 1_000_000d);
            Assert.Equal(rule.Calls, ran.Words.Sum(word => word.Calls));
        }
    }

    [Fact]
    public void SampleBeforeParsingKeepsItsProjectCountsAndWarnings()
    {
        var empty = EmptyOverview();
        var parsed = SampleEvidence.Overview(Assessment());
        Assert.Null(empty.AssessmentId);
        Assert.Null(empty.AssessmentElapsedSeconds);
        Assert.Equal((parsed.WordformCount, parsed.RuleCount, parsed.LexemeCount),
            (empty.WordformCount, empty.RuleCount, empty.LexemeCount));
        Assert.Equal(GrammarFindings().Count, empty.Warnings!.Count);
        Assert.Equal(TextWords().Words.Count, empty.SelectionWordCount);
    }

    [Fact]
    public void SampleInspectorHasNoTimeForAnUnusedAllomorph()
    {
        var subject = InspectorSubject.Morpheme(Id("w-|allomorph", 9), SampleMorph("wa-").GrammaticalInfoId, "w-", "3PL")!;
        var inspection = SampleInspection(subject);
        Assert.Empty(inspection.Uses.Value!.Words);
        Assert.Empty(inspection.RanIn.Value!.Words);
    }

    [Fact]
    public void SampleTryWordUsesTheSharedApprovedAnalysisAndNamesItsGrammar()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData();
            try
            {
                await TryTheSampleWord(workspace);
                var page = workspace.PageModel<TryWordPageModel>();
                Assert.Equal("Approved", Assert.Single(page.FieldWorksAnalyses).OpinionLabel);
                Assert.Contains("ha- + wa- + ja- + fik + -a", page.FieldWorksAnalyses[0].Text);
                Assert.Equal(workspace.Context.Evidence.Assessment!.Assessment.Baseline.Token,
                    page.Trace.Result!.HostCapture!.Baseline!.Token);
                Assert.Null(page.Trace.Result.ParserElapsedMs);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(15));
    }

    private static async Task TryTheSampleWord(WorkspaceShellViewModel workspace)
    {
        workspace.Context.TryWord("hawajafika");
        await workspace.Assess.Trace.TryCommand.ExecutionTask!;
    }

    internal static void AssertSceneHasExpectedErrorState(MainWindow window, WorkspaceShellViewModel workspace,
        string scene, string? expectedCode = null)
    {
        Settle(window);
        var codes = window.GetVisualDescendants().OfType<RefusalBlock>()
            .Where(block => block.IsEffectivelyVisible)
            .Select(block => (block.DataContext as WindowRefusal)?.Code ?? "unbound refusal")
            .ToList();
        if (workspace.CurrentPage == WorkspacePage.Overview &&
            workspace.PageModel<OverviewPageModel>().OverviewRefusal is { } overviewRefusal)
            codes.Add(overviewRefusal.Code);

        if (expectedCode is null)
            Assert.True(codes.Count == 0, $"Scene '{scene}' displays error state(s): {string.Join(", ", codes)}.");
        else
            Assert.Equal([expectedCode], codes);
    }

    internal static void Save(MainWindow window, string path)
    {
        Settle(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets bindings, layout and a render pass catch up with the last change to the window.</summary>
    internal static void Settle(Avalonia.Controls.Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>
    /// Opens the window over the sample project; without <paramref name="parse"/> it stops before the first parse,
    /// and with <paramref name="leaveSetupOpen"/> it stops with first-run setup still showing.
    /// </summary>
    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenOverSampleData(
        bool parse = true, Action<FakeCommandClient, AssessCommandResponse>? configure = null, bool leaveSetupOpen = false)
    {
        var fake = new FakeCommandClient();
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, DateTimeOffset.UtcNow)]);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token(), DateTimeOffset.UtcNow.AddHours(-2), false));
        fake.ProjectHistoryIs(new ProjectHistoryResponse(
        [
            new ProjectHistoryEntry(DateTimeOffset.Now.AddMinutes(-20), ProjectHistoryKind.Assessment, "9 words · 7 parsed · 5 built something else"),
            new ProjectHistoryEntry(DateTimeOffset.Now.AddHours(-2), ProjectHistoryKind.Baseline, "Baseline captured · 862 lexemes · 2 texts"),
        ]));
        fake.StoredGrammarCheckIs(new GrammarCheckResponse(GrammarFindings(), HasBaseline: true));
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse(GrammarFindings(), HasBaseline: true));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(Story, "Hadithi ya sungura"), new TextChoiceSummary(Letter, "Barua kwa mwalimu")], HasBaseline: true));
        fake.ListTextWordsCompletesWith(TextWords());
        fake.AssessCompletesWith(Assessment());
        fake.StatsCompletesWith(new StatsCommandResponse("assessment/one", ProjectPath, "cache", null, StatisticsRows()));
        fake.HandoffCompletesWith(new HandoffCommandResponse(@"C:\Users\linguist\Documents\Motif Handoffs\Sample 1140",
            Capture(), new SelectionProjection([], []),
            ["handoff.md", "parse-results.json", "texts.json", "grammar.json", "read_results.py"], ["assessment/one"])
        {
            InvocationId = "assessment/one",
            HandoffMarkdown = SampleHandoffMarkdown,
        });

        // Try a Word traces through the page's own path: a result set directly is wiped when a word is chosen.
        fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceFixture()).Value! with
        {
            ParserElapsedMs = null,
            HostCapture = new TraceHostCapture(Token().ProjectIdentity, null, null, Token().BundleDigest, null, null, [])
            {
                Baseline = new TraceBaselineSource(Token(), DateTimeOffset.Parse("2026-09-22T09:48:00Z"),
                    DateTimeOffset.Parse(Token().CapturedUtc), "Sample grammar capture"),
            },
        });
        fake.WordContextHandler = (request, _) =>
        {
            var word = Assessment().Words.FirstOrDefault(word => word.Word == request.Word);
            return Task.FromResult(CommandOutcome<WordContextResponse>.Success(new(request.Word, true)
            {
                IsInFieldWorks = word is not null,
                Baseline = Token(), SourceLastWriteUtc = DateTimeOffset.Parse("2026-09-22T09:48:00Z"),
                Analyses = word?.StoredAnalyses ?? [],
                ExpectedAnalysis = word?.StoredAnalyses.FirstOrDefault(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved),
            }));
        };
        fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(SampleInspection(request.Subject))));
        OverviewTimingScreenshots.ReadOverviewAndTiming(fake, Assessment());
        if (!parse) fake.OverviewCompletesWith(EmptyOverview());
        configure?.Invoke(fake, Assessment());

        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new Picker()), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection),
            new Folder(), new Drag(),
            fake, techDemoNotice: FirstRunNotice());
        var window = new MainWindow();
        window.Compose(workspace);
        window.Show();

        await workspace.SetProjectAsync(ProjectPath);
        if (leaveSetupOpen) return (workspace, window);
        workspace.Context.Setup?.SkipCommand.Execute(null);
        foreach (var text in selection.Texts) text.IsChecked = true;
        await Task.Yield();
        await workspace.PageModel<TextsPageModel>().Words.ReloadAsync();
        if (!parse) return (workspace, window);
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        workspace.PageModel<TimingPageModel>().Statistics.AssessmentId = "assessment/one";
        await workspace.PageModel<TimingPageModel>().Statistics.LoadCommand.ExecuteAsync(null);
        workspace.Assess.Words.SelectedRow = workspace.Assess.Words.Rows.FirstOrDefault(row => row.Word == "hawajafika");
        workspace.PageModel<TextsPageModel>().ResultsInText.SelectToken(workspace.PageModel<TextsPageModel>().ResultsInText.VisibleLines[0].Tokens[1]);
        await workspace.PageModel<AiHandoffPageModel>().Handoff.RunCommand.ExecuteAsync(null);
        workspace.PageModel<AiHandoffPageModel>().Handoff.LatestAssessmentAt = workspace.PageModel<AiHandoffPageModel>().Handoff.WrittenAt!.Value.AddMinutes(35);
        return (workspace, window);
    }

    private const string SampleHandoffMarkdown =
        "# AI Handoff: Sample\n\n" +
        "These files describe how the grammar of **Sample** parsed 9 words from 2 texts.\n\n" +
        "- `parse-results.json`: every word, whether it parsed, and how long it took.\n" +
        "- `texts.json`: the chosen texts with the analyses the project stores.\n" +
        "- `grammar.json`: the grammar the parser used.\n\n" +
        "Start with *hawajafika*, approved in FieldWorks as ha-wa-ja-fik-a but not parsed.\n";

    private static IReadOnlyList<GrammarWarning> GrammarFindings() => SeededGrammarFindings.All();

    private static readonly IReadOnlyDictionary<(string Form, string Gloss), ParserReadingMorph> TraceMorphology =
        WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!.Reading.Attempts.Single().Morphs
            .ToDictionary(morph => (morph.Form, morph.Gloss));

    private static readonly (string Word, string[] Forms, string[] Glosses)[] Vocabulary =
    [
        ("Sungura", ["sungura"], ["hare"]),
        ("alikula", ["a-", "li-", "kul", "-a"], ["3SG", "PST", "eat", "FV"]),
        ("chakula", ["ch-", "akula"], ["7", "food"]),
        ("hawajafika", ["ha-", "wa-", "ja-", "fik", "-a"], ["NEG", "3PL", "NEG.PERF", "arrive", "FV"]),
        ("watoto", ["wa-", "toto"], ["2", "child"]),
        ("walikula", ["wa-", "li-", "kul", "-a"], ["3PL", "PST", "eat", "FV"]),
        ("mwalimu", ["m-", "walimu"], ["1", "teacher"]),
        ("anapenda", ["a-", "na-", "pend", "-a"], ["3SG", "PRS", "love", "FV"]),
        ("kitabu", ["ki-", "tabu"], ["7", "book"]),
    ];

    private static ParseAnalysis Reading(string word) => RawReading(Resolved(word));

    private static ParseAnalysis RawReading(ParserReading reading) =>
        new([.. reading.Morphs.Select(morph => new ParseMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, null))]);

    private static string Id(string word, int index) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(word + index))).ToString("D");

    private static ParserReading Resolved(string word)
    {
        var item = Vocabulary.Single(entry => entry.Word == word);
        return new ParserReading([.. item.Forms.Select((form, index) =>
        {
            TraceMorphology.TryGetValue((form, item.Glosses[index]), out var traceMorph);
            return new ParserReadingMorph(form, item.Glosses[index], index == item.Forms.Length - 1 ? "v" : "", null, false,
                "silfw://localhost/link?tool=lexiconEdit")
            {
                AllomorphId = traceMorph?.AllomorphId ?? Id("allomorph " + form + item.Glosses[index], 0),
                GrammaticalInfoId = traceMorph?.GrammaticalInfoId ?? Id("grammatical info " + item.Glosses[index], 0),
            };
        })]);
    }

    /// <summary>The first sample morpheme spelled <paramref name="form"/>, with the ids every word that uses it shares.</summary>
    internal static ParserReadingMorph SampleMorph(string form) =>
        Vocabulary.Select(item => item.Word).SelectMany(word => Resolved(word).Morphs).First(morph => morph.Form == form);

    /// <summary>
    /// What the inspector reads for the sample: the words whose analyses use a morpheme, by identity, with the
    /// sample's own FieldWorks facts; or, for a rule, its words from the sample timings.
    /// </summary>
    internal static InspectResponse SampleInspection(InspectorSubject subject)
    {
        var asked = new ObjectUseRef
        {
            AllomorphId = subject.AllomorphId, GrammaticalInfoId = subject.GrammaticalInfoId,
            TimingKind = subject.TimingKey?.Kind, TimingKey = subject.TimingKey?.Key, Label = subject.Label, Gloss = subject.Gloss,
            TimingIdentityQuality = subject.TimingKey?.IdentityQuality ?? "authored", TimingScope = subject.TimingKey?.Scope,
        };
        var assessment = Assessment();
        var words = assessment.Words;
        var facts = SampleFacts(asked);
        asked = ObjectUsesQuery.WithTimingKey(asked, facts)!;
        var timings = SampleEvidence.ObjectTimings;
        var morpheme = subject.Kind == InspectorSubjectKind.Morpheme;
        return new InspectResponse(subject, InspectorResolution.Resolved)
        {
            AssessmentId = "assessment/one",
            TimingKey = asked.TimingKind is null ? null : new TraceTimingKey(asked.TimingKind, asked.TimingKey!),
            Facts = InspectorSection<ObjectFacts>.Of(facts),
            Uses = morpheme ? InspectorSection<ObjectUseWords>.Of(ObjectUsesQuery.UsesOf(words, asked))
                : InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Unsupported, "Only a morpheme is used by words."),
            RanIn = InspectorSection<ObjectUseWords>.Of(ObjectUsesQuery.RanIn(words, timings, asked)),
            Warnings = InspectorSection<IReadOnlyList<GrammarWarning>>.Of(
                InspectQuery.WarningsNaming(GrammarFindings(), subject, asked.TimingKind is null ? null
                    : new TraceTimingKey(asked.TimingKind, asked.TimingKey!))),
        };
    }

    private static ObjectFacts SampleFacts(ObjectUseRef asked)
    {
        TraceFieldWorksTarget Tool(string tool, string name, string id) =>
            new(tool, name, id, $"silfw://localhost/link?tool={tool}&guid={id}");
        if (asked.AllomorphId is null)
            return new ObjectFacts
            {
                Rule = new ObjectFactsRule(asked.TimingKey!, asked.TimingKind == "phon_rule" ? "phonologicalRule" : "affixRule",
                    asked.Label ?? asked.TimingKey!)
                {
                    FieldWorks = asked.TimingKind == "phon_rule"
                        ? Tool("PhonologicalRuleEdit", "Phonological Rules", asked.TimingKey!)
                        : Tool("lexiconEdit", "Lexicon Edit", asked.TimingKey!),
                },
            };
        var form = asked.Label ?? "?";
        var affix = form.StartsWith('-') || form.EndsWith('-');
        var headword = form == "w-" ? "wa-" : form;
        var entry = Id(headword + "|entry", 3);
        return new ObjectFacts
        {
            Entry = new ObjectFactsEntry(entry, headword)
            {
                MorphType = form.EndsWith('-') ? "prefix" : form.StartsWith('-') ? "suffix" : "root",
                FieldWorks = Tool("lexiconEdit", "Lexicon Edit", entry),
            },
            Senses = [new ObjectFactsSense(Id(form + "|sense", 4), "1") { Gloss = asked.Gloss, FieldWorks = Tool("lexiconEdit", "Lexicon Edit", entry) }],
            GrammaticalInfo = new ObjectFactsGrammaticalInfo(asked.GrammaticalInfoId ?? entry, affix ? "inflectionalAffix" : "stem")
            {
                Category = new ObjectFactsNamed(Id("verb", 5), affix || form == "kul" || form == "fik" ? "Verb" : "Noun")
                {
                    FieldWorks = Tool("posEdit", "Category Edit", Id("verb", 5)),
                },
                Slots = affix && form.EndsWith('-')
                    ? [new ObjectFactsSlot(Id("slot", 6), "Subject") { Templates = [new ObjectFactsNamed(Id("template", 7), "Finite verb")],
                        FieldWorks = Tool("posEdit", "Category Edit", Id("verb", 5)) }]
                    : [],
            },
            // Swahili's wa- loses its vowel before a vowel, which gives the breadcrumb a second allomorph to step to.
            TimingKey = affix ? new TraceTimingKey("morph_rule", asked.GrammaticalInfoId ?? entry) : new TraceTimingKey("lex_entry", entry),
            Allomorphs = form is "wa-" or "w-"
                ?
                [
                    new ObjectFactsAllomorph(SampleMorph("wa-").AllomorphId!, "wa-") { IsAsked = form == "wa-" },
                    new ObjectFactsAllomorph(Id("w-|allomorph", 9), "w-")
                    {
                        IsAsked = form == "w-",
                        Environments = [new ObjectFactsEnvironment(Id("env-v", 9), "/ _ [V]")
                            { FieldWorks = Tool("EnvironmentEdit", "Environments", Id("env-v", 9)) }],
                    },
                ]
                : [new ObjectFactsAllomorph(asked.AllomorphId, form) { IsAsked = true }],
        };
    }

    private static readonly (string Form, string Gloss)[] TraceMorphemes = [("tin", "see"), ("ma-", "PST"), ("-lu", "3SG")];

    /// <summary>The Inspector test trace with authored identities added to its recorded morphemes.</summary>
    internal static WordTraceResponse TraceWithIdentities()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json")))!;
        void Walk(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonArray array)
                foreach (var item in array) Walk(item);
            if (node is not System.Text.Json.Nodes.JsonObject element) return;
            if (element["identity"] is System.Text.Json.Nodes.JsonObject identity && identity.ContainsKey("formId"))
            {
                var morpheme = int.Parse(identity["morphemeId"]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                identity["formId"] = Id("trace-allomorph", 10 + morpheme);
                identity["msaId"] = Id("trace-msa", 10 + morpheme);
                identity["quality"] = "authored";
                var (form, gloss) = TraceMorphemes[morpheme % TraceMorphemes.Length];
                element["form"] ??= form;
                element["gloss"] ??= gloss;
            }
            if (element["type"]?.ToString() is { } type && type.StartsWith("MorphologicalRule", StringComparison.Ordinal) &&
                element["source"]?.ToString() is { } source)
                element["sourceIdentity"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["kind"] = "morphRule", ["id"] = Id(source, 8), ["quality"] = "authored",
                };
            foreach (var (_, child) in element.ToArray()) Walk(child);
        }
        Walk(document);
        return WordTraceQuery.LoadDiagnostic(document.ToJsonString()).Value!;
    }

    // Morphemes named by identity, so the Matrix can say what words share; another stem keeps each word's cell.
    private static ParserReading ApprovedInAssessment(string word)
    {
        return new ParserReading([.. Resolved(word).Morphs.Select(morph => morph with
        {
            AllomorphId = morph.Form.Contains('-') ? morph.AllomorphId : Id("stem of " + word + " " + morph.Form + morph.Gloss, 0),
        })])
        {
            StoredAnalysisId = Id(word, 97),
            StoredAnalysisOpinion = ReadingGrade.Approved,
            Identity = StoredIdentity(word, 7),
        };
    }

    private static ApprovedMorphology StoredIdentity(string word, int variant) =>
        new([.. Resolved(word).Morphs.Select(morph => new ApprovedMorph(
            variant == 0 || morph.Form.Contains('-') ? morph.AllomorphId! :
                Id("stem of " + word + " " + morph.Form + morph.Gloss, 0),
            morph.GrammaticalInfoId!, null, [morph.Form]))]);

    // FieldWorks' kul against PanGloss's ku- + l: the sample's one word whose two analyses part inside a morpheme.
    private static ParserReading AlikulaApproved() =>
        new([Piece("a-", "3SG"), Piece("li-", "PST"), Piece("kul", "eat"), Piece("-a", "FV")])
        {
            StoredAnalysisId = Id("alikula", 90),
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };

    // The sample's identities, so alikula's affixes are the ones the other approved words use.
    private static ParserReadingMorph Piece(string form, string gloss) =>
        new(form, gloss, "", null, false, "silfw://localhost/link?tool=lexiconEdit")
        {
            AllomorphId = Id((form.Contains('-') ? "allomorph " : "stem of alikula ") + form + gloss, 0),
            GrammaticalInfoId = Id("grammatical info " + gloss, 0),
        };

    private static ProjectAnalysis Stored(ParserReading reading) => new(
        reading.StoredAnalysisId!, reading.Morphs)
    {
        StoredAnalysisId = reading.StoredAnalysisId,
        StoredAnalysisOpinion = reading.StoredAnalysisOpinion,
        Identity = reading.Identity,
    };

    private static TextWordsResponse TextWords()
    {
        var assessment = Assessment();
        TextToken Token(string form)
        {
            var word = assessment.Words.Single(word => word.Word == form);
            var stored = word.StoredAnalyses.Select(Stored).ToArray();
            return new TextToken(form, form, null, word.ProjectStanding == ProjectStanding.Approved ? "approved" :
                word.ProjectStanding == ProjectStanding.Rejected ? "disapproved" :
                stored.Length > 0 ? "unapproved" : "unanalysed")
            {
                Analysis = stored.FirstOrDefault(), StoredAnalyses = stored,
                WordformId = Guid.Parse(Id("wordform " + form, 0)),
                StoredAnalysisId = stored.FirstOrDefault()?.StoredAnalysisId,
                WordLink = "silfw://localhost/link?tool=Analyses",
            };
        }
        var lines = new[]
        {
            new TextLines(Story, "Hadithi ya sungura",
            [
                new TextLine(1, [Token("Sungura"), Token("alikula"), Token("chakula"), new TextToken(".", null, null, null)]),
                new TextLine(2, [Token("watoto"), Token("hawajafika"), new TextToken(",", null, null, null), Token("mwalimu")]),
            ]),
            new TextLines(Letter, "Barua kwa mwalimu",
            [new TextLine(1, [Token("walikula"), Token("anapenda"), Token("kitabu"), new TextToken(".", null, null, null)])]),
        };
        var words = assessment.Words.Select(word =>
        {
            var stored = word.StoredAnalyses.Select(Stored).ToArray();
            var occurrences = lines.SelectMany(text => text.Lines.SelectMany(line => line.Tokens
                .Where(token => token.Form == word.Word).Select(token => new WordOccurrence(text.TextId, text.Title,
                    line.Number, string.Join(" ", line.Tokens.Select(token => token.Text)), token.Status!, token.Analysis))))
                .ToArray();
            return new TextWord(word.Word, Id("wordform " + word.Word, 0), occurrences,
                stored.Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved).ToArray(),
                stored.Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Disapproved).ToArray(),
                stored.Count(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Candidate)) { Analyses = stored };
        }).ToArray();
        return new TextWordsResponse(words, lines, HasBaseline: true, words.Sum(word => word.Occurrences.Count));
    }

    internal static AssessCommandResponse Assessment()
    {
        AssessmentWordResult Word(string word, string outcome, string[] grades, int elapsed, params ParseAnalysis[] analyses) =>
            new(word, outcome, outcome is "capped", outcome == "capped" ? "Stopped at the step limit" : "Search completed", elapsed, null)
            {
                Morphology = new ParseWordEvidence("v1", 0, word, elapsed, outcome == "capped", false, false, analyses, []),
                Readings = [.. analyses.Select(_ => Resolved(word))],
                ReadingGrades = grades,
                Attempts = elapsed * 7,
                Passes = analyses.Length,
                ProjectStanding = grades.Contains("approved") ? "approved" :
                    grades.Contains("disapproved") ? "rejected" :
                    outcome == "no-analysis" ? "not-present" : "candidate",
                OccurrenceCount = 1,
                TryWordLink = "silfw://localhost/link?tool=Analyses",
                StoredAnalyses = grades.Contains("approved") ? [ApprovedInAssessment(word)] : [],
            };
        var response = new AssessCommandResponse(Capture(), new SelectionProjection([], []), ["assessment/one"], "## 9 words\n\n7 parsed, 1 built nothing, 1 stopped.")
        {
            InvocationId = "assessment/one",
            CompletionSummary = "9 words · 7 parsed · run 12:15",
            Words =
            [
                Word("Sungura", "analysed", ["approved"], 3, Reading("Sungura")),
                Word("alikula", "analysed", ["approved", "no-opinion"], 11, RawReading(new ParserReading([Piece("a-", "3SG"), Piece("li-", "PST"), Piece("ku-", "INF"), Piece("l", "eat"), Piece("-a", "FV")])), Reading("alikula")) with
                {
                    ExpectedAnalysis = AlikulaApproved(),
                    StoredAnalyses = [AlikulaApproved() with { Identity = new ApprovedMorphology([.. AlikulaApproved().Morphs.Select(morph =>
                        new ApprovedMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, [morph.Form]))]) }],
                    Readings = [new ParserReading([Piece("a-", "3SG"), Piece("li-", "PST"), Piece("ku-", "INF"), Piece("l", "eat"),
                        Piece("-a", "FV")]), Resolved("alikula")],
                },
                Word("chakula", "analysed", ["no-opinion"], 6, Reading("chakula")) with { ProjectStanding = ProjectStanding.NotPresent },
                Word("hawajafika", "no-analysis", [], 48) with
                { ProjectStanding = ProjectStanding.Approved, StoredAnalyses = [ApprovedInAssessment("hawajafika")] },
                Word("watoto", "analysed", ["approved"], 4, Reading("watoto")),
                Word("walikula", "analysed", ["disapproved"], 12, Reading("walikula")) with
                { StoredAnalyses = [Resolved("walikula") with
                    { StoredAnalysisId = Id("walikula", 97), StoredAnalysisOpinion = ReadingGrade.Disapproved, Identity = StoredIdentity("walikula", 0) }] },
                Word("mwalimu", "capped", [], 700) with
                { StoredAnalyses = [ApprovedInAssessment("mwalimu") with { StoredAnalysisOpinion = ReadingGrade.Candidate }] },
                Word("anapenda", "analysed", ["approved"], 9, Reading("anapenda")),
                Word("kitabu", "analysed", ["approved"], 2, Reading("kitabu")),
            ],
        };
        return response with
        {
            Words = response.Words.Select(word => word with
            {
                StoredAnalysesAvailable = true,
                ReadingGrades = (word.Morphology?.Analyses ?? []).Select(reading =>
                    word.StoredAnalyses.FirstOrDefault(stored => stored.Identity is not null &&
                        SIL.Motif.Host.Analysis.AnalysisMorphologyMatcher.Matches(reading, stored.Identity))
                        ?.StoredAnalysisOpinion ?? ReadingGrade.NoOpinion).ToArray(),
            }).ToArray(),
        };
    }

    private static IReadOnlyList<JsonElement> StatisticsRows() =>
        Assessment().Words.Select(word => JsonSerializer.SerializeToElement(new
        {
            kind = "word", form = word.Word, attempts = word.Attempts, passes = word.Passes,
            elapsed_ns = word.ElapsedMs * 1_000_000L, capped = word.Outcome == "capped", timed_out = false,
        })).ToArray();

    /// <summary>The sample trace's document, as PanGloss wrote it.</summary>
    internal static string SampleTrace() => TraceFixture();

    private static string TraceFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-hawajafika.json"));

    private static BaselineToken Token() =>
        new("project-1", "sha256:" + new string('a', 64), "1", "2026-09-22T10:00:00Z", "sha256:" + new string('b', 64));

    private static BaselineCaptureResponse Capture() =>
        new(Token(), ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true);

    private sealed class Picker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(ProjectPath);
    }

    private sealed class Folder : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(@"C:\Users\linguist\Documents\Motif Handoffs");
    }

    /// <summary>The tech demo notice as a first run shows it, live, so a capture never draws it disabled.</summary>
    internal static TechDemoNoticeViewModel FirstRunNotice() => new(new NoticeNotYetSeen(), new Launcher());

    private sealed class NoticeNotYetSeen : ITechDemoNoticePreferences
    {
        public bool HasSeenTechDemoNotice => false;

        public void MarkTechDemoNoticeSeen() { }
    }

    private sealed class Launcher : IUriLauncher
    {
        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class Drag : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
