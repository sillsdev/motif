using System.Collections.Specialized;
using Avalonia.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
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
public sealed class ResultsInTextViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
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
            StoredAnalysisId = "stored-" + reading.Morphs[0].Form,
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

    private static async Task<(ResultsInTextViewModel InText, List<string> Shown, FakeCommandClient Client)> Loaded(
        PendingChangesSnapshot? pending = null, IReadOnlyList<TextLine>? sourceLines = null,
        IReadOnlyList<TextLines>? sourceTexts = null,
        IReadOnlyList<OccurrenceAnchor>? readOccurrences = null,
        Func<WordReadStateRequest, CancellationToken, Task<CommandOutcome<WordReadStateResponse>>>? readStateHandler = null,
        bool waitForReadState = true,
        Func<ResultsInTextViewModel, ChangesViewModel, FakeCommandClient, Task>? afterAssessment = null,
        IReadOnlyList<AssessmentWordResult>? assessmentWords = null,
        IReadOnlyList<TextWord>? projectWords = null, Action<Func<Task>>? captureReload = null,
        Action<AssessViewModel>? captureAssess = null)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var texts = new TextWordsViewModel(fake, selection);
        captureReload?.Invoke(() => texts.ReloadAsync());
        var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
        captureAssess?.Invoke(assess);
        var shown = new List<string>();
        var changes = new ChangesViewModel(fake);
        if (pending is not null) fake.PendingChangesIs(pending);
        await changes.OpenProjectAsync(ProjectPath);
        var inText = new ResultsInTextViewModel(texts, assess, shown.Add, _ => { }, changes, fake);

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
        fake.OnReadWordState(readStateHandler ?? ((request, _) =>
        {
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
        fake.ListTextWordsCompletesWith(new TextWordsResponse(projectWords ?? [], loadedTexts, HasBaseline: true));
        await texts.SetProjectAsync(ProjectPath);

        fake.AssessCompletesWith(new AssessCommandResponse(
            new BaselineCaptureResponse(new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", Digest),
                ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
            new SelectionProjection([], []), ["assessment/one"], "(summary)")
        {
            Measurements = [new ProducedAssessmentReference("assessment/one", AssessmentKinds.ParseTime,
                "invocation/one")],
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
        });
        await assess.RunCommand.ExecuteAsync(null);
        if (waitForReadState) await inText.ReadStateRefresh;
        if (afterAssessment is not null) await afterAssessment(inText, changes, fake);
        return (inText, shown, fake);
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
    public async Task EachOccurrenceIsJudgedAgainstWhatIsStoredThere()
    {
        var (inText, _, _) = await Loaded();

        Assert.Null(inText.Message);
        var line = inText.VisibleLines[0].Tokens;
        Assert.Equal(OccurrenceVerdict.Differs, line[0].Verdict);
        Assert.StartsWith("≠ parser:", line[0].ParserLine, StringComparison.Ordinal);
        Assert.Equal(OccurrenceVerdict.Differs, line[1].Verdict);
        Assert.StartsWith("≠ parser:", line[1].ParserLine, StringComparison.Ordinal);
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
    }

    [Fact]
    public async Task TheFakeTextViewExposesExactOpinionAndParserSetMarkings()
    {
        var (inText, _, _) = await Loaded();
        var tokens = inText.VisibleLines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();

        Assert.Equal(AnalysisMarkingClass.Extra, tokens[0].Marking.PanGlossClass);
        Assert.Equal(ReadingGrade.Approved, Assert.Single(tokens[0].Marking.FieldWorksAnalyses).Opinion);
        Assert.Equal(AnalysisMarkingActionKind.KeepFieldWorks, tokens[0].Marking.PrimaryAction!.Kind);
        Assert.Contains(tokens[0].Marking.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.Add &&
            choice.Label == "Add as Unknown" && choice.Subtitle == "Not in FieldWorks → Unknown");
        Assert.Equal(AnalysisMarkingClass.Conflict, tokens[1].Marking.PanGlossClass);
        Assert.Null(tokens[1].Marking.PrimaryAction);
        Assert.Equal(AnalysisMarkingClass.Different, tokens[2].Marking.PanGlossClass);
        Assert.Equal(AnalysisMarkingClass.None, tokens[3].Marking.PanGlossClass);
    }

    [Fact]
    public async Task CardAndStripGiveTheSameAnswerForEveryLoadedWord()
    {
        var (inText, _, _) = await Loaded();
        var tokens = inText.VisibleLines.SelectMany(line => line.Tokens).Where(token => token.IsWord);

        foreach (var token in tokens)
        {
            var placement = CompareSemantics.PlacementOf(token.Comparison);
            var expected = CompareSemantics.MeaningOf(placement.Standing, placement.Column).Label;
            Assert.Equal(expected, token.PanGlossSummary);
            Assert.Equal(expected, token.VerdictLabel);
        }
    }
    [Fact]
    public async Task OccurrenceFiltersFollowTheSharedMarkingClass()
    {
        var (inText, _, _) = await Loaded();
        var token = inText.VisibleLines[0].Tokens[0];

        Assert.Equal(AnalysisMarkingClass.Extra, token.Marking.PanGlossClass);
        Assert.Equal(OccurrenceVerdict.Differs, token.Verdict);
        Assert.Equal(0, inText.MatchesCount);
        Assert.Equal(3, inText.DiffersCount);
    }
    [Fact]
    public async Task SavedReadStateLoadsPerOccurrenceWithoutHidingNeedsALook()
    {
        var read = new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0);
        var (inText, _, fake) = await Loaded(readOccurrences: [read]);
        var tokens = inText.VisibleLines[0].Tokens;

        Assert.False(tokens[0].Marking.IsUnread);
        Assert.True(tokens[0].Marking.NeedsALook);
        Assert.True(tokens[1].Marking.IsUnread);
        Assert.Contains(fake.ReadWordStateRequests,
            request => request.TextId == TextId && request.IsRead is null);
    }

    [Fact]
    public async Task ReadStateRequestsNameTheAssessmentShownInTheWindow()
    {
        var (_, _, fake) = await Loaded();

        Assert.Contains(fake.ReadWordStateRequests, request =>
            request.AssessmentIds?.Contains("assessment/one") == true);
    }

    [Fact]
    public async Task AnUncertainChangeRemainsMarkedUncertainAfterRefresh()
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

            var token = viewModel.VisibleLines.SelectMany(line => line.Tokens)
                .First(candidate => candidate.Form == "kitabu");
            Assert.True(token.Marking.IsUncertain);
            Assert.True(token.Marking.IsUnread);
            Assert.False(token.Marking.NeedsALook);
        });
    }

    [Fact]
    public async Task EnterAndSpaceRouteWordTokensThroughTheCardOpener()
    {
        var (inText, _, _) = await Loaded();
        var token = inText.VisibleLines.SelectMany(line => line.Tokens).First(candidate =>
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
    }

    [Fact]
    public async Task MarkingOneOccurrenceDoesNotCancelThePendingReadStateLoad()
    {
        var pendingRead = new TaskCompletionSource<CommandOutcome<WordReadStateResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var alphaLine = new TextLine(1, [Word("kitabu", Stored(Book, "book"))])
            { ParagraphId = ParagraphId, SegmentId = SegmentId, ParseIsCurrent = true };
        var betaLine = new TextLine(1, [Word("kitabu", Stored(Book, "book"))])
            { ParagraphId = OtherParagraphId, SegmentId = OtherSegmentId, ParseIsCurrent = true };
        var (inText, _, _) = await Loaded(
            sourceTexts: [new TextLines(TextId, "Alpha", [alphaLine]), new TextLines(OtherTextId, "Beta", [betaLine])],
            readStateHandler: (request, _) => request.IsRead is null && request.TextId == OtherTextId
                ? pendingRead.Task
                : Task.FromResult(CommandOutcome<WordReadStateResponse>.Success(new WordReadStateResponse(
                    request.Occurrences ?? [], true))),
            waitForReadState: false);
        var words = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Where(token => token.Occurrence is not null).ToArray();
        var alpha = words.Single(token => token.Occurrence!.TextId == TextId);
        var beta = words.Single(token => token.Occurrence!.TextId == OtherTextId);
        var refresh = inText.ReadStateRefresh;

        await inText.MarkReadAsync(alpha);
        pendingRead.SetResult(CommandOutcome<WordReadStateResponse>.Success(
            new WordReadStateResponse([beta.Occurrence!], true)));
        await refresh;

        Assert.False(alpha.Marking.IsUnread);
        Assert.False(beta.Marking.IsUnread);
    }

    [Fact]
    public async Task AReadStateLoadThatFinishesAfterMarkReadDoesNotMakeTheWordUnreadAgain()
    {
        var stored = new HashSet<OccurrenceAnchor>();
        var heldLoad = new TaskCompletionSource<CommandOutcome<WordReadStateResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (inText, _, _) = await Loaded(
            readStateHandler: (request, _) =>
            {
                if (request.IsRead is null) return heldLoad.Task;
                foreach (var occurrence in request.Occurrences!) stored.Add(occurrence);
                return Task.FromResult(CommandOutcome<WordReadStateResponse>.Success(
                    new WordReadStateResponse(stored.ToArray(), true)));
            },
            waitForReadState: false);
        var token = inText.VisibleLines.SelectMany(line => line.Tokens).First(token => token.Occurrence is not null);
        var load = inText.ReadStateRefresh;

        await inText.MarkReadAsync(token);
        Assert.False(token.Marking.IsUnread);
        heldLoad.SetResult(CommandOutcome<WordReadStateResponse>.Success(new WordReadStateResponse([], true)));
        await load;

        Assert.False(token.Marking.IsUnread, "The load that began before Mark read put back its older Unread.");
    }

    [Fact]
    public async Task ARejectedSingleWordReadPublishesItsRefusalForTheWindow()
    {
        var (inText, _, client) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
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
    }

    [Fact]
    public async Task APartialTextReadPublishesTheSkippedOccurrenceNoticeForTheWindow()
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
    }

    [Fact]
    public async Task SelectingAWordForKeyboardNavigationDoesNotMarkItRead()
    {
        var (inText, _, fake) = await Loaded();
        var token = inText.VisibleLines[0].Tokens[0];

        inText.SelectToken(token);

        Assert.True(token.Marking.IsUnread);
        Assert.All(fake.ReadWordStateRequests, request => Assert.Null(request.IsRead));
    }

    [Fact]
    public async Task OpeningAWordCardMarksOnlyThatOccurrenceRead()
    {
        var (inText, _, fake) = await Loaded();
        var token = inText.VisibleLines[0].Tokens[1];

        await inText.OpenTokenCardAsync(token);

        Assert.Same(token, inText.SelectedToken);
        Assert.False(token.Marking.IsUnread);
        var request = Assert.Single(fake.ReadWordStateRequests, item => item.IsRead is not null);
        Assert.Equal([token.Occurrence!], request.Occurrences);
        Assert.True(request.IsRead);
    }

    [Fact]
    public async Task ExplicitReadAndUnreadAreScopedToOneOccurrence()
    {
        var (inText, _, fake) = await Loaded();
        var token = inText.VisibleLines[0].Tokens[2];

        await inText.MarkReadAsync(token);
        Assert.False(token.Marking.IsUnread);
        await inText.MarkUnreadAsync(token);

        Assert.True(token.Marking.IsUnread);
        var writes = fake.ReadWordStateRequests.Where(item => item.IsRead is not null).ToArray();
        Assert.Equal([true, false], writes.Select(item => item.IsRead));
        Assert.All(writes, request => Assert.Equal([token.Occurrence!], request.Occurrences));
    }

    [Fact]
    public async Task ExplicitReadCanApplyToOnlyTheSuppliedSelection()
    {
        var (inText, _, fake) = await Loaded();
        var first = inText.VisibleLines[0].Tokens[0];
        var second = inText.VisibleLines[0].Tokens[1];
        var outside = inText.VisibleLines[0].Tokens[2];

        await inText.MarkReadAsync([first, second]);

        Assert.False(first.Marking.IsUnread);
        Assert.False(second.Marking.IsUnread);
        Assert.True(outside.Marking.IsUnread);
        var request = Assert.Single(fake.ReadWordStateRequests, item => item.IsRead is not null);
        Assert.Equal([first.Occurrence!, second.Occurrence!], request.Occurrences);
        Assert.True(request.IsRead);
    }

    [Fact]
    public async Task SelectedOccurrencesCanBeMarkedReadAndUnreadFromThePage()
    {
        var (inText, _, fake) = await Loaded();
        var first = inText.VisibleLines[0].Tokens[0];
        var second = inText.VisibleLines[0].Tokens[1];
        var outside = inText.VisibleLines[0].Tokens[2];
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
    }

    [Fact]
    public async Task WholeTextCanBeMarkedReadAndUnread()
    {
        var (inText, _, fake) = await Loaded();
        var tokens = inText.SelectedText!.Lines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();

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
    }

    [Fact]
    public async Task UnreadFilterKeepsOnlyLinesWithUnreadOccurrences()
    {
        var (inText, _, _) = await Loaded();
        var firstLine = inText.SelectedText!.Lines[0].Tokens.Where(token => token.IsWord).ToArray();
        await inText.MarkReadAsync(firstLine);

        inText.SetFilterCommand.Execute(ResultsInTextFilter.Unread);

        var visible = Assert.Single(inText.VisibleLines);
        Assert.Equal(2, visible.Number);
        Assert.True(visible.Tokens.Single(token => token.IsWord).Marking.IsUnread);
    }

    [Fact]
    public async Task FilteringToDifferencesKeepsOnlyTheirLines_AndDimsTheOtherWordsThere()
    {
        var (inText, _, _) = await Loaded();

        inText.SetFilterCommand.Execute(ResultsInTextFilter.Differs);

        Assert.Equal(2, inText.VisibleLines.Count);
        var line = inText.VisibleLines[0];
        Assert.False(line.Tokens[0].IsDimmed);
        Assert.False(line.Tokens[1].IsDimmed);
        Assert.True(line.Tokens[2].IsDimmed);
        Assert.False(inText.VisibleLines[1].Tokens[0].IsDimmed);

        inText.SetFilterCommand.Execute(ResultsInTextFilter.All);
        Assert.Equal(2, inText.VisibleLines.Count);
        Assert.DoesNotContain(inText.VisibleLines.SelectMany(l => l.Tokens), token => token.IsDimmed);
    }

    [Fact]
    public async Task NeedsALookFilterKeepsWordsWithAvailableMarkingActions()
    {
        var (inText, _, _) = await Loaded();

        Assert.Contains("NeedsALook", Enum.GetNames<ResultsInTextFilter>());
        inText.SetFilterCommand.Execute(Enum.Parse<ResultsInTextFilter>("NeedsALook"));

        var visibleWords = inText.VisibleLines.SelectMany(line => line.Tokens)
            .Where(token => token.IsWord && !token.IsDimmed).Select(token => token.Form).ToArray();
        Assert.Equal(["kitabu", "anapenda", "mtoto", "kitabu"], visibleWords);
        Assert.Equal(4, inText.NeedsALookCount);
        Assert.DoesNotContain(inText.VisibleLines.SelectMany(line => line.Tokens),
            token => token.Form == "zzz" && !token.IsDimmed);
    }

    [Fact]
    public async Task NeedsALookFilterRefreshesWhenAnActionIsStaged()
    {
        var (inText, _, _) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Single(candidate => candidate.Form == "mtoto");
        inText.SetFilterCommand.Execute(ResultsInTextFilter.NeedsALook);
        inText.SelectToken(token);

        await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

        Assert.Equal(3, inText.NeedsALookCount);
        Assert.False(token.Marking.NeedsALook);
        Assert.True(token.IsDimmed);
    }

    [Fact]
    public async Task AWordStripCanStageItsActionWithoutOpeningTheCard()
    {
        var (inText, _, client) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Single(candidate => candidate.Form == "mtoto");

        Assert.True(inText.StagePrimaryMarkingActionForTokenCommand.CanExecute(token));
        await inText.StagePrimaryMarkingActionForTokenCommand.ExecuteAsync(token);

        var request = Assert.Single(client.PendingPutRequests);
        Assert.Equal(ChangeKinds.AddCandidate, request.Change.Kind);
        Assert.Equal("mtoto", request.Change.Word);
        Assert.Equal(CanonicalId.FromGuid(token.WordformId!.Value).Value, request.Change.WordformId);
    }

    [Fact]
    public async Task StagingAnActionKeepsVisibleLineContainersInPlace()
    {
        var (inText, _, _) = await Loaded();
        var resets = 0;
        inText.VisibleLines.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        var token = inText.VisibleLines.SelectMany(line => line.Tokens)
            .Single(candidate => candidate.Form == "mtoto");
        inText.SelectToken(token);

        await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

        Assert.Equal(0, resets);
    }

    [Fact]
    public async Task SelectAllCountsChosenWordsAndUsesTheSameSelectionForReadActions()
    {
        var (inText, _, _) = await Loaded();

        Assert.Equal($"Selected 0 of {inText.AllCount} words", inText.CheckedWordCountLabel);
        inText.SelectAllWordsCommand.Execute(null);

        Assert.Equal(inText.AllCount, inText.CheckedWordCount);
        Assert.Equal(inText.AllCount, inText.SelectedReadStateCount);
        Assert.Equal($"Selected {inText.AllCount} of {inText.AllCount} words", inText.CheckedWordCountLabel);
        inText.ClearSelectedWordsCommand.Execute(null);
        Assert.Equal(0, inText.CheckedWordCount);
    }

    [Fact]
    public async Task BulkRemovalPreviewNamesAffectedWordsAndTheirUsesBeforeStaging()
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
    }

    [Fact]
    public async Task AcceptNewSetForTheSelectedTextUsesTheRealTextScope()
    {
        var (inText, _, client) = await Loaded();
        inText.Changes.AssessmentId = "assessment/one";
        client.AcceptNewSetResponse = new PendingChangesSnapshot(null, "accepted", [], []);

        await inText.AcceptNewSetCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

        var request = Assert.Single(client.AcceptNewSetRequests);
        Assert.Equal(TextId, request.TextId);
        Assert.Null(request.WordformId);
        Assert.False(request.Selection);
        Assert.Equal("assessment/one", request.AssessmentId);
    }

    [Fact]
    public async Task AcceptNewSetForTheSelectionUsesTheSelectionScope()
    {
        var (inText, _, client) = await Loaded();
        inText.Changes.AssessmentId = "assessment/one";
        client.AcceptNewSetResponse = new PendingChangesSnapshot(null, "accepted", [], []);

        await inText.AcceptNewSetCommand.ExecuteAsync(AnalysisOperationScope.AssessmentSelection);

        var request = Assert.Single(client.AcceptNewSetRequests);
        Assert.Null(request.TextId);
        Assert.Null(request.WordformId);
        Assert.True(request.Selection);
        Assert.Equal("assessment/one", request.AssessmentId);
    }

    [Fact]
    public async Task AcceptNewSetEligibilityUsesTheSharedWordClassification()
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
            inText.SelectedText!.Lines.SelectMany(line => line.Tokens)
                .First(token => token.Form == "kitabu").Marking.PanGlossClass);
        Assert.True(inText.AcceptNewSetCommand.CanExecute(AnalysisOperationScope.SelectedText));
        Assert.True(inText.AcceptNewSetCommand.CanExecute(AnalysisOperationScope.AssessmentSelection));
    }

    [Fact]
    public async Task RemovingAnalysesForTheSelectedTextUsesTheRealTextScope()
    {
        var (inText, _, client) = await Loaded();
        client.AnalysisRemovalResponse = new PendingChangesSnapshot(null, "removed", [], []);

        await inText.RemoveAnalysesCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

        var request = Assert.Single(client.AnalysisRemovalRequests);
        Assert.Equal(TextId, request.TextId);
        Assert.Null(request.AnalysisIds);
    }

    [Fact]
    public async Task MarkingSpellingsIncorrectForTheSelectedTextStagesEachWordformOnce()
    {
        var (inText, _, client) = await Loaded();
        var expected = inText.SelectedText!.Lines.SelectMany(line => line.Tokens).Where(token => token.IsWord)
            .Select(token => token.Form).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        await inText.MarkSpellingsIncorrectCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

        Assert.Equal(expected, client.PendingPutRequests.Select(request => request.Change.Word)
            .Order(StringComparer.Ordinal));
        Assert.All(client.PendingPutRequests, request => Assert.Equal(ChangeKinds.IncorrectSpelling,
            request.Change.Kind));
    }

    [Fact]
    public async Task CheckedOccurrencesExposeASelectionAndRemoveEachStoredAnalysisOnce()
    {
        var (inText, _, client) = await Loaded();
        var occurrences = inText.SelectedText!.Lines.SelectMany(line => line.Tokens)
            .Where(token => token.Form == "kitabu").ToArray();
        foreach (var token in occurrences) token.IsSelectedForActions = true;
        client.AnalysisRemovalResponse = new PendingChangesSnapshot(null, "removed", [], []);

        Assert.Equal(2, inText.CheckedWordCount);
        Assert.True(inText.HasCheckedWords);
        await inText.RemoveAnalysesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

        var request = Assert.Single(client.AnalysisRemovalRequests);
        Assert.Equal(["stored-aaaaaaaa-0000-0000-0000-000000000001"], request.AnalysisIds);
    }

    [Fact]
    public async Task RemovingAnalysesForTheSelectionUsesEveryDistinctStoredAnalysisId()
    {
        var (inText, _, client) = await Loaded();
        client.AnalysisRemovalResponse = new PendingChangesSnapshot(null, "removed", [], []);
        var expected = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .SelectMany(token => token.Marking.FieldWorksAnalyses).Select(analysis => analysis.StoredAnalysisId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        await inText.RemoveAnalysesCommand.ExecuteAsync(AnalysisOperationScope.ChosenTexts);

        var request = Assert.Single(client.AnalysisRemovalRequests);
        Assert.Null(request.TextId);
        Assert.Equal(expected, request.AnalysisIds!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RemovingAStoredAnalysisStagesTheExactAnalysis()
    {
        var (inText, _, client) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(candidate => candidate.Form == "kitabu");
        inText.SelectToken(token);
        var stored = Assert.Single(token.Marking.FieldWorksAnalyses);
        var remove = Assert.Single(token.Marking.FixChoices, choice =>
            choice.Kind == AnalysisMarkingActionKind.RemoveAnalysis &&
            choice.StoredAnalysisId == stored.StoredAnalysisId);

        await inText.StageMarkingChoiceCommand.ExecuteAsync(remove);

        var request = Assert.Single(client.AnalysisRemovalRequests);
        Assert.Equal(stored.StoredAnalysisId, request.AnalysisId);
        Assert.Equal(token.Form, request.Word);
        Assert.Equal(CanonicalId.FromGuid(token.WordformId!.Value).Value, request.WordformId);
        Assert.Equal("remove-analysis", Assert.Single(inText.Changes.Snapshot.Changes).Kind);
    }

    [Fact]
    public async Task ARefusedAnalysisRemovalLeavesTheWordUnread()
    {
        var (inText, _, client) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(candidate => candidate.Form == "kitabu");
        inText.SelectToken(token);
        var removal = Assert.Single(token.Marking.FixChoices, choice =>
            choice.Kind == AnalysisMarkingActionKind.RemoveAnalysis);
        client.RemoveAnalysisHandler = (_, _) => Task.FromResult(
            CommandOutcome<PendingChangesSnapshot>.Refused(new Refusal("remove.refused",
                FailureReason.InvalidArgument, "The stored analysis changed.")));

        await inText.StageMarkingChoiceCommand.ExecuteAsync(removal);

        Assert.True(token.Marking.IsUnread);
    }

    [Fact]
    public async Task ARefusedAcceptNewSetLeavesTheWordUnread()
    {
        var (inText, _, client) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(candidate => candidate.Marking.FixChoices.Any(choice =>
                choice.Kind == AnalysisMarkingActionKind.AcceptNewSet));
        inText.SelectToken(token);
        inText.Changes.AssessmentId = "assessment/one";
        var accept = Assert.Single(token.Marking.FixChoices, choice =>
            choice.Kind == AnalysisMarkingActionKind.AcceptNewSet);
        client.AcceptNewSetHandler = (_, _) => Task.FromResult(
            CommandOutcome<PendingChangesSnapshot>.Refused(new Refusal("accept.refused",
                FailureReason.InvalidArgument, "The Assessment is no longer current.")));

        await inText.StageMarkingChoiceCommand.ExecuteAsync(accept);

        Assert.True(token.Marking.IsUnread);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokensUseTheirOwnHomographsProjectFacts(bool reverse)
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
        var displayed = inText.VisibleLines[0].Tokens;
        var own = Assert.Single(displayed, token => token.WordformId == ownId);
        var other = Assert.Single(displayed, token => token.WordformId == otherId);
        Assert.Equal("Approved", own.ProjectStatusLabel);
        Assert.Single(own.ProjectApprovedAnalyses);
        Assert.Equal("book", own.ProjectSummary);
        Assert.Equal("Incorrect spelling", other.ProjectStatusLabel);
        Assert.Empty(other.ProjectApprovedAnalyses);
        Assert.Equal("FieldWorks marks this spelling as incorrect", other.ProjectSummary);
    }

    [Fact]
    public async Task EvidenceRefreshKeepsTheSelectedTextIdentityWhenTitlesAreEqual()
    {
        Func<Task>? reload = null;
        var (inText, _, client) = await Loaded(sourceTexts:
            [new TextLines(TextId, "Same title", []), new TextLines(OtherTextId, "Same title", [])],
            captureReload: action => reload = action);
        inText.SelectedText = inText.Texts[1];
        client.ListTextWordsCompletesWith(new TextWordsResponse([],
            [new TextLines(TextId, "Same title", []), new TextLines(OtherTextId, "Renamed title", [])], true));
        await reload!();

        Assert.Equal(OtherTextId, inText.SelectedText!.TextId);
        Assert.Equal("Renamed title", inText.SelectedText.Title);
    }

    [Fact]
    public async Task AssessmentRefreshKeepsTheSecondTextWithTheSameTitleSelected()
    {
        AssessViewModel? assess = null;
        var (inText, _, _) = await Loaded(sourceTexts:
            [new TextLines(TextId, "Same title", []), new TextLines(OtherTextId, "Same title", [])],
            captureAssess: value => assess = value);
        inText.SelectedText = inText.Texts[1];

        assess!.Restore(new WorkspaceEvidence(assess.Result! with { SummaryMarkdown = "new evidence" }, null, false));

        Assert.Equal(OtherTextId, inText.SelectedText!.TextId);
    }

    [Fact]
    public async Task EvidenceRefreshFallsBackOnlyWhenTheSelectedTextDisappears()
    {
        Func<Task>? reload = null;
        var (inText, _, client) = await Loaded(sourceTexts:
            [new TextLines(TextId, "Alpha", []), new TextLines(OtherTextId, "Beta", [])],
            captureReload: action => reload = action);
        inText.SelectedText = inText.Texts[1];
        client.ListTextWordsCompletesWith(new TextWordsResponse([], [new TextLines(TextId, "Alpha", [])], true));

        await reload!();

        Assert.Equal(TextId, inText.SelectedText!.TextId);
        client.ListTextWordsCompletesWith(new TextWordsResponse([], [], true));
        await reload();
        Assert.Null(inText.SelectedText);
    }

    [Fact]
    public async Task AWordCardWithoutAnOccurrenceDoesNotBorrowAHomographsProjectFacts()
    {
        var approved = Stored(Book, "book");
        var ownId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
        var (inText, _, _) = await Loaded(sourceTexts: [], projectWords:
            [new TextWord("kitabu", ownId.ToString("D"), [], [approved], []) { Analyses = [approved] }]);

        inText.SelectWord("kitabu");

        Assert.Null(inText.SelectedToken!.WordformId);
        Assert.Empty(inText.SelectedToken.ProjectApprovedAnalyses);
        Assert.Equal("No project entry is loaded for this word.", inText.SelectedToken.ProjectSummary);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public async Task WordformWidePendingMarkersExcludeSameSpellingDifferentWordforms(string kind)
    {
        var (inText, _, _) = await HomographsWithPendingChoice(kind);
        var tokens = inText.VisibleLines[0].Tokens;

        Assert.False(tokens[0].IsPending);
        Assert.True(tokens[1].IsPending);
        Assert.True(tokens[2].IsPending);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public async Task UndoCheckedWordsCannotRemoveAnotherWordformsSameSpellingChoice(string kind)
    {
        var (inText, _, client) = await HomographsWithPendingChoice(kind);
        inText.VisibleLines[0].Tokens[0].IsSelectedForActions = true;

        await inText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

        Assert.Empty(client.PendingRemoveRequests);
        Assert.Single(inText.Changes.Items);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public async Task UndoEnablementRequiresThePendingWordformIdentity(string kind)
    {
        var (inText, _, client) = await HomographsWithPendingChoice(kind);
        var tokens = inText.VisibleLines[0].Tokens;
        tokens[0].IsSelectedForActions = true;
        Assert.False(inText.UndoChangesCommand.CanExecute(AnalysisOperationScope.CheckedWords));
        tokens[0].IsSelectedForActions = false;
        tokens[2].IsSelectedForActions = true;
        Assert.True(inText.UndoChangesCommand.CanExecute(AnalysisOperationScope.CheckedWords));

        await inText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

        Assert.Equal("homograph-choice", Assert.Single(client.PendingRemoveRequests).ChangeId);
        Assert.All(tokens, token => Assert.False(token.IsPending));
    }

    [Theory]
    [InlineData(AnalysisOperationScope.CheckedWords)]
    [InlineData(AnalysisOperationScope.SelectedText)]
    public async Task ScopedUndoDoesNotExpandAnAcceptedGroup(AnalysisOperationScope scope)
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
        inText.Texts[0].Lines[0].Tokens[0].IsSelectedForActions = true;

        await inText.UndoChangesCommand.ExecuteAsync(scope);

        Assert.Equal("own", Assert.Single(client.PendingRemoveRequests).ChangeId);
        Assert.Equal("other", Assert.Single(inText.Changes.Items).ChangeId);
        Assert.False(inText.Texts[0].Lines[0].Tokens[0].IsPending);
        Assert.True(inText.Texts[1].Lines[0].Tokens[0].IsPending);
    }

    [Theory]
    [InlineData(ChangeKinds.IncorrectSpelling)]
    [InlineData(ChangeKinds.AddCandidate)]
    public async Task RefusedUndoRetainsOnlyTheAddressedHomographsMarkers(string kind)
    {
        var (inText, _, client) = await HomographsWithPendingChoice(kind);
        client.PendingRemoveHandler = (_, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(
            new Refusal("change.cannot-compose", FailureReason.Refused, "Removal refused.")));
        inText.VisibleLines[0].Tokens[1].IsSelectedForActions = true;

        await inText.UndoChangesCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);

        Assert.Single(inText.Changes.Items);
        Assert.False(inText.VisibleLines[0].Tokens[0].IsPending);
        Assert.All(inText.VisibleLines[0].Tokens.Skip(1), token =>
        {
            Assert.True(token.IsPending);
            Assert.Single(token.StagedChanges);
        });
    }

    private static Task<(ResultsInTextViewModel InText, List<string> Shown, FakeCommandClient Client)>
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
    public async Task UndoInASelectedTextLeavesTheSameWordInAnotherTextStaged()
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
    }

    [Fact]
    public async Task AClickedWordOpensInTheWordsView_OnlyWhenAsked()
    {
        var (inText, shown, _) = await Loaded();
        var anapenda = inText.VisibleLines[0].Tokens[1];

        inText.SelectToken(anapenda);
        Assert.Same(anapenda, inText.SelectedToken);
        Assert.Empty(shown);

        inText.ShowInWordsCommand.Execute(null);
        Assert.Equal(["anapenda"], shown);
    }

    [Fact]
    public async Task OpinionsNeedAnExplicitReadingAndMarkTheSelectedOccurrence()
    {
        var (inText, _, _) = await Loaded();
        var tokens = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Where(token => token.Form == "kitabu").ToArray();
        var selected = tokens[0];
        inText.SelectToken(selected);

        Assert.Null(selected.SelectedReading);
        Assert.False(inText.AddChangeCommand.CanExecute(ChangeKinds.Approve));

        selected.SelectedReading = selected.Readings[0];
        Assert.True(inText.AddChangeCommand.CanExecute(ChangeKinds.Approve));
        await inText.AddChangeCommand.ExecuteAsync(ChangeKinds.Approve);

        Assert.Equal(ChangeKinds.Approve, Assert.Single(inText.Changes.Items).Kind);
        Assert.True(tokens[0].IsPending);
        Assert.Equal("Not applied yet", tokens[0].PendingChangeStatus);
        Assert.Equal(PendingChangeState.NotAppliedYet, tokens[0].PendingState);
        Assert.False(tokens[1].IsPending);
        Assert.Equal(PendingChangeState.None, tokens[1].PendingState);
        Assert.False(inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Single(token => token.Form == "anapenda").IsPending);
    }

    [Fact]
    public async Task AnalyzeTextsPassesTheSelectedOccurrenceWithAnOpinionChange()
    {
        var (inText, _, client) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(candidate => candidate.Form == "kitabu");
        inText.SelectToken(token);
        token.SelectedReading = token.Readings[0];

        await inText.AddChangeCommand.ExecuteAsync(ChangeKinds.Approve);

        var change = Assert.Single(client.PendingPutRequests).Change;
        Assert.Equal(ChangeKinds.Approve, change.Kind);
        Assert.Equal(new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 0), change.Occurrence);
        Assert.Equal(CanonicalId.FromGuid(token.WordformId!.Value).Value, change.WordformId);
    }

    [Fact]
    public async Task AnalyzeTextsSendsTheSegmentIndexWhenPunctuationPrecedesARepeatedWord()
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
        var selected = inText.Texts.SelectMany(text => text.Lines).SelectMany(item => item.Tokens)
            .Single(token => token.OccurrenceIndex == 2);
        inText.SelectToken(selected);
        selected.SelectedReading = selected.Readings[0];

        await inText.AddChangeCommand.ExecuteAsync(ChangeKinds.Reject);

        var change = Assert.Single(client.PendingPutRequests).Change;
        Assert.Equal(CanonicalId.FromGuid(selected.WordformId!.Value).Value, change.WordformId);
        Assert.Equal(new OccurrenceAnchor(TextId, ParagraphId, SegmentId, 2), change.Occurrence);
    }

    [Theory]
    [InlineData(ChangeKinds.Reject, true)]
    [InlineData(ChangeKinds.Candidate, true)]
    [InlineData(ChangeKinds.AddCandidate, false)]
    public async Task AnalyzeTextsAnchorsOpinionChangesButNotCandidateAdds(string kind, bool hasAnchor)
    {
        var (inText, _, client) = await Loaded();
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(candidate => candidate.Form == "kitabu");
        inText.SelectToken(token);
        token.SelectedReading = token.Readings[0];

        await inText.AddChangeCommand.ExecuteAsync(kind);

        Assert.NotEmpty(client.PendingPutRequests);
        Assert.All(client.PendingPutRequests,
            request => Assert.Equal(hasAnchor, request.Change.Occurrence is not null));
    }

    [Fact]
    public async Task AnUncertainChangeExposesItsOccurrenceAnchor()
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
    }

    [Fact]
    public async Task AWordWithAnUncertainChangeShowsTheCheckAgainLabel()
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
        var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(candidate => candidate.Form == "anapenda");

        Assert.Equal("Uncertain — check again", inText.Texts.SelectMany(text => text.Lines)
            .SelectMany(line => line.Tokens).First(candidate => candidate.Form == "kitabu").PendingChangeStatus);
        Assert.True(token.IsUncertainChanged);
    }

    [Fact]
    public async Task UncertainHighlightUsesItsAnchoredSegmentAndOccurrence()
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

        var lines = inText.Texts.SelectMany(text => text.Lines).ToArray();
        Assert.True(lines[0].Tokens[1].IsUncertainChanged);
        Assert.False(lines[1].Tokens[1].IsUncertainChanged);
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
    public async Task AStoredAnalysisChoiceStagesItsExactOpinionAtTheSelectedOccurrence()
    {
        var (inText, _, fake) = await Loaded();
        inText.Changes.AssessmentId = "assessment/one";
        var tokens = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
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
        Assert.Single(selected.Marking.StagedTransitions);
        Assert.Empty(tokens[1].Marking.StagedTransitions);
    }

    [Fact]
    public async Task AParserOnlyReadingCanBeDisapprovedAtTheSelectedOccurrence()
    {
        var (inText, _, fake) = await Loaded();
        inText.Changes.AssessmentId = "assessment/one";
        var selected = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
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
    }

    [Fact]
    public async Task KeepFieldWorksIsANamedNoOp()
    {
        var (inText, _, fake) = await Loaded();
        var selected = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .First(token => token.Form == "kitabu");
        inText.SelectToken(selected);

        await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

        Assert.Empty(fake.PendingPutRequests);
        Assert.Empty(selected.Marking.StagedTransitions);
        Assert.False(selected.Marking.IsUnread);
        Assert.Contains(fake.ReadWordStateRequests, request => request.IsRead == true &&
            request.Occurrences?.SequenceEqual([selected.Occurrence!]) == true);
    }

    [Fact]
    public async Task StagedChangesRemainKeyedPerReadingAndOccurrence()
    {
        var (inText, _, _) = await Loaded();
        var tokens = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
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
    }

    [Fact]
    public async Task AParserOnlyReadingCanBeAddedAsApproved()
    {
        var (inText, _, fake) = await Loaded();
        inText.Changes.AssessmentId = "assessment/one";
        var selected = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
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
    }

    [Fact]
    public async Task AParserOnlyReadingCanBeAddedAsUnknown()
    {
        var (inText, _, fake) = await Loaded();
        var selected = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Single(token => token.Form == "mtoto");
        inText.SelectToken(selected);
        var choice = Assert.Single(selected.Marking.FixChoices,
            candidate => candidate.Kind == AnalysisMarkingActionKind.Add && candidate.Label == "Add as Unknown");

        await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);

        var request = Assert.Single(fake.PendingPutRequests);
        Assert.Equal(ChangeKinds.AddCandidate, request.Change.Kind);
        Assert.Equal(CanonicalId.FromGuid(selected.WordformId!.Value).Value, request.Change.WordformId);
        Assert.Null(request.Change.AssessmentId);
        Assert.Equal(0, request.Change.ReadingIndex);
        Assert.Null(request.Change.Occurrence);
    }

    [Fact]
    public async Task PrimaryAddStagesAnUnknownReadingWithoutAnAssessmentId()
    {
        var (inText, _, fake) = await Loaded();
        var selected = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Single(token => token.Form == "mtoto");
        inText.SelectToken(selected);

        await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.PendingPutRequests);
        Assert.Equal(ChangeKinds.AddCandidate, request.Change.Kind);
        Assert.Equal("Unknown", selected.Marking.PrimaryAction!.AfterApply);
        Assert.Null(request.Change.AssessmentId);
    }

    [Fact]
    public async Task ARefusedMarkingStageLeavesTheOccurrenceUnread()
    {
        var (inText, _, fake) = await Loaded();
        var selected = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Single(token => token.Form == "mtoto");
        inText.SelectToken(selected);
        fake.PendingPutRefusal = new Refusal("pending-change-invalid", FailureReason.InvalidArgument,
            "The proposed change was refused.");

        await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

        Assert.NotEmpty(fake.PendingPutRequests);
        Assert.True(selected.Marking.IsUnread);
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
            var projectWord = new TextWordRowViewModel(word);
            var token = new ResultsTokenViewModel(word.Form, 1,
                new TextToken(word.Form, word.Form, null, "unanalysed"), null, projectWord);

            Assert.Equal(expected, token.ProjectStatusMark);
        }
    }

    [Fact]
    public void BeforeAnyAssessmentItLeavesTheEmptyStateToTheSharedParsePrompt()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var inText = new ResultsInTextViewModel(
            new TextWordsViewModel(fake, selection), new AssessViewModel(fake, selection), _ => { }, _ => { },
            new ChangesViewModel(fake), fake);

        Assert.Null(inText.Message);
        Assert.False(inText.HasMessage);
        Assert.False(inText.HasAssessment);
        Assert.False(inText.HasResults);
        Assert.Empty(inText.VisibleLines);
    }

    [Fact]
    public async Task BeforeTheFirstParseTheReaderShowsTheTextWithItsFieldWorksLine()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var texts = new TextWordsViewModel(fake, selection);
        fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
        var inText = new ResultsInTextViewModel(texts, new AssessViewModel(fake, selection), _ => { }, _ => { },
            new ChangesViewModel(fake), fake);
        fake.ListTextWordsCompletesWith(new TextWordsResponse([], [new TextLines(TextId, "Alpha",
            [new TextLine(1, [Word("kitabu", Stored(Book, "book"))])])], HasBaseline: true));

        await texts.SetProjectAsync(ProjectPath);

        Assert.True(inText.HasTexts);
        Assert.False(inText.HasResults);
        Assert.True(inText.HasLines);
        Assert.Equal("Alpha", Assert.Single(inText.Texts).Title);
        var word = Assert.Single(Assert.Single(inText.VisibleLines).Tokens);
        Assert.True(word.HasFieldWorksAnalyses);
        Assert.Equal("Not parsed yet", word.PanGlossSummary);
        Assert.False(word.ShowUnread);
        Assert.False(word.HasPrimaryAction);
        Assert.Null(inText.Message);
        Assert.Empty(fake.ReadWordStateRequests);
    }
}
