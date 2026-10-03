using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ParserRefusalDisplayTests
{
    [Fact]
    public void RefusedWordsKeepTheNoParseColumnAndExistingListsWithoutParseAgain()
    {
        var result = new AssessmentWordResult("chats", "skipped", false, "Skipped", 0, "-")
        {
            Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "chats", 0,
                false, false, true, [], []),
        };
        var words = new AssessWordsViewModel();
        words.Load([result]);
        var compare = new CompareViewModel { Rerun = (_, _) => Task.CompletedTask };
        compare.Load(words.AllRows);
        var lists = new TextsListsViewModel(compare);
        lists.SelectedList = lists.Lists.Single(list => list.Name == "Nobody can analyze");

        var row = Assert.Single(compare.Words).WordRow;
        Assert.Equal(ParserOutcome.NoParse, row.Outcome);
        Assert.Equal(5, compare.Columns.Count);
        Assert.Equal(8, lists.Lists.Count);
        var cell = compare.ChosenCell!;
        Assert.Equal(CompareColumnKind.NoParse, cell.Column);
        Assert.Equal(ParserRefusals.Title, cell.Label);
        Assert.Equal(MeaningTone.Neutral, cell.Tone);
        Assert.Equal(Mark.ParserRefusal, row.OutcomeMark);
        Assert.Equal(ParserRefusals.Title, row.Meaning);
        Assert.Equal(CompareFamilyKind.Unknown, Assert.Single(compare.Words).Family);
        Assert.Contains("phonemes", row.MeaningDetail);
        Assert.False(compare.CanRerun);
        Assert.False(lists.CanParseAgain);
        Assert.Empty(compare.RerunWords);
        Assert.Equal(ParserRefusals.ListExplanation, lists.SelectedList.Sentence);
    }

    [Fact]
    public void SameSpellingWithDifferentGrammaticalInfoKeepsBothReadings()
    {
        var result = new AssessmentWordResult("froid", "analysed", false, "Search completed", 0, "|froid;|froid")
        {
            Readings = [Reading("noun"), Reading("adjective")],
            Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "froid", 0, false, false, false,
                [new ParseAnalysis([new ParseMorph("form", "noun", null, null)]),
                 new ParseAnalysis([new ParseMorph("form", "adjective", null, null)])], []),
        };
        var compare = new CompareViewModel();
        compare.Load([new AssessWordRowViewModel(result)]);

        var row = Assert.Single(compare.Words);
        Assert.Equal(2, row.Readings.Count);
        Assert.Contains("2 analyses", row.WordRow.ReadingCountText);
        Assert.Equal(2, row.WordRow.Row.PanGlossReadingCount);
    }

    [Fact]
    public void InvalidShapeHasAnAnswerAndWhySectionWithoutAnyTerminalAttempt()
    {
        var trace = TraceWordViewModel.FromDiagnosticJson(TraceEnvelope.Of("-", null, invalidShape: true));

        Assert.Equal(ParserRefusals.Title, trace.AnswerText);
        Assert.Equal(Mark.ParserRefusal, trace.AnswerMark);
        Assert.True(trace.HasNoParseReasons);
        Assert.Contains("phonemes", Assert.Single(trace.NoParseReasons));
        Assert.False(trace.NoAttemptRecorded);
    }

    private static ParserReading Reading(string category) => new(
        [new ParserReadingMorph("froid", "cold", category, null, false, null)]);
}
