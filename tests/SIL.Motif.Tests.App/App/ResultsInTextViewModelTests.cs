using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
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

    private static async Task<(ResultsInTextViewModel InText, List<string> Shown, FakeCommandClient Client)> Loaded(
        PendingChangesSnapshot? pending = null, IReadOnlyList<TextLine>? sourceLines = null,
        IReadOnlyList<OccurrenceAnchor>? readOccurrences = null)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var texts = new TextWordsViewModel(fake, selection);
        var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
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
        var availableOccurrences = lines.Where(line => line.ParagraphId != Guid.Empty && line.SegmentId != Guid.Empty)
            .SelectMany(line => line.Tokens.Where(token => token.Form is not null)
                .Select(token => new OccurrenceAnchor(TextId, line.ParagraphId, line.SegmentId, token.OccurrenceIndex)))
            .ToArray();
        fake.OnReadWordState((request, _) =>
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
        });
        fake.ListTextWordsCompletesWith(new TextWordsResponse([],
            [new TextLines(TextId, "Alpha", lines)], HasBaseline: true));
        await texts.SetProjectAsync(ProjectPath);

        fake.AssessCompletesWith(new AssessCommandResponse(
            new BaselineCaptureResponse(new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", Digest),
                ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
            new SelectionProjection([], []), ["assessment/one"], "(summary)")
        {
            Words =
            [
                Result("kitabu", Book, Child),
                Result("anapenda", Like),
                Result("mtoto", Child),
                Result("zzz"),
            ],
        });
        await assess.RunCommand.ExecuteAsync(null);
        await inText.ReadStateRefresh;
        return (inText, shown, fake);
    }

    [Fact]
    public async Task EachOccurrenceIsJudgedAgainstWhatIsStoredThere()
    {
        var (inText, _, _) = await Loaded();

        Assert.Null(inText.Message);
        var line = inText.VisibleLines[0].Tokens;
        Assert.Equal(OccurrenceVerdict.Matches, line[0].Verdict);
        Assert.Equal("✓ parser agrees, with 1 other reading", line[0].ParserLine);
        Assert.Equal(OccurrenceVerdict.Differs, line[1].Verdict);
        Assert.StartsWith("≠ parser:", line[1].ParserLine, StringComparison.Ordinal);
        Assert.Equal(OccurrenceVerdict.New, line[2].Verdict);
        Assert.Equal("Not present", line[2].ProjectStatusLabel);
        Assert.Equal(OccurrenceVerdict.NoParse, line[3].Verdict);
        Assert.False(line[4].IsWord);
        Assert.Contains(line[0].Readings, reading => reading.IsStoredHere);
        Assert.DoesNotContain(line[1].Readings, reading => reading.IsStoredHere);

        Assert.Equal(5, inText.AllCount);
        Assert.Equal(2, inText.MatchesCount);
        Assert.Equal(1, inText.DiffersCount);
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
        first.IsSelectedForReadState = true;
        second.IsSelectedForReadState = true;

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

        var line = Assert.Single(inText.VisibleLines);
        Assert.Equal(1, line.Number);
        Assert.False(line.Tokens[1].IsDimmed);
        Assert.True(line.Tokens[0].IsDimmed);
        Assert.True(line.Tokens[2].IsDimmed);

        inText.SetFilterCommand.Execute(ResultsInTextFilter.All);
        Assert.Equal(2, inText.VisibleLines.Count);
        Assert.DoesNotContain(inText.VisibleLines.SelectMany(l => l.Tokens), token => token.IsDimmed);
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
            [new PendingChange("uncertain", "wordform/kitabu", "kitabu", ChangeKinds.Approve, null, null, [])],
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
            [new PendingChange("uncertain", "wordform/kitabu", "kitabu", ChangeKinds.Approve, null, null, [])],
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
            occurrence: selected.Occurrence, storedAnalysisId: "stored-book"));
        inText.Changes.Items.Add(new ChangeViewModel(ChangeKinds.AddCandidate, "kitabu", "extra reading",
            readingIndex: 1));

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
    public void ProjectStatusChipUsesTheWordStatusVerdict()
    {
        var analysis = new ProjectAnalysis("k1", [new ParserReadingMorph("form", "gloss", "n", null, false, null)]);
        var cases = new[]
        {
            (new TextWord("approved", null, [], [analysis], []), Verdict.Approved),
            (new TextWord("candidate", null, [], [], [], CandidateCount: 1), Verdict.Candidate),
            (new TextWord("rejected", null, [], [], [analysis]), Verdict.Differs),
            (new TextWord("incorrect", null, [], [], [], IncorrectSpelling: true), Verdict.Several),
            (new TextWord("new", null, [], [], []), Verdict.New),
        };

        foreach (var (word, expected) in cases)
        {
            var projectWord = new TextWordRowViewModel(word);
            var token = new ResultsTokenViewModel(word.Form, 1,
                new TextToken(word.Form, word.Form, null, "unanalysed"), null, projectWord);

            Assert.Equal(expected, token.ProjectStatusVerdict);
        }
    }

    [Fact]
    public void BeforeAnyAssessmentItSaysSoInsteadOfShowingNothing()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var inText = new ResultsInTextViewModel(
            new TextWordsViewModel(fake, selection), new AssessViewModel(fake, selection), _ => { }, _ => { },
            new ChangesViewModel(fake), fake);

        Assert.StartsWith("Run an Assessment", inText.Message, StringComparison.Ordinal);
        Assert.Empty(inText.VisibleLines);
    }

    [Fact]
    public async Task BeforeAssessmentTheReaderShowsChosenTextAndItsWords()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var texts = new TextWordsViewModel(fake, selection);
        fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
        var inText = new ResultsInTextViewModel(texts, new AssessViewModel(fake, selection), _ => { }, _ => { },
            new ChangesViewModel(fake), fake);
        fake.ListTextWordsCompletesWith(new TextWordsResponse([], [new TextLines(TextId, "Alpha",
            [new TextLine(1, [Word("kitabu", null)])])], HasBaseline: true));

        await texts.SetProjectAsync(ProjectPath);

        Assert.True(inText.HasTexts);
        Assert.Equal("Alpha", Assert.Single(inText.Texts).Title);
        Assert.Equal("kitabu", Assert.Single(Assert.Single(inText.VisibleLines).Tokens).Form);
        Assert.Equal(OccurrenceVerdict.NotAssessed,
            Assert.Single(Assert.Single(inText.VisibleLines).Tokens).Verdict);
        Assert.Null(inText.Message);
    }
}
