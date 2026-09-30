using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class AnalysisActionRulesTests
{
    [Theory]
    [InlineData(AnalysisMarkingActionKind.RemoveAnalysis, AnalysisMarkingClass.Different, false)]
    [InlineData(AnalysisMarkingActionKind.KeepFieldWorks, AnalysisMarkingClass.Extra, false)]
    [InlineData(AnalysisMarkingActionKind.Approve, AnalysisMarkingClass.Same, true)]
    [InlineData(AnalysisMarkingActionKind.Disapprove, AnalysisMarkingClass.Same, true)]
    public void PrimaryActionsCountTowardNeedsALookOnlyWhenTheyAskForAReview(
        AnalysisMarkingActionKind kind, AnalysisMarkingClass comparison, bool expected)
    {
        var state = State(comparison, new AnalysisMarkingAction(
            kind, "action", null, null, null, "Before", "After", null));

        Assert.Equal(expected, state.NeedsALook);
    }

    [Theory]
    [InlineData(AnalysisMarkingActionKind.RemoveAnalysis, false)]
    [InlineData(AnalysisMarkingActionKind.KeepFieldWorks, false)]
    [InlineData(AnalysisMarkingActionKind.Disapprove, true)]
    public void FixChoicesCountTowardNeedsALookOnlyWhenTheyAskForAReview(
        AnalysisMarkingActionKind kind, bool expected)
    {
        var choice = new AnalysisMarkingChoice(
            kind, "choice", "details", "stored-analysis", null, null, "Before", "After", null);
        var state = State(AnalysisMarkingClass.Different, null, [choice]);

        Assert.Equal(expected, state.NeedsALook);
    }

    private static AnalysisMarkingState State(AnalysisMarkingClass comparison,
        AnalysisMarkingAction? primary, IReadOnlyList<AnalysisMarkingChoice>? choices = null) =>
        new([], comparison, [], primary, choices ?? [], [], false, false, true);
}
