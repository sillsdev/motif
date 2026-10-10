using System.Collections.Specialized;
using Avalonia.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="ResultsInTextViewModel"/>: each occurrence is compared with the analysis stored at that very
/// place — the parser produced it, produced something else, proposes something where nothing is stored, or
/// found nothing — and a filter keeps only the lines holding a matching word, dimming the rest of them.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ResultsInTextViewModelTests : IAsyncLifetime
{
    private readonly List<SelectionModelFixture> _fixtures = [];
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            foreach (var fixture in _fixtures) await fixture.DisposeAsync();
        }, TimeSpan.FromSeconds(60));
        return Task.CompletedTask;
    }

    [Fact]
    public void ReaderInventoryKeepsUnseenWordListRowsUnmaterialized()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var words = new TextWordsViewModel(client, selection, client.ReaderOwner);
            var assess = new AssessViewModel(client, selection);
            var reader = new ResultsInTextViewModel(words, assess, _ => { }, _ => { },
                new ChangesViewModel(client), client, client.ReaderOwner);
            await words.SetProjectAsync(ProjectPath);
            var analysis = Stored(Book, "book");
            var source = Enumerable.Range(0, 1000).Select(index => new TextWord($"word-{index}", Guid.NewGuid().ToString(),
                [new WordOccurrence(TextId, "Story", index + 1, "sentence", "approved", analysis)], [analysis], [])
                { Analyses = [analysis] }).ToArray();
            var lines = source.Select((word, index) => new TextLine(index + 1,
                [new TextToken(word.Form, word.Form, "book", "approved")
                    { Analysis = analysis, StoredAnalyses = [analysis], WordformId = Guid.Parse(word.WordformGuid!) }])).ToArray();
            var fixture = new SelectionModelFixture(client);
            _fixtures.Add(fixture);
            await fixture.PublishAsync(new TextWordsResponse(source, [new TextLines(TextId, "Story", lines)], true, 1000));

            Assert.Equal(1000, reader.AllCount);
            Assert.Equal(1000, words.Rows.Count);
            Assert.Equal(0, words.MaterializedRowCount);
            words.WordCardActions = reader;
            var card = reader.GetCardToken(Result(source[^1].Form));
            Assert.Equal(Guid.Parse(source[^1].WordformGuid!), card.WordformId);
            Assert.Equal(0, words.MaterializedRowCount);
            var header = reader.VisibleHeaders[^1];
            var line = reader.RealizeLine(header);
            await ((SIL.Motif.App.Services.IProgressivePageSource)line.TokenSource!).ReadPageAsync(0, 20, CancellationToken.None);
            var final = Assert.Single(line.Tokens);
            Assert.Equal("book", final.ProjectSummary);
            Assert.Equal(0, words.MaterializedRowCount);
            Assert.Equal("book", Assert.Single(final.ProjectApprovedAnalyses).Gloss);
            Assert.Equal(0, words.MaterializedRowCount);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void TokensAndStandaloneCardsShareExactlyThreeTargetCommands()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (owner, _, _) = await Loaded();
            var tokens = SelectionModelFixture.VisibleLines(owner).SelectMany(line => line.Tokens)
                .Where(token => token.IsWord).Append(owner.GetCardToken(Result("standalone", Book))).ToArray();
            Assert.Equal(3, owner.TokenActionCommands.Count);
            Assert.Equal(3, owner.TokenActionCommands.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.All(tokens, token =>
            {
                Assert.Same(owner.AddChangeForTargetCommand, token.AddChangeForTokenCommand);
                Assert.Same(owner.TryWordForTargetCommand, token.TryWordForTokenCommand);
                Assert.Same(owner.StageMarkingChoiceForTargetCommand, token.StageMarkingChoiceForTokenCommand);
            });
            Assert.DoesNotContain(typeof(ResultsTokenViewModel).GetFields(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                field => typeof(System.Windows.Input.ICommand).IsAssignableFrom(field.FieldType));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ACapturedFixChoiceKeepsItsOriginalTargetAfterAnotherTokenIsSelected()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (owner, _, client) = await Loaded();
            var original = SelectionModelFixture.VisibleLines(owner).SelectMany(line => line.Tokens).First(token => token.Form == "kitabu");
            var other = SelectionModelFixture.VisibleLines(owner).SelectMany(line => line.Tokens).First(token => token.Form == "anapenda");
            var choice = Assert.IsType<WordMarkingChoice>(Assert.Single(original.Disposition.StoredRows).Tiles
                .Single(item => item.Kind == WordDispositionKind.Absent).Action);
            owner.SelectToken(other);

            await owner.StageMarkingChoiceForTargetCommand.ExecuteAsync(choice);

            var request = Assert.Single(client.PendingPutRequests);
            var removed = request.Change;
            Assert.Equal(ChangeKinds.RemoveAnalysis, removed.Kind);
            Assert.Equal(original.Form, removed.Word);
            Assert.Equal(CanonicalId.FromGuid(original.WordformId!.Value).Value, removed.WordformId);
            Assert.Equal(choice.Choice.StoredAnalysisId, removed.StoredAnalysisId);
            Assert.Same(original.CaptureActionTarget(null).ExpectedContext, request.ExpectedContext);
            Assert.Equal(other.Occurrence, owner.SelectedToken!.Occurrence);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ACapturedSpellingActionKeepsItsTargetAfterAnotherTokenIsSelected()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (owner, _, client) = await Loaded();
            var original = SelectionModelFixture.VisibleLines(owner).SelectMany(line => line.Tokens).First(token => token.Form == "kitabu");
            var other = SelectionModelFixture.VisibleLines(owner).SelectMany(line => line.Tokens).First(token => token.Form == "anapenda");
            var action = original.IncorrectSpellingAction;
            owner.SelectToken(other);

            await owner.AddChangeForTargetCommand.ExecuteAsync(action);

            var written = Assert.Single(client.PendingPutRequests);
            Assert.Equal(original.Form, written.Change.Word);
            Assert.Equal(CanonicalId.FromGuid(original.WordformId!.Value).Value, written.Change.WordformId);
            Assert.Equal(ChangeKinds.IncorrectSpelling, written.Change.Kind);
            Assert.Equal(other.Occurrence, owner.SelectedToken!.Occurrence);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void CapturedReadActionsSendTheirOriginalEvidenceAndProducingAssessmentSet()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (owner, _, fake) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(owner).SelectMany(line => line.Tokens).First(item => item.Form == "kitabu");
            var expected = new ExpectedContext(ReadBaseline) { TextIds = [TextId], SelectionEvidence = new(
                "explicit", null, [], [], null, "reader-root", ["reader-replacement"], [], []) };
            var target = new WordActionTarget(token.Form, token.WordformId, token.Occurrence, "reader-root", expected);
            var choice = new WordMarkingChoice(target, new AnalysisMarkingChoice(AnalysisMarkingActionKind.KeepFieldWorks,
                "Keep FieldWorks", string.Empty, null, null, null, "Unread", "Read", null), true);

            await owner.StageMarkingChoiceForTargetCommand.ExecuteAsync(choice);

            var request = Assert.Single(fake.ReadWordStateRequests, request => request.ExpectedContext is not null);
            Assert.Same(expected, request.ExpectedContext);
            Assert.Equal([token.Occurrence!], request.Occurrences);
            Assert.Equal(["reader-root", "reader-replacement"], request.AssessmentIds);
            Assert.True(request.IsRead);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadScopesCompareEvidenceValuesAndRefuseMixedContextsBeforeWriting(bool mixed)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (owner, _, fake) = await Loaded();
            var expected = new ExpectedContext(ReadBaseline) { TextIds = [TextId], AddedWords = ["added"] };
            var other = expected with { TextIds = expected.TextIds.ToArray(), AddedWords = expected.AddedWords.ToArray() };
            if (mixed) other = other with { Baseline = new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z",
                "sha256:" + new string('c', 64)) };
            using var first = new ResultsTokenViewModel("Story", 1, Word("kitabu", Stored(Book, "book")), null,
                occurrence: new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0), expectedContext: expected);
            using var second = new ResultsTokenViewModel("Story", 1, Word("kitabu", Stored(Book, "book")), null,
                occurrence: new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 1), expectedContext: other);
            var before = fake.ReadWordStateRequests.Count;

            await owner.MarkReadAsync([first, second]);

            if (mixed)
            {
                Assert.Equal(before, fake.ReadWordStateRequests.Count);
                Assert.NotNull(owner.ReadStateRefusal);
            }
            else
            {
                var request = Assert.Single(fake.ReadWordStateRequests.Skip(before));
                Assert.Same(expected, request.ExpectedContext);
                Assert.Equal([first.Occurrence!, second.Occurrence!], request.Occurrences);
                Assert.Null(owner.ReadStateRefusal);
            }
        }, TimeSpan.FromSeconds(180));
    }

    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly BaselineToken ReadBaseline = new("project-1", Digest, "1", "2026-09-05T00:00:00Z", Digest);
    private static readonly Guid TextId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ParagraphId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SegmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondParagraphId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SecondSegmentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherTextId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OtherParagraphId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid OtherSegmentId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static readonly ParseAnalysis Book = Reading("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly ParseAnalysis Love = Reading("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly ParseAnalysis Like = Reading("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly ParseAnalysis Child = Reading("aaaaaaaa-0000-0000-0000-000000000004");

    private static ParseAnalysis Reading(string form) =>
        new([new ParseMorph(form, "bbbbbbbb-0000-0000-0000-000000000001", null, null)]);

    private static ProjectAnalysis Stored(ParseAnalysis reading, string gloss) =>
        new(ProjectAnalysisKey.For(reading), [new ParserReadingMorph("form", gloss, "n", null, false, null)
            { Entry = "form" }])
        {
            StoredAnalysisId = CanonicalId.FromGuid(Guid.Parse(reading.Morphs[0].Form!
                .Replace("aaaaaaaa", "dddddddd", StringComparison.Ordinal))).Value,
            StoredAnalysisOpinion = ReadingGrade.Approved,
            Identity = new ApprovedMorphology(reading.Morphs.Select(morph => new ApprovedMorph(
                morph.Form, morph.Msa, morph.InflType, ["form"])).ToArray()),
        };

    private static TextToken Word(string text, ProjectAnalysis? stored, int index = 0) =>
        new(text, text, null, "approved")
        {
            Analysis = stored,
            StoredAnalyses = stored is null ? [] : [stored],
            StoredAnalysisId = stored?.StoredAnalysisId,
            OccurrenceIndex = index,
            WordformId = Guid.Parse(text switch
            {
                "kitabu" => "aaaaaaaa-0000-0000-0000-000000000011",
                "anapenda" => "aaaaaaaa-0000-0000-0000-000000000012",
                "mtoto" => "aaaaaaaa-0000-0000-0000-000000000013",
                "zzz" => "aaaaaaaa-0000-0000-0000-000000000014",
                _ => throw new ArgumentOutOfRangeException(nameof(text)),
            }),
        };
    private static AssessmentWordResult Result(string word, params ParseAnalysis[] readings) =>
        new(word, readings.Length > 0 ? "analysed" : "no-analysis", false, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, word, 3, false, false, false, readings, []),
            Readings = readings.Select(_ => new ParserReading([new ParserReadingMorph(word, "gloss", "n", null, false, null)])).ToArray(),
            ReadingGrades = readings.Select(_ => ReadingGrade.NoOpinion).ToArray(),
        };

    private static ParserReading AsReading(ProjectAnalysis analysis) => new(analysis.Morphs)
    {
        StoredAnalysisId = analysis.StoredAnalysisId, StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
        Identity = analysis.Identity,
    };

    private async Task<(ResultsInTextViewModel InText, List<string> Shown, FakeCommandClient Client)> Loaded(
        PendingChangesSnapshot? pending = null, IReadOnlyList<TextLine>? sourceLines = null,
        IReadOnlyList<TextLines>? sourceTexts = null,
        IReadOnlyList<OccurrenceAnchor>? readOccurrences = null,
        Func<WordReadStateRequest, CancellationToken, Task<CommandOutcome<WordReadStateResponse>>>? readStateHandler = null,
        bool waitForReadState = true,
        Func<ResultsInTextViewModel, ChangesViewModel, FakeCommandClient, Task>? afterAssessment = null,
        IReadOnlyList<AssessmentWordResult>? assessmentWords = null,
        IReadOnlyList<TextWord>? projectWords = null, Action<Func<Task>>? captureReload = null,
        Action<AssessViewModel>? captureAssess = null,
        IReadOnlyList<string>? assessmentIds = null,
        IReadOnlyList<ProducedAssessmentReference>? measurements = null,
        IReadOnlyList<string>? timingOverrideAssessmentIds = null,
        Action<SelectionReaderPausePoint>? readerPause = null)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var texts = new TextWordsViewModel(fake, selection, fake.ReaderOwner);
        var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
        captureAssess?.Invoke(assess);
        var shown = new List<string>();
        var changes = new ChangesViewModel(fake);
        if (pending is not null) fake.PendingChangesIs(pending);
        await changes.OpenProjectAsync(ProjectPath);
        var inText = new ResultsInTextViewModel(texts, assess, shown.Add, _ => { }, changes, fake, fake.ReaderOwner);

        var lines = sourceLines ??
        [
                new TextLine(1, [Word("kitabu", Stored(Book, "book"), 0), Word("anapenda", Stored(Love, "love"), 1),
                    Word("mtoto", null, 2), Word("zzz", null, 3), new TextToken(".", null, null, null)])
                {
                    ParagraphId = ParagraphId,
                    SegmentId = SegmentId,
                    ParseIsCurrent = true,
                },
                new TextLine(2, [Word("kitabu", Stored(Book, "book"))])
                {
                    ParagraphId = SecondParagraphId,
                    SegmentId = SecondSegmentId,
                    ParseIsCurrent = true,
            },
        ];
        var readState = (readOccurrences ?? []).ToHashSet();
        var loadedTexts = sourceTexts ?? [new TextLines(TextId, "Alpha", lines)];
        var availableOccurrences = loadedTexts.SelectMany(text => text.Lines
                .Where(line => line.ParagraphId != Guid.Empty && line.SegmentId != Guid.Empty)
                .SelectMany(line => line.Tokens.Where(token => token.Form is not null)
                    .Select(token => new OccurrenceAnchor(text.TextId, line.ParagraphId, line.SegmentId,
                        token.OccurrenceIndex))))
            .ToArray();
        SelectionModelFixture? currentFixture = null;
        fake.OnReadWordState(readStateHandler ?? ((request, _) =>
        {
            if (currentFixture is not null) return Task.FromResult(currentFixture.WriteReadState(request));
            if (request.IsRead is { } isRead)
            {
                var targets = request.Occurrences ?? availableOccurrences
                    .Where(occurrence => occurrence.TextId == request.TextId).ToArray();
                foreach (var occurrence in targets)
                    if (isRead) readState.Add(occurrence);
                    else readState.Remove(occurrence);
            }
            return Task.FromResult(CommandOutcome<WordReadStateResponse>.Success(new WordReadStateResponse(
                readState.Where(occurrence => occurrence.TextId == request.TextId).ToArray(), true)));
        }));
        var source = new TextWordsResponse(projectWords ?? [], loadedTexts, HasBaseline: true);
        var fixture = new SelectionModelFixture(fake) { PauseAt = readerPause };
        _fixtures.Add(fixture);
        currentFixture = fixture;
        captureReload?.Invoke(async () =>
        {
            await fixture.PublishAsync(fixture.Source ?? source, assess.Result);
            await SelectionModelFixture.RealizeAsync(inText);
        });
        await fixture.PublishAsync(source);
        await texts.SetProjectAsync(ProjectPath);

        fake.AssessCompletesWith(SelectionModelFixture.WithOrigins(new AssessCommandResponse(
            new BaselineCaptureResponse(new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", Digest),
                ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
            new SelectionProjection([], []), assessmentIds ?? ["assessment/one"], "(summary)")
        {
            Measurements = measurements ?? [new ProducedAssessmentReference("assessment/one", AssessmentKinds.ParseTime,
                "invocation/one")],
            TimingOverrideAssessmentIds = timingOverrideAssessmentIds ?? [],
            Words = assessmentWords ??
            [
                Result("kitabu", Book, Child) with
                {
                    ProjectStanding = ProjectStanding.Approved, StoredAnalysesAvailable = true,
                    StoredAnalyses = [AsReading(Stored(Book, "book"))],
                    ReadingGrades = [ReadingGrade.Approved, ReadingGrade.NoOpinion],
                },
                Result("anapenda", Like) with
                {
                    ProjectStanding = ProjectStanding.Approved, StoredAnalysesAvailable = true,
                    StoredAnalyses = [AsReading(Stored(Love, "love"))],
                },
                Result("mtoto", Child),
                Result("zzz"),
            ],
        }));
        await assess.RunCommand.ExecuteAsync(null);
        await fixture.PublishAsync(source, assess.Result, readOccurrences: readOccurrences);
        await SelectionModelFixture.RealizeAsync(inText, waitForReadState);
        if (waitForReadState) await inText.ReadStateRefresh;
        if (afterAssessment is not null) await afterAssessment(inText, changes, fake);
        return (inText, shown, fake);
    }

    [Fact]
    public void LoadingWordsPublishesOneCompleteReader()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var selection = new SelectionViewModel(fake) { AllWordforms = true };
            var words = new TextWordsViewModel(fake, selection, fake.ReaderOwner);
            var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
            var changes = new ChangesViewModel(fake);
            var reader = new ResultsInTextViewModel(words, assess, _ => { }, _ => { }, changes, fake, fake.ReaderOwner);
            var analysis = Stored(Book, "book");
            var token = Word("kitabu", analysis);
            var text = new TextLines(TextId, "Alpha", [new TextLine(1, [token])
            {
                ParagraphId = ParagraphId, SegmentId = SegmentId,
            }]);
            var projectWord = new TextWord("kitabu", token.WordformId!.Value.ToString("D"),
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "approved", analysis)], [analysis], [], 0, false)
            {
                Analyses = [analysis],
            };
            var fixture = new SelectionModelFixture(fake);
            _fixtures.Add(fixture);
            var source = new TextWordsResponse([projectWord], [text], HasBaseline: true);
            var observed = new List<SIL.Motif.Commands.SelectionReading.SelectionTextSummary>();
            reader.Texts.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is null) return;
                foreach (ResultsTextViewModel added in e.NewItems) observed.Add(added.Summary!);
            };
            await words.SetProjectAsync(ProjectPath);
            await fixture.PublishAsync(source);
            Assert.Equal(1, Assert.Single(observed).OccurrenceCount);
            Assert.Empty(reader.LinePages!.RealizedLines);
            await SelectionModelFixture.RealizeAsync(reader);
            Assert.Equal("book", Assert.Single(Assert.Single(SelectionModelFixture.VisibleLines(reader)).Tokens).ProjectSummary);
            observed.Clear();
            await fixture.PublishAsync(source);
            Assert.Equal(1, Assert.Single(observed).OccurrenceCount);
            Assert.Empty(reader.LinePages!.RealizedLines);
            await SelectionModelFixture.RealizeAsync(reader);
            Assert.Equal("book", Assert.Single(Assert.Single(SelectionModelFixture.VisibleLines(reader)).Tokens).ProjectSummary);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void StoredHereFollowsMorphologyIdentityRatherThanTheDisplayKey()
    {
        var stored = Stored(Book, "book");
        var differentlyKeyed = new ProjectAnalysis("display-key-only", stored.Morphs)
        {
            StoredAnalysisId = stored.StoredAnalysisId,
            StoredAnalysisOpinion = stored.StoredAnalysisOpinion,
            Identity = stored.Identity,
        };
        var token = new TextToken("kitabu", "kitabu", null, "approved")
        {
            Analysis = differentlyKeyed,
            StoredAnalyses = [differentlyKeyed],
        };

        var card = new ResultsTokenViewModel("Text", 1, token, Result("kitabu", Book));

        Assert.True(Assert.Single(card.Readings).IsStoredHere);
        Assert.Equal(AnalysisMarkingClass.Same, card.Marking.PanGlossClass);
    }
    [Fact]
    public void EachOccurrenceIsJudgedAgainstWhatIsStoredThere()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();

            Assert.Null(inText.Message);
            var line = SelectionModelFixture.VisibleLines(inText)[0].Tokens;
            Assert.Equal(OccurrenceVerdict.Differs, line[0].Verdict);
            Assert.StartsWith("parser:", line[0].ParserLine, StringComparison.Ordinal);
            Assert.Equal(OccurrenceVerdict.Differs, line[1].Verdict);
            Assert.StartsWith("parser:", line[1].ParserLine, StringComparison.Ordinal);
            Assert.Equal(OccurrenceVerdict.New, line[2].Verdict);
            Assert.Equal("Not present", line[2].ProjectStatusLabel);
            Assert.Equal(OccurrenceVerdict.NoParse, line[3].Verdict);
            Assert.False(line[4].IsWord);
            Assert.Contains(line[0].Readings, reading => reading.IsStoredHere);
            Assert.DoesNotContain(line[1].Readings, reading => reading.IsStoredHere);

            Assert.Equal(5, inText.AllCount);
            Assert.Equal(0, inText.MatchesCount);
            Assert.Equal(3, inText.DiffersCount);
            Assert.Equal(1, inText.NewCount);
            Assert.Equal(1, inText.NoParseCount);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void TheFakeTextViewExposesExactOpinionAndParserSetMarkings()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var tokens = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();

            Assert.Equal(AnalysisMarkingClass.Extra, tokens[0].Marking.PanGlossClass);
            Assert.Equal(ReadingGrade.Approved, Assert.Single(tokens[0].Marking.FieldWorksAnalyses).Opinion);
            Assert.Equal(AnalysisMarkingActionKind.KeepFieldWorks, tokens[0].Marking.PrimaryAction!.Kind);
            Assert.Contains(tokens[0].Marking.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.Add &&
                choice.Label == "Add as Unknown" && choice.Subtitle == "Not in FieldWorks → Unknown");
            Assert.Equal(AnalysisMarkingClass.Conflict, tokens[1].Marking.PanGlossClass);
            Assert.Null(tokens[1].Marking.PrimaryAction);
            Assert.Equal(AnalysisMarkingClass.Different, tokens[2].Marking.PanGlossClass);
            Assert.Equal(AnalysisMarkingClass.None, tokens[3].Marking.PanGlossClass);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void CardAndStripGiveTheSameAnswerForEveryLoadedWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var tokens = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens).Where(token => token.IsWord);

            foreach (var token in tokens)
            {
                var placement = CompareSemantics.PlacementOf(token.Comparison);
                var expected = CompareSemantics.MeaningOf(placement.Standing, placement.Column).Label;
                Assert.Equal(expected, token.PanGlossSummary);
                Assert.Equal(expected, token.VerdictLabel);
            }
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void NoParseCardNamesTheOutcomeAndItsMeaning()
    {
        var token = new ResultsTokenViewModel("Text", 1, new TextToken("hawajafika", "hawajafika", null, null),
            new AssessmentWordResult("hawajafika", "no-analysis", false, "Complete", 1, null)
            { ProjectStanding = ProjectStanding.Approved });

        Assert.Equal("∅ No parse · the grammar builds nothing for this word · Lost", token.PanGlossCardSummary);
        Assert.Equal("Why it might not parse", token.WarningSectionHeading);
    }
    [Fact]
    public void OccurrenceFiltersFollowTheSharedMarkingClass()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText)[0].Tokens[0];

            Assert.Equal(AnalysisMarkingClass.Extra, token.Marking.PanGlossClass);
            Assert.Equal(OccurrenceVerdict.Differs, token.Verdict);
            Assert.Equal(0, inText.MatchesCount);
            Assert.Equal(3, inText.DiffersCount);
        }, TimeSpan.FromSeconds(180));
    }
    [Fact]
    public void SavedReadStateLoadsPerOccurrenceWithoutHidingNeedsALook()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var read = new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0);
            var (inText, _, fake) = await Loaded(readOccurrences: [read]);
            var tokens = SelectionModelFixture.VisibleLines(inText)[0].Tokens;

            Assert.False(tokens[0].Marking.IsUnread);
            Assert.True(tokens[0].Marking.NeedsALook);
            Assert.True(tokens[1].Marking.IsUnread);
            Assert.Empty(fake.ReadWordStateRequests);
            Assert.Equal("assessment/one", fake.ReaderOwner.Reader!.Context.Evidence!.RootAssessmentId);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ReadStateRequestsNameTheAssessmentShownInTheWindow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (owner, _, fake) = await Loaded();
            await owner.MarkReadAsync(SelectionModelFixture.VisibleLines(owner)[0].Tokens[0]);

            Assert.Contains(fake.ReadWordStateRequests, request =>
                request.AssessmentIds?.Contains("assessment/one") == true);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AnUncertainChangeRemainsMarkedUncertainAfterRefresh()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            const string changeId = "change/refresh-uncertain";
            var pending = new PendingChangesSnapshot("draft/refresh-uncertain", "revision/refresh-uncertain",
                [new PendingChange(changeId, Wordform("kitabu"), "kitabu", ChangeKinds.Approve, null, null, [])],
                [new ChangeFit(changeId, ChangeFitStatus.Uncertain, [])
                {
                    Uncertainty = new ChangeUncertainty("The sentence changed.", [], []),
                }]);
            var (inText, _, _) = await Loaded(pending: pending, afterAssessment: async (viewModel, changes, client) =>
            {
                client.PendingLoadHandler = (_, _) => Task.FromResult(
                    CommandOutcome<PendingChangesSnapshot>.Success(pending));
                await changes.ReloadAsync();

                var token = SelectionModelFixture.VisibleLines(viewModel).SelectMany(line => line.Tokens)
                    .First(candidate => candidate.Form == "kitabu");
                Assert.True(token.Marking.IsUncertain);
                Assert.True(token.Marking.IsUnread);
                Assert.False(token.Marking.NeedsALook);
            });
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void EnterAndSpaceRouteWordTokensThroughTheCardOpener()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens).First(candidate =>
                candidate.Occurrence is not null);
            var opened = false;

            var handled = await ResultsInTextPanel.OpenTokenCardOnKeyboardAsync(Key.Space, token, _ =>
            {
                opened = true;
                return Task.CompletedTask;
            });

            Assert.True(handled);
            Assert.True(opened);
            Assert.False(await ResultsInTextPanel.OpenTokenCardOnKeyboardAsync(Key.Tab, token,
                _ => Task.CompletedTask));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void MarkingOneOccurrenceDoesNotCancelThePendingReadStateLoad() =>
        AvaloniaHeadlessFixture.RunUntilComplete(() => VerifyPendingPresentationReadAsync(true), TimeSpan.FromSeconds(60));

    [Fact]
    public void AReadStateLoadThatFinishesAfterMarkReadDoesNotMakeTheWordUnreadAgain() =>
        AvaloniaHeadlessFixture.RunUntilComplete(() => VerifyPendingPresentationReadAsync(false), TimeSpan.FromSeconds(60));

    private async Task VerifyPendingPresentationReadAsync(bool includeOtherText)
    {
        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var paused = 0;
        var alpha = new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0);
        var beta = new OccurrenceAnchor(OtherTextId, OtherParagraphId, OtherSegmentId, 0);
        var source = new List<TextLines>
        {
            new(TextId, "Alpha", [new TextLine(1, [Word("kitabu", Stored(Book, "book"))])
                { ParagraphId = ParagraphId, SegmentId = SegmentId, ParseIsCurrent = true }]),
        };
        if (includeOtherText) source.Add(new(OtherTextId, "Beta", [new TextLine(1, [Word("kitabu", Stored(Book, "book"))])
            { ParagraphId = OtherParagraphId, SegmentId = OtherSegmentId, ParseIsCurrent = true }]));
        ResultsInTextViewModel? owner = null;
        try
        {
            var loaded = await Loaded(sourceTexts: source, readOccurrences: includeOtherText ? [beta] : [],
                waitForReadState: false, readerPause: point =>
                {
                    if (point != SelectionReaderPausePoint.AfterPresentationStateLoad ||
                        Interlocked.CompareExchange(ref paused, 1, 0) != 0) return;
                    reached.Set();
                    if (!resume.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("The presentation read was not released.");
                });
            owner = loaded.InText;
            Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))));
            var pending = owner.ReadStateRefresh;
            Assert.False(pending.IsCompleted);
            var token = await owner.ReadOccurrenceAsync(alpha);
            Assert.NotNull(token);
            await owner.MarkReadAsync(token);
            Assert.Null(owner.ReadStateRefusal);
            Assert.False(token.Marking.IsUnread);
            resume.Set();
            await pending;
            token = await owner.ReadOccurrenceAsync(alpha);
            Assert.False(token!.Marking.IsUnread);
            if (includeOtherText)
            {
                var other = await owner.ReadOccurrenceAsync(beta);
                Assert.False(other!.Marking.IsUnread);
            }
            Assert.Null(owner.ReadStateRefusal);
        }
        finally
        {
            resume.Set();
            if (owner is not null) await owner.ReadStateRefresh;
        }
    }

    [Fact]
    public void ARejectedSingleWordReadPublishesItsRefusalForTheWindow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(candidate => candidate.IsWord);
            var changes = new List<string?>();
            inText.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
            client.OnReadWordState((_, _) => Task.FromResult(
                CommandOutcome<WordReadStateResponse>.Refused(new Refusal("word.read-state-invalid",
                    FailureReason.InvalidArgument, "This paragraph has not been parsed."))));

            await inText.MarkReadAsync(token);

            Assert.Contains("ReadStateRefusal", changes);
            Assert.NotNull(inText.ReadStateRefusal);
            Assert.Contains("This paragraph has not been parsed.", inText.ReadStateRefusal!.Details);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void APartialTextReadPublishesTheSkippedOccurrenceNoticeForTheWindow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var changes = new List<string?>();
            inText.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
            client.OnReadWordState((_, _) => Task.FromResult(CommandOutcome<WordReadStateResponse>.Success(
                new WordReadStateResponse([], true)
                {
                    SkippedOccurrences = [new OccurrenceAnchor(TextId, SecondParagraphId, SecondSegmentId, 0)],
                })));

            await inText.MarkTextReadAsync();

            Assert.Contains("ReadStateNotice", changes);
            Assert.True(inText.HasReadStateNotice);
            Assert.Equal("1 word occurrence was not marked Read because its paragraph has not been parsed.",
                inText.ReadStateNotice);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void SelectingAWordForKeyboardNavigationDoesNotMarkItRead()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText)[0].Tokens[0];

            inText.SelectToken(token);

            Assert.True(token.Marking.IsUnread);
            Assert.All(fake.ReadWordStateRequests, request => Assert.Null(request.IsRead));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void OpeningAWordCardMarksOnlyThatOccurrenceRead()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText)[0].Tokens[1];

            await inText.OpenTokenCardAsync(token);

            Assert.Equal(token.Occurrence, inText.SelectedToken!.Occurrence);
            Assert.False(token.Marking.IsUnread);
            var request = Assert.Single(fake.ReadWordStateRequests, item => item.IsRead is not null);
            Assert.Equal([token.Occurrence!], request.Occurrences);
            Assert.True(request.IsRead);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void StoppingTheReaderWaitsForTheReadStateAWordCardIsStillSaving()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText)[0].Tokens[1];
            var write = new TaskCompletionSource<CommandOutcome<WordReadStateResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fake.OnReadWordState((request, _) => request.IsRead is null
                ? Task.FromResult(CommandOutcome<WordReadStateResponse>.Success(new WordReadStateResponse([], true)))
                : write.Task);

            var opening = inText.OpenTokenCardAsync(token);
            var stopping = inText.StopAsync();
            await Task.Delay(50);

            Assert.False(stopping.IsCompleted, "the reader stopped while its read-state write was still running");
            write.SetResult(CommandOutcome<WordReadStateResponse>.Success(new WordReadStateResponse([], true)));
            await stopping;
            await opening;
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void WordCardTimingSelectsTheParseTimeMeasurementAndKeepsItsReplacements()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded(
                assessmentIds: ["assessment/parse", "assessment/object", "assessment/correctness"],
                measurements:
                [
                    new ProducedAssessmentReference("assessment/parse", AssessmentKinds.ParseTime, "invocation/one"),
                    new ProducedAssessmentReference("assessment/object", AssessmentKinds.ObjectTiming, "invocation/one"),
                    new ProducedAssessmentReference("assessment/correctness", AssessmentKinds.Correctness, "invocation/one"),
                ],
                timingOverrideAssessmentIds: ["assessment/rerun"]);
            fake.TimingCompletesWith(new TimingResponse("assessment/parse", "selected", "rule", 1, 1, 1, [], [], []));

            await inText.OpenTokenCardAsync(SelectionModelFixture.VisibleLines(inText)[0].Tokens[0]);

            var request = Assert.Single(fake.TimingRequests);
            Assert.Equal("assessment/parse", request.AssessmentId);
            Assert.Equal(["assessment/rerun"], request.OverrideAssessmentIds);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ExplicitReadAndUnreadAreScopedToOneOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText)[0].Tokens[2];

            await inText.MarkReadAsync(token);
            Assert.False(token.Marking.IsUnread);
            await inText.MarkUnreadAsync(token);

            Assert.True(token.Marking.IsUnread);
            var writes = fake.ReadWordStateRequests.Where(item => item.IsRead is not null).ToArray();
            Assert.Equal([true, false], writes.Select(item => item.IsRead));
            Assert.All(writes, request => Assert.Equal([token.Occurrence!], request.Occurrences));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ExplicitReadCanApplyToOnlyTheSuppliedSelection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var first = SelectionModelFixture.VisibleLines(inText)[0].Tokens[0];
            var second = SelectionModelFixture.VisibleLines(inText)[0].Tokens[1];
            var outside = SelectionModelFixture.VisibleLines(inText)[0].Tokens[2];

            await inText.MarkReadAsync([first, second]);

            Assert.False(first.Marking.IsUnread);
            Assert.False(second.Marking.IsUnread);
            Assert.True(outside.Marking.IsUnread);
            var request = Assert.Single(fake.ReadWordStateRequests, item => item.IsRead is not null);
            Assert.Equal([first.Occurrence!, second.Occurrence!], request.Occurrences);
            Assert.True(request.IsRead);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void SelectedOccurrencesCanBeMarkedReadAndUnreadFromThePage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var first = SelectionModelFixture.VisibleLines(inText)[0].Tokens[0];
            var second = SelectionModelFixture.VisibleLines(inText)[0].Tokens[1];
            var outside = SelectionModelFixture.VisibleLines(inText)[0].Tokens[2];
            first.IsSelectedForActions = true;
            second.IsSelectedForActions = true;

            Assert.Equal(2, inText.SelectedReadStateCount);
            await inText.MarkSelectionReadCommand.ExecuteAsync(null);
            Assert.False(first.Marking.IsUnread);
            Assert.False(second.Marking.IsUnread);
            Assert.True(outside.Marking.IsUnread);

            await inText.MarkSelectionUnreadCommand.ExecuteAsync(null);

            Assert.True(first.Marking.IsUnread);
            Assert.True(second.Marking.IsUnread);
            Assert.True(outside.Marking.IsUnread);
            var writes = fake.ReadWordStateRequests.Where(item => item.IsRead is not null).ToArray();
            Assert.Equal([true, false], writes.Select(item => item.IsRead));
            Assert.All(writes, request => Assert.Equal([first.Occurrence!, second.Occurrence!], request.Occurrences));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void WholeTextCanBeMarkedReadAndUnread()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var tokens = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();

            await inText.MarkTextReadAsync();
            Assert.All(tokens, token => Assert.False(token.Marking.IsUnread));
            await inText.MarkTextUnreadAsync();

            Assert.All(tokens, token => Assert.True(token.Marking.IsUnread));
            var writes = fake.ReadWordStateRequests.Where(item => item.IsRead is not null).ToArray();
            Assert.Equal([true, false], writes.Select(item => item.IsRead));
            Assert.All(writes, request =>
            {
                Assert.Equal(TextId, request.TextId);
                Assert.Null(request.Occurrences);
            });
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void UnreadFilterKeepsOnlyLinesWithUnreadOccurrences()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var firstLine = SelectionModelFixture.VisibleLines(inText)[0].Tokens.Where(token => token.IsWord).ToArray();
            await inText.MarkReadAsync(firstLine);

            inText.SetFilterCommand.Execute(ResultsInTextFilter.Unread);

            var visible = Assert.Single(SelectionModelFixture.VisibleLines(inText));
            Assert.Equal(2, visible.Number);
            Assert.True(visible.Tokens.Single(token => token.IsWord).Marking.IsUnread);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void FilteringToDifferencesKeepsOnlyTheirLines_AndDimsTheOtherWordsThere()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();

            inText.SetFilterCommand.Execute(ResultsInTextFilter.Differs);

            Assert.Equal(2, SelectionModelFixture.VisibleLines(inText).Count);
            var line = SelectionModelFixture.VisibleLines(inText)[0];
            Assert.False(line.Tokens[0].IsDimmed);
            Assert.False(line.Tokens[1].IsDimmed);
            Assert.True(line.Tokens[2].IsDimmed);
            Assert.False(SelectionModelFixture.VisibleLines(inText)[1].Tokens[0].IsDimmed);

            inText.SetFilterCommand.Execute(ResultsInTextFilter.All);
            Assert.Equal(2, SelectionModelFixture.VisibleLines(inText).Count);
            Assert.DoesNotContain(SelectionModelFixture.VisibleLines(inText).SelectMany(l => l.Tokens), token => token.IsDimmed);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void NeedsALookFilterKeepsWordsWithAvailableMarkingActions()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();

            Assert.Contains("NeedsALook", Enum.GetNames<ResultsInTextFilter>());
            inText.SetFilterCommand.Execute(Enum.Parse<ResultsInTextFilter>("NeedsALook"));

            var visibleWords = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Where(token => token.IsWord && !token.IsDimmed).Select(token => token.Form).ToArray();
            Assert.Equal(["kitabu", "anapenda", "mtoto", "kitabu"], visibleWords);
            Assert.Equal(4, inText.NeedsALookCount);
            Assert.DoesNotContain(SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens),
                token => token.Form == "zzz" && !token.IsDimmed);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void NeedsALookFilterRefreshesWhenAnActionIsStaged()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(candidate => candidate.Form == "mtoto");
            inText.SetFilterCommand.Execute(ResultsInTextFilter.NeedsALook);
            inText.SelectToken(token);

            await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

            await inText.SelectionRefresh;
            Assert.Equal(3, inText.NeedsALookCount);
            Assert.False(token.Marking.NeedsALook);
            Assert.True(token.IsDimmed);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AWordStripCanStageItsActionWithoutOpeningTheCard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(candidate => candidate.Form == "mtoto");

            Assert.True(inText.StagePrimaryMarkingActionForTokenCommand.CanExecute(token));
            await inText.StagePrimaryMarkingActionForTokenCommand.ExecuteAsync(token);

            var request = Assert.Single(client.PendingPutRequests);
            Assert.Equal(ChangeKinds.AddCandidate, request.Change.Kind);
            Assert.Equal("mtoto", request.Change.Word);
            Assert.Equal(CanonicalId.FromGuid(token.WordformId!.Value).Value, request.Change.WordformId);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void StagingAnActionKeepsVisibleLineContainersInPlace()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var before = SelectionModelFixture.VisibleLines(inText).ToArray();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(candidate => candidate.Form == "mtoto");
            inText.SelectToken(token);

            await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

            await inText.SelectionRefresh;
            Assert.Equal(before, SelectionModelFixture.VisibleLines(inText));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void SelectAllCountsChosenWordsAndUsesTheSameSelectionForReadActions()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();

            Assert.Equal($"Selected 0 of {inText.AllCount} words", inText.CheckedWordCountLabel);
            inText.SelectAllWordsCommand.Execute(null);

            Assert.Equal(inText.AllCount, inText.CheckedWordCount);
            Assert.Equal(inText.AllCount, inText.SelectedReadStateCount);
            Assert.Equal($"Selected {inText.AllCount} of {inText.AllCount} words", inText.CheckedWordCountLabel);
            inText.ClearSelectedWordsCommand.Execute(null);
            Assert.Equal(0, inText.CheckedWordCount);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void BulkRemovalPreviewNamesAffectedWordsAndTheirUsesBeforeStaging()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();

            Assert.True(inText.ChosenTextsAnalysisCount > 0);
            Assert.EndsWith($"({inText.ChosenTextsAnalysisCount})", inText.ChosenTextsRemoveHeader,
                StringComparison.Ordinal);
            Assert.Contains("all chosen Texts", inText.ScopeCountSummary, StringComparison.Ordinal);
            Assert.StartsWith($"{inText.ChosenTextsAnalysisCount} stored analyses on",
                inText.ChosenTextsRemovalPreview, StringComparison.Ordinal);
            Assert.Contains("stored analyses on", inText.ChosenTextsRemovalPreview, StringComparison.Ordinal);
            Assert.Contains("occurrences", inText.ChosenTextsRemovalPreview, StringComparison.Ordinal);
            Assert.Contains("everywhere in the project", inText.ChosenTextsRemovalPreview, StringComparison.Ordinal);
            Assert.Empty(client.PendingRemoveRequests);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AcceptNewSetForTheSelectedTextUsesTheRealTextScope()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded(sourceTexts: AcceptanceScopeTexts());
            inText.Changes.AssessmentId = "assessment/one";

            await inText.AcceptNewSetCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

            AssertCapturedAcceptance(client, ["kitabu"], [TextId, OtherTextId]);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AcceptNewSetForTheSelectionUsesTheSelectionScope()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded(sourceTexts: AcceptanceScopeTexts());
            inText.Changes.AssessmentId = "assessment/one";

            await inText.AcceptNewSetCommand.ExecuteAsync(AnalysisOperationScope.AssessmentSelection);

            AssertCapturedAcceptance(client, ["kitabu", "mtoto"], [TextId, OtherTextId]);
        }, TimeSpan.FromSeconds(180));
    }

    private static IReadOnlyList<TextLines> AcceptanceScopeTexts() =>
    [
        new(TextId, "Alpha", [new TextLine(1, [Word("kitabu", Stored(Book, "book"))])
            { ParagraphId = ParagraphId, SegmentId = SegmentId, ParseIsCurrent = true }]),
        new(OtherTextId, "Beta", [new TextLine(1, [Word("mtoto", null)])
            { ParagraphId = OtherParagraphId, SegmentId = OtherSegmentId, ParseIsCurrent = true }]),
    ];

    private static void AssertCapturedAcceptance(FakeCommandClient client, IReadOnlyList<string> forms,
        IReadOnlyList<Guid> textIds)
    {
        var expected = client.ReaderOwner.Summary!.Words.Where(word => forms.Contains(word.Key.Form)).SelectMany(word =>
            (word.Actions.Classification?.Readings ?? []).Where(reading => reading.IsParserOnly)
                .Select(reading => (Form: word.Key.Form, ReadingIndex: (int?)reading.Index)))
            .OrderBy(item => item.Form).ThenBy(item => item.ReadingIndex).ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, client.PendingPutRequests.Select(request =>
            (request.Change.Word, request.Change.ReadingIndex)).OrderBy(item => item.Word)
            .ThenBy(item => item.ReadingIndex));
        Assert.Single(client.PendingPutRequests.Select(request => request.Change.GroupId).Distinct());
        Assert.All(client.PendingPutRequests, request =>
        {
            Assert.Equal(ChangeKinds.AddCandidate, request.Change.Kind);
            Assert.Equal("assessment/one", request.Change.AssessmentId);
            Assert.NotNull(request.Change.GroupId);
            Assert.Equal(textIds, request.ExpectedContext!.TextIds);
        });
    }

    [Fact]
    public void AcceptNewSetEligibilityUsesTheSharedWordClassification()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var completeByMarking = Result("kitabu", Book, Child);
            var cappedParserMetadata = completeByMarking with
            {
                Morphology = completeByMarking.Morphology! with { Capped = true },
            };
            var words = new[]
            {
                cappedParserMetadata,
                Result("anapenda", Like),
                Result("mtoto", Child),
                Result("zzz"),
            };
            var (inText, _, _) = await Loaded(assessmentWords: words);
            inText.Changes.AssessmentId = "assessment/one";

            Assert.Equal(AnalysisMarkingClass.Extra,
                SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                    .First(token => token.Form == "kitabu").Marking.PanGlossClass);
            Assert.True(inText.AcceptNewSetCommand.CanExecute(AnalysisOperationScope.SelectedText));
            Assert.True(inText.AcceptNewSetCommand.CanExecute(AnalysisOperationScope.AssessmentSelection));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void RemovingAnalysesForTheSelectedTextUsesTheRealTextScope()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            client.AnalysisRemovalResponse = new PendingChangesSnapshot(null, "removed", [], []);

            await inText.RemoveAnalysesCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

            Assert.Equal(new[] { Stored(Book, "book").StoredAnalysisId, Stored(Love, "love").StoredAnalysisId }.Order(),
                client.PendingPutRequests.Select(request => request.Change.StoredAnalysisId).Order());
            Assert.All(client.PendingPutRequests, request =>
            {
                Assert.Equal(ChangeKinds.RemoveAnalysis, request.Change.Kind);
                Assert.Equal([TextId], request.ExpectedContext!.TextIds);
            });
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void MarkingSpellingsIncorrectForTheSelectedTextStagesEachWordformOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var expected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens).Where(token => token.IsWord)
                .Select(token => token.Form).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

            await inText.MarkSpellingsIncorrectCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

            Assert.Equal(expected, client.PendingPutRequests.Select(request => request.Change.Word)
                .Order(StringComparer.Ordinal));
            Assert.All(client.PendingPutRequests, request => Assert.Equal(ChangeKinds.IncorrectSpelling,
                request.Change.Kind));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void CheckedOccurrencesExposeASelectionAndRemoveEachStoredAnalysisOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var occurrences = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Where(token => token.Form == "kitabu").ToArray();
            foreach (var token in occurrences) token.IsSelectedForActions = true;
            client.AnalysisRemovalResponse = new PendingChangesSnapshot(null, "removed", [], []);

            Assert.Equal(2, inText.CheckedWordCount);
            Assert.True(inText.HasCheckedWords);
            await inText.RemoveAnalysesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

            var request = Assert.Single(client.PendingPutRequests);
            Assert.Equal(ChangeKinds.RemoveAnalysis, request.Change.Kind);
            Assert.Equal(Stored(Book, "book").StoredAnalysisId, request.Change.StoredAnalysisId);
            Assert.Equal(Wordform("kitabu"), request.Change.WordformId);
            Assert.NotNull(request.ExpectedContext);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void RemovingAnalysesForTheSelectionUsesEveryDistinctStoredAnalysisId()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            client.AnalysisRemovalResponse = new PendingChangesSnapshot(null, "removed", [], []);
            var expected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .SelectMany(token => token.Marking.FieldWorksAnalyses).Select(analysis => analysis.StoredAnalysisId)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

            await inText.RemoveAnalysesCommand.ExecuteAsync(AnalysisOperationScope.ChosenTexts);

            Assert.Equal(expected, client.PendingPutRequests.Select(request => request.Change.StoredAnalysisId)
                .Order(StringComparer.Ordinal));
            Assert.All(client.PendingPutRequests, request => Assert.Equal(ChangeKinds.RemoveAnalysis, request.Change.Kind));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void RemovingAStoredAnalysisStagesTheExactAnalysis()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(candidate => candidate.Form == "kitabu");
            inText.SelectToken(token);
            var stored = Assert.Single(token.Marking.FieldWorksAnalyses);
            var remove = Assert.Single(token.Marking.FixChoices, choice =>
                choice.Kind == AnalysisMarkingActionKind.RemoveAnalysis &&
                choice.StoredAnalysisId == stored.StoredAnalysisId);

            await inText.StageMarkingChoiceCommand.ExecuteAsync(remove);

            var request = Assert.Single(client.PendingPutRequests);
            Assert.Equal(stored.StoredAnalysisId, request.Change.StoredAnalysisId);
            Assert.Equal(token.Form, request.Change.Word);
            Assert.Equal(CanonicalId.FromGuid(token.WordformId!.Value).Value, request.Change.WordformId);
            Assert.Same(token.CaptureActionTarget(null).ExpectedContext, request.ExpectedContext);
            Assert.Equal("remove-analysis", Assert.Single(inText.Changes.Snapshot.Changes).Kind);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ARefusedAnalysisRemovalLeavesTheWordUnread()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(candidate => candidate.Form == "kitabu");
            inText.SelectToken(token);
            var removal = Assert.Single(token.Marking.FixChoices, choice =>
                choice.Kind == AnalysisMarkingActionKind.RemoveAnalysis);
            client.PendingPutHandler = (_, _) => Task.FromResult(
                CommandOutcome<PendingChangesSnapshot>.Refused(new Refusal("remove.refused",
                    FailureReason.InvalidArgument, "The stored analysis changed.")));

            await inText.StageMarkingChoiceCommand.ExecuteAsync(removal);

            Assert.True(token.Marking.IsUnread);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ARefusedAcceptNewSetLeavesTheWordUnread()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(candidate => candidate.Marking.FixChoices.Any(choice =>
                    choice.Kind == AnalysisMarkingActionKind.AcceptNewSet));
            inText.SelectToken(token);
            inText.Changes.AssessmentId = "assessment/one";
            var accept = Assert.Single(token.Marking.FixChoices, choice =>
                choice.Kind == AnalysisMarkingActionKind.AcceptNewSet);
            client.PendingPutHandler = (_, _) => Task.FromResult(
                CommandOutcome<PendingChangesSnapshot>.Refused(new Refusal("accept.refused",
                    FailureReason.InvalidArgument, "The Assessment is no longer current.")));

            await inText.StageMarkingChoiceCommand.ExecuteAsync(accept);

            Assert.True(token.Marking.IsUnread);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TokensUseTheirOwnHomographsProjectFacts(bool reverse)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var approved = Stored(Book, "book");
            var disapproved = Stored(Love, "love") with { StoredAnalysisOpinion = ReadingGrade.Disapproved };
            var ownId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var otherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
            var tokens = new[] { Word("kitabu", approved, 0),
                Word("kitabu", disapproved, 1) with { WordformId = otherId, IncorrectSpelling = true } };
            var words = new[]
            {
                new TextWord("kitabu", ownId.ToString("D"),
                    [new WordOccurrence(TextId, "Alpha", 1, "kitabu kitabu", "approved", approved)], [approved], [])
                    { Analyses = [approved] },
                new TextWord("kitabu", otherId.ToString("D"),
                    [new WordOccurrence(TextId, "Alpha", 1, "kitabu kitabu", "unapproved", disapproved)], [],
                    [disapproved], IncorrectSpelling: true) { Analyses = [disapproved] },
            };
            if (reverse) { Array.Reverse(tokens); Array.Reverse(words); }
            var (inText, _, _) = await Loaded(sourceLines:
                [new TextLine(1, tokens) { ParagraphId = ParagraphId, SegmentId = SegmentId, ParseIsCurrent = true }],
                projectWords: words);

            Assert.True(inText.HasLines);
            var displayed = SelectionModelFixture.VisibleLines(inText)[0].Tokens;
            var own = Assert.Single(displayed, token => token.WordformId == ownId);
            var other = Assert.Single(displayed, token => token.WordformId == otherId);
            Assert.Equal("Approved", own.ProjectStatusLabel);
            Assert.Single(own.ProjectApprovedAnalyses);
            Assert.Equal("book", own.ProjectSummary);
            Assert.Equal("Incorrect spelling", other.ProjectStatusLabel);
            Assert.Empty(other.ProjectApprovedAnalyses);
            Assert.Equal("FieldWorks marks this spelling as incorrect", other.ProjectSummary);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ProjectFactsFallBackToWordformIdentityWhenTheTextSpellingDiffers()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var ownId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var stored = Stored(Book, "book");
            var token = Word("kitabu", stored) with { WordformId = ownId };
            var projectWord = new TextWord("alternate spelling", ownId.ToString("D"),
                [new WordOccurrence(TextId, "Alpha", 1, "kitabu", "approved", stored)], [stored], [])
            {
                Analyses = [stored],
            };
            var (inText, _, _) = await Loaded(sourceLines:
                [new TextLine(1, [token]) { ParagraphId = ParagraphId, SegmentId = SegmentId, ParseIsCurrent = true }],
                projectWords: [projectWord]);

            var shown = Assert.Single(SelectionModelFixture.VisibleLines(inText)[0].Tokens);

            Assert.Equal("Approved", shown.ProjectStatusLabel);
            Assert.Equal("book", shown.ProjectSummary);
            Assert.Single(shown.ProjectApprovedAnalyses);
            Assert.True(shown.HasFieldWorksAnalyses);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void EvidenceRefreshKeepsTheSelectedTextIdentityWhenTitlesAreEqual()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            Func<Task>? reload = null;
            var (inText, _, client) = await Loaded(sourceTexts:
                [new TextLines(TextId, "Same title", []), new TextLines(OtherTextId, "Same title", [])],
                captureReload: action => reload = action);
            inText.SelectedText = inText.Texts[1];
            await _fixtures.Last().ReplaceSourceAsync(new TextWordsResponse([],
                [new TextLines(TextId, "Same title", []), new TextLines(OtherTextId, "Renamed title", [])], true));
            await reload!();

            Assert.Equal(OtherTextId, inText.SelectedText!.TextId);
            Assert.Equal("Renamed title", inText.SelectedText.Title);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AssessmentRefreshKeepsTheSecondTextWithTheSameTitleSelected()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            AssessViewModel? assess = null;
            var (inText, _, _) = await Loaded(sourceTexts:
                [new TextLines(TextId, "Same title", []), new TextLines(OtherTextId, "Same title", [])],
                captureAssess: value => assess = value);
            inText.SelectedText = inText.Texts[1];

            assess!.Restore(new WorkspaceEvidence(assess.Result! with { SummaryMarkdown = "new evidence" }, null, false));

            Assert.Equal(OtherTextId, inText.SelectedText!.TextId);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void EvidenceRefreshFallsBackOnlyWhenTheSelectedTextDisappears()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            Func<Task>? reload = null;
            var (inText, _, client) = await Loaded(sourceTexts:
                [new TextLines(TextId, "Alpha", []), new TextLines(OtherTextId, "Beta", [])],
                captureReload: action => reload = action);
            inText.SelectedText = inText.Texts[1];
            await _fixtures.Last().ReplaceSourceAsync(new TextWordsResponse([], [new TextLines(TextId, "Alpha", [])], true));

            await reload!();

            Assert.Equal(TextId, inText.SelectedText!.TextId);
            await _fixtures.Last().ReplaceSourceAsync(new TextWordsResponse([], [], true));
            await reload();
            Assert.Null(inText.SelectedText);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AWordCardWithoutAnOccurrenceDoesNotBorrowAHomographsProjectFacts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var approved = Stored(Book, "book");
            var ownId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
            var (inText, _, _) = await Loaded(sourceTexts: [], projectWords:
                [new TextWord("kitabu", ownId.ToString("D"), [], [approved], []) { Analyses = [approved] }]);

            inText.SelectWord("kitabu");
            await inText.SelectionRefresh;

            Assert.Null(inText.SelectedToken!.WordformId);
            Assert.Empty(inText.SelectedToken.ProjectApprovedAnalyses);
            Assert.Equal("Not analysed in the project", inText.SelectedToken.ProjectSummary);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public void WordformWidePendingMarkersExcludeSameSpellingDifferentWordforms(string kind)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await HomographsWithPendingChoice(kind);
            var tokens = SelectionModelFixture.VisibleLines(inText)[0].Tokens;

            Assert.False(tokens[0].IsPending);
            Assert.True(tokens[1].IsPending);
            Assert.True(tokens[2].IsPending);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public void UndoCheckedWordsCannotRemoveAnotherWordformsSameSpellingChoice(string kind)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await HomographsWithPendingChoice(kind);
            SelectionModelFixture.VisibleLines(inText)[0].Tokens[0].IsSelectedForActions = true;

            await inText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

            Assert.Empty(client.PendingRemoveRequests);
            Assert.Single(inText.Changes.Items);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public void UndoEnablementRequiresThePendingWordformIdentity(string kind)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await HomographsWithPendingChoice(kind);
            var tokens = SelectionModelFixture.VisibleLines(inText)[0].Tokens;
            tokens[0].IsSelectedForActions = true;
            Assert.False(inText.UndoChangesCommand.CanExecute(AnalysisOperationScope.CheckedWords));
            tokens[0].IsSelectedForActions = false;
            tokens[2].IsSelectedForActions = true;
            Assert.True(inText.UndoChangesCommand.CanExecute(AnalysisOperationScope.CheckedWords));

            await inText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

            Assert.Equal("homograph-choice", Assert.Single(client.PendingRemoveRequests).ChangeId);
            Assert.All(tokens, token => Assert.False(token.IsPending));
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(AnalysisOperationScope.CheckedWords)]
    [InlineData(AnalysisOperationScope.SelectedText)]
    public void ScopedUndoDoesNotExpandAnAcceptedGroup(AnalysisOperationScope scope)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var otherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
            var pending = new PendingChangesSnapshot("draft/group", "revision/group",
                [new PendingChange("own", Wordform("kitabu"), "kitabu", ChangeKinds.AddCandidate, null, null, [])
                    { GroupId = "accepted-group" },
                 new PendingChange("other", CanonicalId.FromGuid(otherId).Value, "kitabu", ChangeKinds.AddCandidate,
                     null, null, []) { GroupId = "accepted-group" }], []);
            var (inText, _, client) = await Loaded(pending: pending, sourceTexts:
                [new TextLines(TextId, "Alpha", [new TextLine(1, [Word("kitabu", null)])
                    { ParagraphId = ParagraphId, SegmentId = SegmentId }]),
                 new TextLines(OtherTextId, "Beta", [new TextLine(1,
                     [Word("kitabu", null) with { WordformId = otherId }])
                    { ParagraphId = OtherParagraphId, SegmentId = OtherSegmentId }])]);
            SelectionModelFixture.VisibleLines(inText)[0].Tokens[0].IsSelectedForActions = true;

            await inText.UndoChangesCommand.ExecuteAsync(scope);

            Assert.Equal("own", Assert.Single(client.PendingRemoveRequests).ChangeId);
            Assert.Equal("other", Assert.Single(inText.Changes.Items).ChangeId);
            Assert.False(SelectionModelFixture.VisibleLines(inText)[0].Tokens[0].IsPending);
            var other = await inText.ReadOccurrenceAsync(new OccurrenceAnchor(OtherTextId, OtherParagraphId, OtherSegmentId, 0));
            Assert.NotNull(other);
            Assert.True(other.IsPending);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public void RefusedUndoRetainsOnlyTheAddressedHomographsMarkers(string kind)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await HomographsWithPendingChoice(kind);
            client.PendingRemoveHandler = (_, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(
                new Refusal("change.cannot-compose", FailureReason.Refused, "Removal refused.")));
            SelectionModelFixture.VisibleLines(inText)[0].Tokens[1].IsSelectedForActions = true;

            await inText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

            Assert.Single(inText.Changes.Items);
            Assert.False(SelectionModelFixture.VisibleLines(inText)[0].Tokens[0].IsPending);
            Assert.All(SelectionModelFixture.VisibleLines(inText)[0].Tokens.Skip(1), token =>
            {
                Assert.True(token.IsPending);
                Assert.Single(token.StagedChanges);
            });
        }, TimeSpan.FromSeconds(180));
    }

    private Task<(ResultsInTextViewModel InText, List<string> Shown, FakeCommandClient Client)>
        HomographsWithPendingChoice(string kind)
    {
        var otherId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000099");
        var pending = new PendingChangesSnapshot("draft/homographs", "revision/homographs",
            [new PendingChange("homograph-choice", CanonicalId.FromGuid(otherId).Value, "kitabu", kind,
                null, null, [])], []);
        return Loaded(pending: pending, sourceLines:
        [new TextLine(1, [Word("kitabu", null, 0),
            Word("kitabu", null, 1) with { WordformId = otherId },
            Word("kitabu", null, 2) with { WordformId = otherId }])
            { ParagraphId = ParagraphId, SegmentId = SegmentId, ParseIsCurrent = true }]);
    }

    [Fact]
    public void UndoInASelectedTextLeavesTheSameWordInAnotherTextStaged()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            const string selectedChangeId = "selected-text-change";
            const string otherChangeId = "other-text-change";
            var selectedOccurrence = new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0);
            var otherOccurrence = new OccurrenceAnchor(OtherTextId, OtherParagraphId, OtherSegmentId, 0);
            var pending = new PendingChangesSnapshot("draft/scoped-undo", "revision/scoped-undo",
                [new PendingChange(selectedChangeId, Wordform("kitabu"), "kitabu", ChangeKinds.Approve,
                        null, null, []),
                 new PendingChange(otherChangeId, Wordform("kitabu"), "kitabu", ChangeKinds.Approve,
                        null, null, [])],
                [new ChangeFit(selectedChangeId, ChangeFitStatus.Uncertain, []) { Occurrence = selectedOccurrence },
                 new ChangeFit(otherChangeId, ChangeFitStatus.Uncertain, []) { Occurrence = otherOccurrence }]);
            var selectedLine = new TextLine(1, [Word("kitabu", Stored(Book, "book"))])
                { ParagraphId = ParagraphId, SegmentId = SegmentId, ParseIsCurrent = true };
            var otherLine = new TextLine(1, [Word("kitabu", Stored(Book, "book"))])
                { ParagraphId = OtherParagraphId, SegmentId = OtherSegmentId, ParseIsCurrent = true };
            var (inText, _, client) = await Loaded(pending: pending, sourceTexts:
            [
                new TextLines(TextId, "Alpha", [selectedLine]),
                new TextLines(OtherTextId, "Beta", [otherLine]),
            ]);

            await inText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

            Assert.Equal([selectedChangeId], client.PendingRemoveRequests.Select(request => request.ChangeId));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AClickedWordOpensInTheWordsView_OnlyWhenAsked()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, shown, _) = await Loaded();
            var anapenda = SelectionModelFixture.VisibleLines(inText)[0].Tokens[1];

            inText.SelectToken(anapenda);
            Assert.Equal(anapenda.Occurrence, inText.SelectedToken!.Occurrence);
            Assert.Empty(shown);

            inText.ShowInWordsCommand.Execute(null);
            Assert.Equal(["anapenda"], shown);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void OpinionsNeedAnExplicitReadingAndMarkTheSelectedOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var tokens = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Where(token => token.Form == "kitabu").ToArray();
            var selected = tokens[0];
            inText.SelectToken(selected);

            Assert.Null(selected.SelectedReading);
            Assert.False(inText.AddChangeCommand.CanExecute(ChangeKinds.Approve));

            inText.SelectedToken!.SelectedReading = inText.SelectedToken.Readings[0];
            Assert.True(inText.AddChangeCommand.CanExecute(ChangeKinds.Approve));
            await inText.AddChangeCommand.ExecuteAsync(ChangeKinds.Approve);

            Assert.Equal(ChangeKinds.Approve, Assert.Single(inText.Changes.Items).Kind);
            Assert.True(tokens[0].IsPending);
            Assert.Equal("Not applied yet", tokens[0].PendingChangeStatus);
            Assert.Equal(PendingChangeState.NotAppliedYet, tokens[0].PendingState);
            Assert.False(tokens[1].IsPending);
            Assert.Equal(PendingChangeState.None, tokens[1].PendingState);
            Assert.False(SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(token => token.Form == "anapenda").IsPending);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AnalyzeTextsPassesTheSelectedOccurrenceWithAnOpinionChange()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(candidate => candidate.Form == "kitabu");
            inText.SelectToken(token);
            inText.SelectedToken!.SelectedReading = inText.SelectedToken.Readings[0];

            await inText.AddChangeCommand.ExecuteAsync(ChangeKinds.Approve);

            var change = Assert.Single(client.PendingPutRequests).Change;
            Assert.Equal(ChangeKinds.Approve, change.Kind);
            Assert.Equal(new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0), change.Occurrence);
            Assert.Equal(CanonicalId.FromGuid(token.WordformId!.Value).Value, change.WordformId);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AnalyzeTextsSendsTheSegmentIndexWhenPunctuationPrecedesARepeatedWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var line = new TextLine(1,
            [
                new TextToken(",", null, null, null),
                Word("kitabu", Stored(Book, "book"), 1),
                Word("kitabu", Stored(Book, "book"), 2),
                new TextToken(".", null, null, null),
            ])
            {
                ParagraphId = ParagraphId,
                SegmentId = SegmentId,
                ParseIsCurrent = true,
            };
            var (inText, _, client) = await Loaded(sourceLines: [line]);
            var selected = SelectionModelFixture.VisibleLines(inText).SelectMany(item => item.Tokens)
                .Single(token => token.OccurrenceIndex == 2);
            inText.SelectToken(selected);
            inText.SelectedToken!.SelectedReading = inText.SelectedToken.Readings[0];

            await inText.AddChangeCommand.ExecuteAsync(ChangeKinds.Reject);

            var change = Assert.Single(client.PendingPutRequests).Change;
            Assert.Equal(CanonicalId.FromGuid(selected.WordformId!.Value).Value, change.WordformId);
            Assert.Equal(new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 2), change.Occurrence);
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(ChangeKinds.Reject, true)]
    [InlineData(ChangeKinds.Candidate, true)]
    [InlineData(ChangeKinds.AddCandidate, false)]
    public void AnalyzeTextsAnchorsOpinionChangesButNotCandidateAdds(string kind, bool hasAnchor)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, client) = await Loaded();
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(candidate => candidate.Form == "kitabu");
            inText.SelectToken(token);
            inText.SelectedToken!.SelectedReading = inText.SelectedToken.Readings[0];

            await inText.AddChangeCommand.ExecuteAsync(kind);

            Assert.NotEmpty(client.PendingPutRequests);
            Assert.All(client.PendingPutRequests,
                request => Assert.Equal(hasAnchor, request.Change.Occurrence is not null));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AnUncertainChangeExposesItsOccurrenceAnchor()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var anchor = new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 1);
            var fit = new ChangeFit("uncertain", ChangeFitStatus.Uncertain, [])
            {
                Uncertainty = new ChangeUncertainty("The words in the source sentence have changed.", [], []),
            };
            var occurrence = typeof(ChangeFit).GetProperty("Occurrence");
            Assert.NotNull(occurrence);
            occurrence.SetValue(fit, anchor);
            var (inText, _, _) = await Loaded(new PendingChangesSnapshot("draft/uncertain", "revision/uncertain",
                [new PendingChange("uncertain", Wordform("kitabu"), "kitabu", ChangeKinds.Approve, null, null, [])],
                [fit]));

            var change = Assert.Single(inText.Changes.Items);
            var changeOccurrence = typeof(ChangeViewModel).GetProperty("Occurrence");
            Assert.NotNull(changeOccurrence);
            Assert.Equal(anchor, changeOccurrence.GetValue(change));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AWordWithAnUncertainChangeShowsTheCheckAgainLabel()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fit = new ChangeFit("uncertain", ChangeFitStatus.Uncertain, ["The sentence changed."])
            {
                Occurrence = new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0),
                Uncertainty = new ChangeUncertainty("The sentence changed.",
                    [new OccurrenceWordToken(0, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011")).Value, "kitabu"),
                     new OccurrenceWordToken(1, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000012")).Value, "old"),
                     new OccurrenceWordToken(2, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000013")).Value, "mtoto"),
                     new OccurrenceWordToken(3, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000014")).Value, "zzz")],
                    [new OccurrenceWordToken(0, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011")).Value, "kitabu"),
                     new OccurrenceWordToken(1, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000012")).Value, "anapenda"),
                     new OccurrenceWordToken(2, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000013")).Value, "mtoto"),
                     new OccurrenceWordToken(3, CanonicalId.FromGuid(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000014")).Value, "zzz")]),
            };
            var (inText, _, _) = await Loaded(new PendingChangesSnapshot("draft/uncertain", "revision/uncertain",
                [new PendingChange("uncertain", Wordform("kitabu"), "kitabu", ChangeKinds.Approve, null, null, [])],
                [fit]));
            var token = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(candidate => candidate.Form == "anapenda");

            Assert.Equal("Needs another look", SelectionModelFixture.VisibleLines(inText)
                .SelectMany(line => line.Tokens).First(candidate => candidate.Form == "kitabu").PendingChangeStatus);
            Assert.True(token.IsUncertainChanged);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void UncertainHighlightUsesItsAnchoredSegmentAndOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var first = new TextLine(1,
            [
                Word("kitabu", Stored(Book, "book"), 0),
                Word("anapenda", Stored(Love, "love"), 1),
                Word("mtoto", null, 2),
                Word("zzz", null, 3),
            ])
            {
                ParagraphId = ParagraphId,
                SegmentId = SegmentId,
                ParseIsCurrent = true,
            };
            var duplicate = new TextLine(2,
            [
                Word("kitabu", Stored(Book, "book"), 0),
                Word("anapenda", Stored(Love, "love"), 1),
                Word("mtoto", null, 2),
                Word("zzz", null, 3),
            ])
            {
                ParagraphId = ParagraphId,
                SegmentId = SecondSegmentId,
                ParseIsCurrent = true,
            };
            var before = new[]
            {
                new OccurrenceWordToken(0, Wordform("kitabu"), "kitabu"),
                new OccurrenceWordToken(1, Wordform("anapenda"), "old"),
                new OccurrenceWordToken(2, Wordform("mtoto"), "mtoto"),
                new OccurrenceWordToken(3, Wordform("zzz"), "zzz"),
            };
            var after = new[]
            {
                new OccurrenceWordToken(0, Wordform("kitabu"), "kitabu"),
                new OccurrenceWordToken(1, Wordform("anapenda"), "anapenda"),
                new OccurrenceWordToken(2, Wordform("mtoto"), "mtoto"),
                new OccurrenceWordToken(3, Wordform("zzz"), "zzz"),
            };
            var fit = new ChangeFit("uncertain", ChangeFitStatus.Uncertain, [])
            {
                Uncertainty = new ChangeUncertainty("The words in the source sentence have changed.", before, after),
            };
            typeof(ChangeFit).GetProperty("Occurrence")!.SetValue(fit,
                new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0));
            var (inText, _, _) = await Loaded(new PendingChangesSnapshot("draft/uncertain", "revision/uncertain",
                [new PendingChange("uncertain", Wordform("kitabu"), "kitabu", ChangeKinds.Approve, null, null, [])],
                [fit]), [first, duplicate]);

            var lines = SelectionModelFixture.VisibleLines(inText).ToArray();
            Assert.True(lines[0].Tokens[1].IsUncertainChanged);
            Assert.False(lines[1].Tokens[1].IsUncertainChanged);
        }, TimeSpan.FromSeconds(180));
    }

    private static string Wordform(string form) => CanonicalId.FromGuid(form switch
    {
        "kitabu" => Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011"),
        "anapenda" => Guid.Parse("aaaaaaaa-0000-0000-0000-000000000012"),
        "mtoto" => Guid.Parse("aaaaaaaa-0000-0000-0000-000000000013"),
        "zzz" => Guid.Parse("aaaaaaaa-0000-0000-0000-000000000014"),
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    }).Value;
    [Fact]
    public void AStoredAnalysisChoiceStagesItsExactOpinionAtTheSelectedOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            inText.Changes.AssessmentId = "assessment/one";
            var tokens = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Where(token => token.Form == "kitabu").ToArray();
            var selected = tokens[0];
            inText.SelectToken(selected);
            var choice = Assert.Single(selected.Marking.FixChoices,
                candidate => candidate.Kind == AnalysisMarkingActionKind.Disapprove && candidate.StoredAnalysisId is not null);

            await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);

            var request = Assert.Single(fake.PendingPutRequests);
            Assert.Equal(ChangeKinds.Reject, request.Change.Kind);
            Assert.Equal(CanonicalId.FromGuid(selected.WordformId!.Value).Value, request.Change.WordformId);
            Assert.Equal(selected.Marking.FieldWorksAnalyses[0].StoredAnalysisId, request.Change.StoredAnalysisId);
            Assert.Equal(selected.Occurrence, request.Change.Occurrence);
            Assert.Null(request.Change.AssessmentId);
            Assert.Equal("assessment/one", request.ExpectedContext!.SelectionEvidence!.RootAssessmentId);
            Assert.Single(selected.Marking.StagedTransitions);
            Assert.Empty(tokens[1].Marking.StagedTransitions);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AParserOnlyReadingCanBeDisapprovedAtTheSelectedOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            inText.Changes.AssessmentId = "assessment/one";
            var selected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(token => token.Form == "kitabu");
            inText.SelectToken(selected);
            var choice = Assert.Single(selected.Marking.FixChoices,
                candidate => candidate.Kind == AnalysisMarkingActionKind.Disapprove && candidate.ReadingIndex == 1);

            await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);

            var request = Assert.Single(fake.PendingPutRequests);
            Assert.Equal(ChangeKinds.Reject, request.Change.Kind);
            Assert.Null(request.Change.StoredAnalysisId);
            Assert.Equal(1, request.Change.ReadingIndex);
            Assert.Equal("assessment/one", request.Change.AssessmentId);
            Assert.Equal(selected.Occurrence, request.Change.Occurrence);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void KeepFieldWorksIsANamedNoOp()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var selected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .First(token => token.Form == "kitabu");
            inText.SelectToken(selected);

            await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

            Assert.Empty(fake.PendingPutRequests);
            Assert.Empty(selected.Marking.StagedTransitions);
            Assert.False(selected.Marking.IsUnread);
            Assert.Contains(fake.ReadWordStateRequests, request => request.IsRead == true &&
                request.Occurrences?.SequenceEqual([selected.Occurrence!]) == true);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void StagedChangesRemainKeyedPerReadingAndOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, _) = await Loaded();
            var tokens = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Where(token => token.Form == "kitabu").ToArray();
            var selected = tokens[0];
            inText.Changes.Items.Add(new ChangeViewModel(ChangeKinds.Reject, "kitabu", "book",
                occurrence: selected.Occurrence, storedAnalysisId: "stored-book", wordformId: Wordform("kitabu")));
            inText.Changes.Items.Add(new ChangeViewModel(ChangeKinds.AddCandidate, "kitabu", "extra reading",
                readingIndex: 1, wordformId: Wordform("kitabu")));

            Assert.Equal(2, selected.Marking.StagedTransitions.Count);
            Assert.Contains(selected.Marking.StagedTransitions,
                transition => transition.StoredAnalysisId == "stored-book");
            Assert.Contains(selected.Marking.StagedTransitions, transition => transition.ReadingIndex == 1);
            Assert.True(selected.Marking.IsUnread);
            Assert.False(selected.Marking.NeedsALook);
            Assert.Single(tokens[1].Marking.StagedTransitions,
                transition => transition.ReadingIndex == 1);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AParserOnlyReadingCanBeAddedAsApproved()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            inText.Changes.AssessmentId = "assessment/one";
            var selected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(token => token.Form == "mtoto");
            inText.SelectToken(selected);
            var choice = Assert.Single(selected.Marking.FixChoices,
                candidate => candidate.Label == "Add as Approved" && candidate.ReadingIndex == 0);

            await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);

            var request = Assert.Single(fake.PendingPutRequests);
            Assert.Equal(ChangeKinds.Approve, request.Change.Kind);
            Assert.Equal(CanonicalId.FromGuid(selected.WordformId!.Value).Value, request.Change.WordformId);
            Assert.Equal("assessment/one", request.Change.AssessmentId);
            Assert.Equal(0, request.Change.ReadingIndex);
            Assert.Equal(selected.Occurrence, request.Change.Occurrence);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AParserOnlyReadingCanBeAddedAsUnknown()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var selected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(token => token.Form == "mtoto");
            inText.SelectToken(selected);
            var choice = Assert.Single(selected.Marking.FixChoices,
                candidate => candidate.Kind == AnalysisMarkingActionKind.Add && candidate.Label == "Add as Unknown");

            await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);

            var request = Assert.Single(fake.PendingPutRequests);
            Assert.Equal(ChangeKinds.AddCandidate, request.Change.Kind);
            Assert.Equal(CanonicalId.FromGuid(selected.WordformId!.Value).Value, request.Change.WordformId);
            Assert.Equal("assessment/one", request.Change.AssessmentId);
            Assert.Equal(0, request.Change.ReadingIndex);
            Assert.Null(request.Change.Occurrence);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void PrimaryAddUsesTheProducingAssessmentWithoutADefaultAssessment()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var selected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(token => token.Form == "mtoto");
            inText.SelectToken(selected);

            await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

            var request = Assert.Single(fake.PendingPutRequests);
            Assert.Equal(ChangeKinds.AddCandidate, request.Change.Kind);
            Assert.Equal("Unknown", selected.Marking.PrimaryAction!.AfterApply);
            Assert.Equal("assessment/one", request.Change.AssessmentId);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ARefusedMarkingStageLeavesTheOccurrenceUnread()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (inText, _, fake) = await Loaded();
            var selected = SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens)
                .Single(token => token.Form == "mtoto");
            inText.SelectToken(selected);
            fake.PendingPutRefusal = new Refusal("pending-change-invalid", FailureReason.InvalidArgument,
                "The proposed change was refused.");

            await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

            Assert.NotEmpty(fake.PendingPutRequests);
            Assert.True(selected.Marking.IsUnread);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void ProjectStatusChipUsesTheWordsOpinionMark()
    {
        var analysis = new ProjectAnalysis("k1", [new ParserReadingMorph("form", "gloss", "n", null, false, null)]);
        var cases = new[]
        {
            (new TextWord("approved", null, [], [analysis], []), Mark.Approved),
            (new TextWord("candidate", null, [], [], [], CandidateCount: 1), Mark.Unknown),
            (new TextWord("rejected", null, [], [], [analysis]), Mark.Disapproved),
            (new TextWord("incorrect", null, [], [], [], IncorrectSpelling: true), null),
            (new TextWord("new", null, [], [], []), Mark.NotInFieldWorks),
        };

        foreach (var (word, expected) in cases)
        {
            var stored = word.Approved.Select(item => item with { StoredAnalysisOpinion = ReadingGrade.Approved })
                .Concat(word.Disapproved.Select(item => item with { StoredAnalysisOpinion = ReadingGrade.Disapproved }))
                .Concat(Enumerable.Range(0, word.CandidateCount).Select(_ =>
                    analysis with { StoredAnalysisOpinion = ReadingGrade.Candidate })).ToArray();
            var token = new ResultsTokenViewModel(word.Form, 1,
                new TextToken(word.Form, word.Form, null, "unanalysed")
                { StoredAnalyses = stored, IncorrectSpelling = word.IncorrectSpelling }, null);

            Assert.Equal(expected, token.ProjectStatusMark);
        }
    }

    [Fact]
    public void UnavailableMarkAsUnreadActionExplainsThatItNeedsATextOccurrence()
    {
        var token = new ResultsTokenViewModel("Alpha", 1, Word("mtoto", null), null);
        var reason = typeof(ResultsTokenViewModel).GetProperty("MarkUnreadDisabledReason")?.GetValue(token);

        Assert.Equal("Choose a word occurrence in Analyze texts first.", reason);
    }

    [Fact]
    public void BeforeAnyAssessmentItLeavesTheEmptyStateToTheSharedParsePrompt()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var inText = new ResultsInTextViewModel(
            new TextWordsViewModel(fake, selection, fake.ReaderOwner), new AssessViewModel(fake, selection), _ => { }, _ => { },
            new ChangesViewModel(fake), fake, fake.ReaderOwner);

        Assert.Null(inText.Message);
        Assert.False(inText.HasMessage);
        Assert.False(inText.HasAssessment);
        Assert.False(inText.HasResults);
        Assert.Empty(SelectionModelFixture.VisibleLines(inText));
    }

    [Fact]
    public void BeforeTheFirstParseTheReaderShowsTheTextWithItsFieldWorksLine()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var selection = new SelectionViewModel(fake);
            var texts = new TextWordsViewModel(fake, selection, fake.ReaderOwner);
            fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
            var inText = new ResultsInTextViewModel(texts, new AssessViewModel(fake, selection), _ => { }, _ => { },
                new ChangesViewModel(fake), fake, fake.ReaderOwner);
            var fixture = new SelectionModelFixture(fake);
            _fixtures.Add(fixture);
            await fixture.PublishAsync(new TextWordsResponse([], [new TextLines(TextId, "Alpha",
                [new TextLine(1, [Word("kitabu", Stored(Book, "book"))])])], HasBaseline: true));
            await SelectionModelFixture.RealizeAsync(inText);

            await texts.SetProjectAsync(ProjectPath);

            Assert.True(inText.HasTexts);
            Assert.False(inText.HasResults);
            Assert.True(inText.HasLines);
            Assert.Equal("Alpha", Assert.Single(inText.Texts).Title);
            var word = Assert.Single(Assert.Single(SelectionModelFixture.VisibleLines(inText)).Tokens);
            Assert.True(word.HasFieldWorksAnalyses);
            Assert.Equal("Not parsed yet", word.PanGlossSummary);
            Assert.False(word.ShowUnread);
            Assert.False(word.HasPrimaryAction);
            Assert.Null(inText.Message);
            Assert.Empty(fake.ReadWordStateRequests);
        }, TimeSpan.FromSeconds(180));
    }
}
