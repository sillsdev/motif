using System.Text.Json;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Logic that decides what to show must read codes and flags, never the words a person reads.</summary>
public sealed class LocalizedLogicIndependenceTests
{
    [Theory]
    [InlineData("Stratum", true)]
    [InlineData("StratumAnalysisInput", true)]
    [InlineData("Word", true)]
    [InlineData("MorphologicalRule", false)]
    [InlineData("", false)]
    public void RuleLevelsAndTheWordAreStructureWhateverTheyAreCalledOnScreen(string type, bool structural) =>
        Assert.Equal(structural, TraceStepKinds.IsStructural(type));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TheShortCompletionComesFromTheCappedAndTimedOutFlags(bool capped, bool timedOut)
    {
        using var json = JsonDocument.Parse(
            $$"""{"form":"w","attempts":1,"passes":1,"elapsed_ns":1000000,"capped":{{capped.ToString().ToLowerInvariant()}},"timed_out":{{timedOut.ToString().ToLowerInvariant()}}}""");
        var row = new StatsRowViewModel(json.RootElement);

        var expected = capped && timedOut ? MotifAppStrings.StatsSearchAndTimeLimits
            : capped ? MotifAppStrings.StatsSearchLimit
            : timedOut ? MotifAppStrings.StatsTimeLimit
            : MotifAppStrings.StatsCompleted;
        Assert.Equal(expected, row.CompletionShort);
    }
}
