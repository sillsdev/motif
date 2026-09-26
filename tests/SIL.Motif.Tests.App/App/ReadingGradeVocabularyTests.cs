using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that one reading grade reads the same in Analyze texts and in Review: a reading the project rejected is
/// "Rejected" in both, while the wire keeps its own value.
/// </summary>
public sealed class ReadingGradeVocabularyTests
{
    private static ParserReading Reading() =>
        new([new ParserReadingMorph("kitabu", "book", "n", null, false, null)]);

    [Fact]
    public void ARejectedReadingReadsRejectedInAnalyzeTextsAndInReview()
    {
        var inText = new ResultsReadingViewModel("kitabu ‘book’", ReadingGrade.Disapproved, isStoredHere: false);
        var inReview = new ReviewAnalysisViewModel(
            new ReviewAnalysis(Reading(), ReadingGrade.Disapproved, Touched: false, Stored: true), ChangeKinds.Approve);

        Assert.Equal("Rejected", inText.GradeLabel);
        Assert.Equal("Rejected", inReview.Opinion);
    }

    [Theory]
    [InlineData(ReadingGrade.Approved, "Approved")]
    [InlineData(ReadingGrade.Disapproved, "Rejected")]
    [InlineData(ReadingGrade.Candidate, "Candidate")]
    [InlineData(ReadingGrade.NoOpinion, "No opinion")]
    [InlineData(null, "")]
    public void EveryGradeHasOneWindowLabel(string? grade, string label) =>
        Assert.Equal(label, ReadingGradeLabels.Of(grade));

    [Fact]
    public void TheWireValuesAreUnchanged()
    {
        Assert.Equal("approved", ReadingGrade.Approved);
        Assert.Equal("disapproved", ReadingGrade.Disapproved);
        Assert.Equal("candidate", ReadingGrade.Candidate);
        Assert.Equal("no-opinion", ReadingGrade.NoOpinion);
    }
}
