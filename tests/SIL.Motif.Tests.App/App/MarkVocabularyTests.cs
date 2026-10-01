using Avalonia.Automation;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the window's one vocabulary of marks: what FieldWorks holds, what PanGloss built and what the two mean
/// together are three separate things, each with its own words and its own glyphs.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MarkVocabularyTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public MarkVocabularyTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    public static TheoryData<string, ParserOutcome, string, MeaningTone> OpinionByOutcome() => new()
    {
        { ProjectStanding.Approved, ParserOutcome.Same, "Kept", MeaningTone.Fine },
        { ProjectStanding.Approved, ParserOutcome.Different, "Built something else", MeaningTone.Problem },
        { ProjectStanding.Approved, ParserOutcome.NoParse, "Lost", MeaningTone.Problem },
        { ProjectStanding.Approved, ParserOutcome.Stopped, "Unknown yet", MeaningTone.Neutral },
        { ProjectStanding.Approved, ParserOutcome.NotParsed, "Not parsed", MeaningTone.Neutral },
        { ProjectStanding.Candidate, ParserOutcome.Same, "PanGloss confirms", MeaningTone.Fine },
        { ProjectStanding.Candidate, ParserOutcome.Different, "Differs: have a look", MeaningTone.Look },
        { ProjectStanding.Candidate, ParserOutcome.NoParse, "Grammar can't build it", MeaningTone.Look },
        { ProjectStanding.Candidate, ParserOutcome.Stopped, "Unknown yet", MeaningTone.Neutral },
        { ProjectStanding.Candidate, ParserOutcome.NotParsed, "Not parsed", MeaningTone.Neutral },
        { ProjectStanding.Rejected, ParserOutcome.Same, "Built anyway", MeaningTone.Problem },
        { ProjectStanding.Rejected, ParserOutcome.Different, "Fine", MeaningTone.Fine },
        { ProjectStanding.Rejected, ParserOutcome.NoParse, "Fine", MeaningTone.Fine },
        { ProjectStanding.Rejected, ParserOutcome.Stopped, "Unknown yet", MeaningTone.Neutral },
        { ProjectStanding.Rejected, ParserOutcome.NotParsed, "Not parsed", MeaningTone.Neutral },
        { ProjectStanding.IncorrectSpelling, ParserOutcome.Same, "Builds a misspelling", MeaningTone.Look },
        { ProjectStanding.IncorrectSpelling, ParserOutcome.Different, "Over-generates", MeaningTone.Look },
        { ProjectStanding.IncorrectSpelling, ParserOutcome.NoParse, "Correct", MeaningTone.Fine },
        { ProjectStanding.IncorrectSpelling, ParserOutcome.Stopped, "Unknown yet", MeaningTone.Neutral },
        { ProjectStanding.IncorrectSpelling, ParserOutcome.NotParsed, "Not parsed", MeaningTone.Neutral },
        { ProjectStanding.NotPresent, ParserOutcome.Same, "Nothing to compare", MeaningTone.Neutral },
        { ProjectStanding.NotPresent, ParserOutcome.Different, "New: PanGloss proposes", MeaningTone.Look },
        { ProjectStanding.NotPresent, ParserOutcome.NoParse, "Nobody can analyze", MeaningTone.Neutral },
        { ProjectStanding.NotPresent, ParserOutcome.Stopped, "Unknown yet", MeaningTone.Neutral },
        { ProjectStanding.NotPresent, ParserOutcome.NotParsed, "Not parsed", MeaningTone.Neutral },
    };

    [Theory]
    [MemberData(nameof(OpinionByOutcome))]
    public void EachOpinionAndOutcomeMeanOneWordInOneTone(
        string standing, ParserOutcome outcome, string word, MeaningTone tone) =>
        Assert.Equal((word, tone), WindowWords.MeaningOf(standing, outcome));

    [Fact]
    public void ThePinnedTableCoversEveryOpinionAndEveryOutcome()
    {
        var rows = OpinionByOutcome().Select(row => ((string)row[0], (ParserOutcome)row[1])).ToHashSet();

        Assert.Equal(25, rows.Count);
        Assert.Equal(Enum.GetValues<ParserOutcome>().Length, rows.Select(row => row.Item2).Distinct().Count());
    }

    [Fact]
    public void EveryMatrixFamilyHasATone() =>
        Assert.All(Enum.GetValues<CompareFamilyKind>(), family => Assert.True(Enum.IsDefined(WindowWords.ToneOf(family))));

    [Fact]
    public void EachOutcomeIsTheMatrixColumnOfTheSameName()
    {
        Assert.Equal(
            ["Same", "Different", "No parse", "Stopped", "Not parsed"],
            Enum.GetValues<CompareColumnKind>().Select(column => WindowWords.Of(WindowWords.OutcomeOf(column))));
        Assert.All(Enum.GetValues<ParserOutcome>(), outcome =>
            Assert.Equal(outcome, WindowWords.OutcomeOf(WindowWords.ColumnOf(outcome))));
    }

    [Fact]
    public void OpinionsAreFieldWorksOwnCapitalisedWords()
    {
        Assert.Equal(
            ["Approved", "Disapproved", "Unknown", "Not in FieldWorks"],
            Enum.GetValues<OpinionMarkKind>().Select(WindowWords.Of));
        Assert.Equal(OpinionMarkKind.Unknown, WindowWords.OpinionOf(ReadingGrade.Candidate));
        Assert.Equal(OpinionMarkKind.Disapproved, WindowWords.OpinionOf(ReadingGrade.Disapproved));
    }

    [Fact]
    public void NoGlyphBelongsToTwoKindsOfMark()
    {
        var owners = MarkGlyphs.All
            .SelectMany(table => table.Glyphs.Select(glyph => (table.Kind, Glyph: glyph)))
            .GroupBy(entry => entry.Glyph)
            .Where(group => group.Select(entry => entry.Kind).Distinct().Count() > 1 || group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(owners);
        Assert.Equal(Enum.GetValues<MarkKind>().Length, MarkGlyphs.All.Select(table => table.Kind).Distinct().Count());
    }

    [Fact]
    public void EveryMarkWearsTheGlyphOfItsOwnKind() =>
        Assert.All(MarkGlyphs.All, table =>
            Assert.All(Mark.AllOf(table.Kind), mark =>
                Assert.True(mark.Glyph.Length == 0 || table.Glyphs.Contains(mark.Glyph), $"{mark.Word}: {mark.Glyph}")));

    [Fact]
    public void AGrammarWarningIsATriangleAnErrorACircledBangAndANoteAnI()
    {
        Assert.Equal("⚠", GrammarLevelMarks.Warning);
        Assert.Equal("!", GrammarLevelMarks.Error);
        Assert.Equal("i", GrammarLevelMarks.Information);
        Assert.Equal("error", Mark.Of(GrammarDiagnosticLevel.Error).Value);
    }

    [Fact]
    public void AChipWithoutWordsIsNamedByItsMark() =>
        _avalonia.Invoke(() =>
        {
            Assert.Equal("Not in FieldWorks", AutomationProperties.GetName(new MarkChip { Mark = Mark.NotInFieldWorks }));
            Assert.Equal("Stopped", AutomationProperties.GetName(new MarkChip { Mark = Mark.Stopped }));
        });
}
