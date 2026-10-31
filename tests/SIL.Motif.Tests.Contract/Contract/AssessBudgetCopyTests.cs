using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class AssessBudgetCopyTests
{
    private static SetDefaultSelectionRequest Request(int budgetSeconds) =>
        new("project.fwdata", "Default", [], ["word"],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null, budgetSeconds));

    [Fact]
    public void ANonDefaultBudgetSurvivesTheRequestLimitCopies()
    {
        var copied = Request(90) with { };
        Assert.Equal(90, copied.Limits.AssessBudgetSeconds);

        var withMs = new SetDefaultSelectionRequest("project.fwdata", "Default", [], ["word"],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null, 90)) { PerWordLimitMs = 750 };
        Assert.Equal(90, withMs.Limits.AssessBudgetSeconds);
        Assert.Equal(SelectionTimeLimitMode.Explicit, withMs.Limits.TimeMode);

        var withCap = Request(90) with { };
        withCap = new SetDefaultSelectionRequest(withCap.ProjectPath, withCap.Name, [], ["word"], withCap.Limits)
            { PerWordStepLimit = StepCap.Unbounded };
        Assert.Equal(90, withCap.Limits.AssessBudgetSeconds);
    }
}
