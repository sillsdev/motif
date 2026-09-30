using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the window's one label per reading grade in Analyze texts, while the wire keeps its own value. Review rows
/// show no grade label: their staged note carries the opinion.
/// </summary>
public sealed class ReadingGradeVocabularyTests
{
    [Fact]
    public void ARejectedReadingReadsRejectedInAnalyzeTexts()
    {
        var inText = new ResultsReadingViewModel("kitabu ‘book’", ReadingGrade.Disapproved, isStoredHere: false);

        Assert.Equal("Rejected", inText.GradeLabel);
    }

    [Theory]
    [InlineData(ReadingGrade.Approved, "Approved")]
    [InlineData(ReadingGrade.Disapproved, "Rejected")]
    [InlineData(ReadingGrade.Candidate, "Candidate")]
    [InlineData(ReadingGrade.NoOpinion, "Not present")]
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
