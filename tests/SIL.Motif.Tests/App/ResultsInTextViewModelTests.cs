using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
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

    private static readonly ParseAnalysis Book = Reading("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly ParseAnalysis Love = Reading("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly ParseAnalysis Like = Reading("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly ParseAnalysis Child = Reading("aaaaaaaa-0000-0000-0000-000000000004");

    private static ParseAnalysis Reading(string form) =>
        new([new ParseMorph(form, "bbbbbbbb-0000-0000-0000-000000000001", null, null)]);

    private static ProjectAnalysis Stored(ParseAnalysis reading, string gloss) =>
        new(ProjectAnalysisKey.For(reading), [new ParserReadingMorph("form", gloss, "n", null, false, null)]);

    private static TextToken Word(string text, ProjectAnalysis? stored) => new(text, text, null, "approved") { Analysis = stored };

    private static AssessmentWordResult Result(string word, params ParseAnalysis[] readings) =>
        new(word, readings.Length > 0 ? "analysed" : "no-analysis", false, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, word, 3, false, false, false, readings, []),
            Readings = readings.Select(_ => new ParserReading([new ParserReadingMorph(word, "gloss", "n", null, false, null)])).ToArray(),
            ReadingGrades = readings.Select(_ => "no-opinion").ToArray(),
        };

    private static async Task<(ResultsInTextViewModel InText, List<string> Shown)> Loaded()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var texts = new TextWordsViewModel(fake, selection);
        var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
        var shown = new List<string>();
        var inText = new ResultsInTextViewModel(texts, assess, shown.Add, _ => { });

        fake.ListTextWordsCompletesWith(new TextWordsResponse([],
            [new TextLines(TextId, "Alpha",
            [
                new TextLine(1, [Word("kitabu", Stored(Book, "book")), Word("anapenda", Stored(Love, "love")),
                    Word("mtoto", null), Word("zzz", null), new TextToken(".", null, null, null)]),
                new TextLine(2, [Word("kitabu", Stored(Book, "book"))]),
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
        return (inText, shown);
    }

    [Fact]
    public async Task EachOccurrenceIsJudgedAgainstWhatIsStoredThere()
    {
        var (inText, _) = await Loaded();

        Assert.Null(inText.Message);
        var line = inText.VisibleLines[0].Tokens;
        Assert.Equal(OccurrenceVerdict.Matches, line[0].Verdict);
        Assert.Equal("✓ parser agrees, with 1 other reading", line[0].ParserLine);
        Assert.Equal(OccurrenceVerdict.Differs, line[1].Verdict);
        Assert.StartsWith("≠ parser:", line[1].ParserLine, StringComparison.Ordinal);
        Assert.Equal(OccurrenceVerdict.New, line[2].Verdict);
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
        var (inText, _) = await Loaded();

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
        var (inText, shown) = await Loaded();
        var anapenda = inText.VisibleLines[0].Tokens[1];

        inText.SelectToken(anapenda);
        Assert.Same(anapenda, inText.SelectedToken);
        Assert.Empty(shown);

        inText.ShowInWordsCommand.Execute(null);
        Assert.Equal(["anapenda"], shown);
    }

    [Fact]
    public void BeforeAnyAssessmentItSaysSoInsteadOfShowingNothing()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var inText = new ResultsInTextViewModel(
            new TextWordsViewModel(fake, selection), new AssessViewModel(fake, selection), _ => { }, _ => { });

        Assert.StartsWith("Run an Assessment", inText.Message, StringComparison.Ordinal);
        Assert.Empty(inText.VisibleLines);
    }
}
