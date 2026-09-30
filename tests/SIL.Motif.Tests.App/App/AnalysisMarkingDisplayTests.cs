using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the words Analyze texts shows for a stored analysis and a staged change: FieldWorks' opinion names, never
/// the stored grade's spelling, and an addition said as what it will add.
/// </summary>
public sealed class AnalysisMarkingDisplayTests
{
    [Theory]
    [InlineData(ReadingGrade.Approved, "Approved")]
    [InlineData(ReadingGrade.Disapproved, "Disapproved")]
    [InlineData(ReadingGrade.Candidate, "Unknown")]
    [InlineData(ReadingGrade.NoOpinion, "Unknown")]
    public void AStoredAnalysisNamesItsOpinionInFieldWorksWords(string grade, string expected)
    {
        var analysis = new FieldWorksAnalysisDisplayViewModel(new FieldWorksAnalysisMarking("stored-1", grade, []));

        Assert.Equal(expected, analysis.OpinionLabel);
    }

    [Theory]
    [InlineData(StagedMarkingTransition.NotInFieldWorks, "Approved", true)]
    [InlineData("Unknown", "Approved", false)]
    public void OnlyATransitionFromNothingIsAnAddition(string now, string after, bool isAddition)
    {
        Assert.Equal(isAddition, new StagedMarkingTransition(now, after).IsAddition);
    }
}
