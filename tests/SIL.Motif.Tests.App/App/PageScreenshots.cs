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
            var (workspace, window) = await OpenOverSampleData(configure: (fake, _) =>
                fake.OverviewCompletesWith(OverviewPageWordsTests.Populated()));
            try
            {
                var overview = workspace.PageModel<OverviewPageModel>().Overview;
                Assert.NotNull(overview);
                Assert.Equal(142, overview.SelectionWordCount);

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
                    fake.OverviewCompletesWith(state == "empty" ? EmptyOverview() : OverviewFor(state));
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

    private static OverviewResponse OverviewFor(string state) => state == "stale"
        ? OverviewPageWordsTests.Populated() with
        {
            IsStale = true,
            LastFieldWorksSaveUtc = DateTimeOffset.UtcNow,
        }
        : OverviewPageWordsTests.Populated();

    private static OverviewResponse EmptyOverview()
    {
        var saved = DateTimeOffset.UtcNow;
        return new OverviewResponse("Sample", saved, saved, 0, 0, 0, 0, 0, 0, 0, null, null, null,
            null, null, new OverviewTextCoverage(0, 0, 0, 0, 0, 0),
            new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0), new OverviewTiming(null, null, [], 0), null)
        {
            SelectionResolved = true,
            ProjectFileName = "Sample.fwdata",
            BaselineCapturedUtc = saved,
            BaselineSourceLastWriteUtc = saved,
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
            HandoffMarkdown = SampleHandoffMarkdown,
        });

        // Try a Word traces through the page's own path: a result set directly is wiped when a word is chosen.
        fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!);
        fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(SampleInspection(request.Subject))));
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
        "- `assessment.json`: every word, whether it parsed, and how long it took.\n" +
        "- `texts.json`: the chosen texts with the analyses the project stores.\n" +
        "- `grammar.json`: the grammar the parser used.\n\n" +
        "Start with *hawajafika*, approved in FieldWorks as ha-wa-ja-fik-a but not parsed.\n";

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
                "silfw://localhost/link?tool=lexiconEdit")
            {
                AllomorphId = Id("allomorph " + form + item.Glosses[index], 0),
                GrammaticalInfoId = Id("grammatical info " + item.Glosses[index], 0),
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
        };
        var assessment = Assessment();
        var words = assessment.Words.Select(word => word with
        {
            StoredAnalyses = word.Word == "hawajafika" || word.ReadingGrades is { Count: > 0 }
                ? [Resolved(word.Word) with
                {
                    StoredAnalysisOpinion = word.ReadingGrades is [ReadingGrade.Disapproved] ? ReadingGrade.Disapproved : ReadingGrade.Approved,
                }]
                : [],
        }).ToArray();
        var facts = SampleFacts(asked);
        asked = ObjectUsesQuery.WithTimingKey(asked, facts)!;
        var timings = asked.TimingKind is null ? [] : words.Take(4).Select((word, index) => new SIL.Motif.Worker.Store.AssessmentObjectTiming(
            asked.TimingKind, asked.TimingKey!, "authored", "synthesis", asked.Label ?? asked.TimingKey!, word.Word,
            3 + index, null, (index + 1) * 1_400_000L)).ToArray();
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

    // The sample trace records no forms; a real one names matinlu's morphemes as FieldWorks spells them.
    private static readonly (string Form, string Gloss)[] TraceMorphemes = [("tin", "see"), ("ma-", "PST"), ("-lu", "3SG")];

    /// <summary>
    /// The sample trace with the FieldWorks identities a real project's trace records: its morphs by allomorph and
    /// grammatical info, and its rules by the key Timing records them under.
    /// </summary>
    internal static WordTraceResponse TraceWithIdentities()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(TraceFixture())!;
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
        var item = Vocabulary.Single(entry => entry.Word == word);
        return new ParserReading([.. Resolved(word).Morphs.Select(morph => morph with
        {
            AllomorphId = Id((morph.Form.Contains('-') ? "allomorph " : "stem of " + word + " ") + morph.Form + morph.Gloss, 0),
            GrammaticalInfoId = Id("grammatical info " + morph.Gloss, 0),
        })])
        {
            StoredAnalysisId = Id(word, 97),
            StoredAnalysisOpinion = ReadingGrade.Approved,
            Identity = new ApprovedMorphology([.. Reading(word, 7).Morphs.Select((morph, index) =>
                new ApprovedMorph(morph.Form, morph.Msa, morph.InflType, [item.Forms[index]]))]),
        };
    }

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
        return new TextWordsResponse(words, [lines], HasBaseline: true, words.Sum(word => word.Occurrences.Count));
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
                ProjectStanding = grades.Contains("approved") ? "approved" :
                    grades.Contains("disapproved") ? "rejected" :
                    outcome == "no-analysis" ? "not-present" : "candidate",
                OccurrenceCount = 1,
                TryWordLink = "silfw://localhost/link?tool=Analyses",
                StoredAnalyses = grades.Contains("approved") ? [ApprovedInAssessment(word)] : [],
            };
        return new AssessCommandResponse(Capture(), new SelectionProjection([], []), ["assessment/one"], "## 142 words\n\n118 parsed, 24 did not.")
        {
            InvocationId = "assessment/one",
            CompletionSummary = "142 words · 118 parsed · run 12:15",
            Words =
            [
                Word("Sungura", "analysed", ["approved"], 3, Reading("Sungura")),
                Word("alikula", "analysed", ["approved", "no-opinion"], 11, Reading("alikula"), Reading("alikula", 2)) with
                {
                    ExpectedAnalysis = AlikulaApproved(),
                    StoredAnalyses = [AlikulaApproved()],
                    Readings = [new ParserReading([Piece("a-", "3SG"), Piece("li-", "PST"), Piece("ku-", "INF"), Piece("l", "eat"),
                        Piece("-a", "FV")]), Resolved("alikula")],
                },
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

    /// <summary>The sample trace's document, as PanGloss wrote it.</summary>
    internal static string SampleTrace() => TraceFixture();

    private static string TraceFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"));

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
