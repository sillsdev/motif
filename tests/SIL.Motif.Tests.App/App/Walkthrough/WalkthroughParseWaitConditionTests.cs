using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class WalkthroughParseWaitConditionTests
{
    [Theory]
    [InlineData(false, RunState.Completed, true)]
    [InlineData(false, RunState.Running, false)]
    [InlineData(true, RunState.Running, true)]
    public void ParseWaitAcceptsVisibleProgressOrCompletedOutcome(
        bool progressVisible,
        RunState assessmentState,
        bool expected)
    {
        Assert.Equal(expected, WalkthroughReplay.ParseProgressVisibleOrCompleted(
            progressVisible, assessmentState));
    }
}
