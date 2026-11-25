using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A project's parser work limit replaces the one a saved Selection took, whichever way its time cap is set.</summary>
public sealed class SelectionWorkLimitOverrideTests
{
    [Fact]
    public void ANewWorkLimitReplacesTheSavedOneAndKeepsTheTimePolicy()
    {
        var saved = SelectionLimitPolicy.Explicit(ParserWorkLimit.Default, 2500);

        var changed = SelectionLimitPolicy.WithWorkLimit(saved, new ParserWorkLimit(60_000_000));

        Assert.Equal(new ParserWorkLimit(60_000_000), changed.PerWordParserWorkLimit);
        Assert.Equal(SelectionTimeLimitMode.Explicit, changed.TimeMode);
        Assert.Equal(2500, changed.ExplicitPerWordLimitMs);
        Assert.Null(changed.ValidationError());
    }

    [Fact]
    public void NoWorkLimitOnAnExplicitTimeSelectionFallsBackToTheEstimatedTimeCap()
    {
        var saved = SelectionLimitPolicy.Explicit(ParserWorkLimit.Default, 2500);

        var changed = SelectionLimitPolicy.WithWorkLimit(saved, ParserWorkLimit.Unbounded);

        Assert.True(changed.PerWordParserWorkLimit.IsUnbounded);
        Assert.Equal(SelectionTimeLimitMode.Estimated, changed.TimeMode);
        Assert.Null(changed.ExplicitPerWordLimitMs);
        Assert.Null(changed.ValidationError());
    }
}
