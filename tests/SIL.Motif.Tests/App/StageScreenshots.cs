using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
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
            Skip = $"Set {FolderVariable} to a folder to capture every stage as images.";
    }
}

/// <summary>
/// Opens the real window over realistic data, then saves every stage and Results view as an image, at the
/// narrowest and the default width, in the light and the dark theme. The window renders with Skia and the
/// system's fonts, as the app does, so these show what a person would see, not a stub's layout.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class StageScreenshots
{
    private const string ProjectPath = @"C:\Users\linguist\FieldWorks\Projects\Sample\Sample.fwdata";
    private static readonly Guid Story = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Letter = Guid.Parse("11111111-0000-0000-0000-000000000002");

    [ScreenshotFact]
    public void CaptureEveryStage()
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
                        foreach (var (name, stage, view) in Views())
                        {
                            workspace.CurrentStage = stage;
                            workspace.ResultsView = view;
                            Save(window, Path.Combine(folder, $"{name}-{width}-{theme}.png"));
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

    private static IEnumerable<(string Name, WorkflowStage Stage, ResultsView View)> Views() =>
    [
        ("1-project", WorkflowStage.Project, ResultsView.Words),
        ("2-grammar", WorkflowStage.Grammar, ResultsView.Words),
        ("3-texts", WorkflowStage.Texts, ResultsView.Words),
        ("4-results-words", WorkflowStage.Results, ResultsView.Words),
        ("5-results-intext", WorkflowStage.Results, ResultsView.InText),
        ("6-results-statistics", WorkflowStage.Results, ResultsView.Statistics),
        ("7-handoff", WorkflowStage.Handoff, ResultsView.Words),
    ];

    private static void Save(MainWindow window, string path)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path);
    }

    private static async Task<(HandoffWorkspaceViewModel Workspace, MainWindow Window)> OpenOverSampleData()
    {
        var fake = new FakeCommandClient();
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, DateTimeOffset.UtcNow)]);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token(), DateTimeOffset.UtcNow.AddHours(-2), false));
        fake.ProjectHistoryIs(new ProjectHistoryResponse(
        [
            new ProjectHistoryEntry(DateTimeOffset.Now.AddMinutes(-20), ProjectHistoryKind.Assessment, "142 words · 118 parsed · 9 differ from stored"),
            new ProjectHistoryEntry(DateTimeOffset.Now.AddHours(-2), ProjectHistoryKind.Baseline, "Baseline captured · 1,318 entries · 3 texts"),
        ]));
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

        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, new Picker()), new ProjectHistoryViewModel(fake), new BaselineViewModel(fake),
            new GrammarViewModel(fake), selection, words, new AssessViewModel(fake, selection),
            new StatisticsViewModel(fake), new HandoffViewModel(fake, selection, new Folder(), new Drag()));
        var window = new MainWindow();
        window.Compose(workspace);
        window.Show();

        await workspace.SetProjectAsync(ProjectPath);
        foreach (var text in selection.Texts) text.IsChecked = true;
        await Task.Yield();
        await words.ReloadAsync();
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        workspace.Statistics.AssessmentId = "assessment/one";
        await workspace.Statistics.LoadCommand.ExecuteAsync(null);
        workspace.Assess.Words.SelectedRow = workspace.Assess.Words.Rows.FirstOrDefault(row => row.Word == "hawajafika");
        workspace.Assess.Trace.Result = WordTraceQuery.LoadDiagnostic(TraceFixture()).Value;
        workspace.ResultsInText.SelectToken(workspace.ResultsInText.VisibleLines[0].Tokens[1]);
        await workspace.Handoff.RunCommand.ExecuteAsync(null);
        workspace.Handoff.LatestAssessmentAt = workspace.Handoff.WrittenAt!.Value.AddMinutes(35);
        return (workspace, window);
    }

    // Load warnings copied from a real project's grammar load, and health findings in the parser's newer shape.
    private static IReadOnlyList<GrammarWarning> GrammarFindings()
    {
        var lines = new[]
        {
            ("", "environment representation failed validation", 7),
            ("invalid environment \"e2\" (/ _ [C])", "unknown natural class \"C\"; treated as absent", 5),
            ("allomorph \"kat\"", "cannot segment \"kat\": no character definition matches at position 0; skipped", 3),
            ("", "MSA has zero loadable allomorphs for this stratum bucket", 2),
            ("phoneme \"ng'\"", "representation collides with an earlier phoneme/boundary; skipped", 1),
            ("", "inferred segment \"ŋ\" carries no authored feature values, so it satisfies every feature-based natural class", 2),
        };
        var findings = new List<GrammarWarning>();
        foreach (var (subject, problem, count) in lines)
        {
            for (var index = 0; index < count; index++)
            {
                var text = "warning: " + (subject.Length == 0 ? problem : $"{subject}: {problem}");
                findings.Add(new GrammarWarning("warning", string.Empty,
                    subject.Length == 0 ? [] : [new GrammarWarningPart(subject, "text")], [new GrammarWarningPart(problem, "text")], text));
            }
        }
        foreach (var entry in new[] { "mbo - ADD", "di - EVID", "phwet - entrar", "botari - boa tarde" })
        {
            findings.Add(new GrammarWarning("warning", "Partial morpheme",
                [new GrammarWarningPart(entry, "object", null, "lex_entry", "silfw://localhost/link?tool=lexiconEdit")],
                [new GrammarWarningPart($"Lexical entry '{entry}' is partially analyzed.", "text")],
                $"warning: hc-partial-morpheme: Lexical entry '{entry}' is partially analyzed.")
            {
                Group = "Partial morpheme analysis",
                Code = "hc-partial-morpheme",
            });
        }
        return findings;
    }

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

    private static ProjectAnalysis Stored(string word, int variant = 0) =>
        new(ProjectAnalysisKey.For(Reading(word, variant)), Resolved(word).Morphs);

    private static TextToken Token(string word, bool stored = true, int variant = 0) =>
        new(word, word, null, stored ? "approved" : "unanalysed")
        {
            Analysis = stored ? Stored(word, variant) : null,
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

    private static string TraceFixture([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!, "..", "TestFixtures", "trace-details-v2-matinlu.json")));

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

    private sealed class Drag : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
