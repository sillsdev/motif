using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
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
                            Save(window, Path.Combine(folder, $"{name}-{width}-{theme}.png"));
                            if (page != WorkspacePage.TryAWord) continue;
                            window.Height = 1500;
                            Save(window, Path.Combine(folder, $"{name}-{width}-{theme}-tall.png"));
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

    private static async Task TryTheSampleWord(WorkspaceShellViewModel workspace)
    {
        workspace.Context.TryWord("matinlu");
        await workspace.Assess.Trace.TryCommand.ExecutionTask!;
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
            new ProjectHistoryEntry(DateTimeOffset.Now.AddMinutes(-20), ProjectHistoryKind.Assessment, "142 words · 118 parsed · 9 differ from stored"),
            new ProjectHistoryEntry(DateTimeOffset.Now.AddHours(-2), ProjectHistoryKind.Baseline, "Baseline captured · 1,318 entries · 3 texts"),
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
            ["handoff.md", "assessment.json", "texts.json", "grammar.json", "parse_grammar_texts_assessment.py"], ["assessment/one"])
        {
            InvocationId = "assessment/one",
        });

        // Try a Word traces through the page's own path: a result set directly is wiped when a word is chosen.
        fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!);
        configure?.Invoke(fake, Assessment());

        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new Picker()), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection),
            new Folder(), new Drag(),
            fake, techDemoNotice: new TechDemoNoticeViewModel(new NoticeNotYetSeen(), new Launcher()));
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

    private static IReadOnlyList<GrammarWarning> GrammarFindings() => SeededGrammarFindings.All();

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

    private static ParseAnalysis Reading(string word, int variant = 0) => new(
        [.. Vocabulary.Single(item => item.Word == word).Forms.Select((_, index) =>
            new ParseMorph(Id(word, index + variant * 10), Id(word, 50 + index + variant * 10), null, null))]);

    private static string Id(string word, int index) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(word + index))).ToString("D");

    private static ParserReading Resolved(string word)
    {
        var item = Vocabulary.Single(entry => entry.Word == word);
        return new ParserReading([.. item.Forms.Select((form, index) =>
            new ParserReadingMorph(form, item.Glosses[index], index == item.Forms.Length - 1 ? "v" : "", null, false,
                "silfw://localhost/link?tool=lexiconEdit"))]);
    }

    private static ProjectAnalysis Stored(string word, int variant = 0)
    {
        var reading = Reading(word, variant);
        var entries = Vocabulary.Single(item => item.Word == word).Forms;
        return new(ProjectAnalysisKey.For(reading), Resolved(word).Morphs)
        {
            StoredAnalysisId = Id(word, 90 + variant),
            StoredAnalysisOpinion = ReadingGrade.Approved,
            Identity = new ApprovedMorphology([.. reading.Morphs.Select((morph, index) =>
                new ApprovedMorph(morph.Form, morph.Msa, morph.InflType, [entries[index]]))]),
        };
    }

    private static TextToken Token(string word, bool stored = true, int variant = 0) =>
        new(word, word, null, stored ? "approved" : "unanalysed")
        {
            Analysis = stored ? Stored(word, variant) : null,
            StoredAnalyses = stored ? [Stored(word, variant)] : [],
            StoredAnalysisId = stored ? Stored(word, variant).StoredAnalysisId : null,
            WordLink = "silfw://localhost/link?tool=Analyses",
        };

    private static TextWordsResponse TextWords()
    {
        var lines = new TextLines(Story, "Hadithi ya sungura",
        [
            new TextLine(1, [Token("Sungura"), Token("alikula"), Token("chakula", stored: false), new TextToken(".", null, null, null)]),
            new TextLine(2, [Token("watoto"), Token("hawajafika"), new TextToken(",", null, null, null), Token("mwalimu", stored: false)]),
            new TextLine(3, [Token("walikula", variant: 1), Token("anapenda"), Token("kitabu"), new TextToken(".", null, null, null)]),
        ]);
        var words = Vocabulary.Select(item => new TextWord(item.Word, null,
            [new WordOccurrence(Story, "Hadithi ya sungura", 1, "Sungura alikula chakula.", "approved", Stored(item.Word))],
            [Stored(item.Word)], [])).ToArray();
        return new TextWordsResponse(words, [lines], HasBaseline: true);
    }

    private static AssessCommandResponse Assessment()
    {
        AssessmentWordResult Word(string word, string outcome, string[] grades, int elapsed, params ParseAnalysis[] analyses) =>
            new(word, outcome, outcome is "capped", outcome == "analysed" ? "Search completed" : "INCOMPLETE — step limit", elapsed, null)
            {
                Morphology = new ParseWordEvidence("v1", 0, word, elapsed, outcome == "capped", false, false, analyses, []),
                Readings = [.. analyses.Select(_ => Resolved(word))],
                ReadingGrades = grades,
                Attempts = elapsed * 7,
                Passes = elapsed,
                TryWordLink = "silfw://localhost/link?tool=Analyses",
            };
        return new AssessCommandResponse(Capture(), new SelectionProjection([], []), ["assessment/one"], "## 142 words\n\n118 parsed, 24 did not.")
        {
            InvocationId = "assessment/one",
            CompletionSummary = "142 words · 118 parsed · run 12:15",
            Words =
            [
                Word("Sungura", "analysed", ["approved"], 3, Reading("Sungura")),
                Word("alikula", "analysed", ["approved", "no-opinion"], 11, Reading("alikula"), Reading("alikula", 2)),
                Word("chakula", "analysed", ["no-opinion"], 6, Reading("chakula")),
                Word("hawajafika", "no-analysis", [], 48),
                Word("watoto", "analysed", ["approved"], 4, Reading("watoto")),
                Word("walikula", "analysed", ["disapproved"], 12, Reading("walikula")),
                Word("mwalimu", "capped", [], 700),
                Word("anapenda", "analysed", ["approved"], 9, Reading("anapenda")),
                Word("kitabu", "analysed", ["approved"], 2, Reading("kitabu")),
            ],
        };
    }

    private static IReadOnlyList<JsonElement> StatisticsRows() =>
    [
        .. Vocabulary.Select((item, index) => JsonDocument.Parse(
            $"{{\"kind\":\"word\",\"form\":\"{item.Word}\",\"attempts\":{(index + 1) * 37},\"passes\":{index + 2}," +
            $"\"elapsed_ns\":{(index == 3 ? 48_000_000 : (index + 1) * 1_300_000)},\"capped\":{(index == 6 ? "true" : "false")},\"timed_out\":false}}")
            .RootElement.Clone()),
    ];

    private static string TraceFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v2-matinlu.json"));

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

    // The banner shows as it does on a first run: the harness never remembers an acknowledgment.
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
