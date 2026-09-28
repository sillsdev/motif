using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
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
        new(ProjectAnalysisKey.For(reading), [new ParserReadingMorph("form", gloss, "n", null, false, null)]);

    private static TextToken Word(string text, ProjectAnalysis? stored, int index = 0) =>
        new(text, text, null, "approved")
        {
            Analysis = stored,
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
            ReadingGrades = readings.Select(_ => "no-opinion").ToArray(),
        };

    private static async Task<(ResultsInTextViewModel InText, List<string> Shown, FakeCommandClient Client)> Loaded(
        PendingChangesSnapshot? pending = null)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var texts = new TextWordsViewModel(fake, selection);
        var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
        var shown = new List<string>();
        var changes = new ChangesViewModel(fake);
        if (pending is not null) fake.PendingChangesIs(pending);
        await changes.OpenProjectAsync(ProjectPath);
        var inText = new ResultsInTextViewModel(texts, assess, shown.Add, _ => { }, changes);

        fake.ListTextWordsCompletesWith(new TextWordsResponse([],
            [new TextLines(TextId, "Alpha",
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
            ])], HasBaseline: true));
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
    public async Task OpinionsNeedAnExplicitReadingAndMarkEveryOccurrenceOfTheWordform()
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
        Assert.All(tokens, token =>
        {
            Assert.True(token.IsPending);
            Assert.Equal("Not applied yet", token.PendingChangeStatus);
            Assert.Equal(PendingChangeState.NotAppliedYet, token.PendingState);
        });
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
    }

    [Fact]
    public async Task AWordWithAnUncertainChangeShowsTheCheckAgainLabel()
    {
        var fit = new ChangeFit("uncertain", ChangeFitStatus.Uncertain, ["The sentence changed."])
        {
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
            new ChangesViewModel(fake));

        Assert.StartsWith("Run an Assessment", inText.Message, StringComparison.Ordinal);
        Assert.Empty(inText.VisibleLines);
    }

    [Fact]
    public async Task BeforeAssessmentTheReaderShowsChosenTextAndItsWords()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var texts = new TextWordsViewModel(fake, selection);
        var inText = new ResultsInTextViewModel(texts, new AssessViewModel(fake, selection), _ => { }, _ => { },
            new ChangesViewModel(fake));
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
