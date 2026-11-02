using System.Globalization;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

[Trait("MotifTestLevel", "Unit")]
public sealed class PanGlossTraceDetailsTests
{
    private static PanGlossTraceDetails Details(bool capped, bool timedOut, long? parserWork) =>
        new(capped, timedOut, false, Steps: 42, ElapsedNs: 1, Guessed: false, Categories: []) { ParserWork = parserWork };

    [Fact]
    public void AWorkLimitStopNamesTheAnalysisAttemptsAndTheParserWorkAsDifferentCounts()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var reason = Details(capped: true, timedOut: false, parserWork: 9_000).LimitStopReason;

        Assert.Equal("The parser stopped at the work limit after 42 analysis attempts and 9,000 parser work.", reason);
    }

    [Fact]
    public void AWorkLimitStopWithoutAWorkCountNamesOnlyTheAttempts() =>
        Assert.Equal("The parser stopped at the work limit after 42 analysis attempts.",
            Details(capped: true, timedOut: false, parserWork: null).LimitStopReason);

    [Fact]
    public void AZeroWorkCountIsNotPrintedAsMeasuredWork() =>
        Assert.Equal("The parser stopped at the work limit after 42 analysis attempts.",
            Details(capped: true, timedOut: false, parserWork: 0).LimitStopReason);

    [Fact]
    public void BothLimitsFiringIsSaidRatherThanNamingOnlyTheWorkLimit() =>
        Assert.Equal("The parser stopped at the work limit after 42 analysis attempts, and the time safety net, " +
            "which depends on how fast this computer is, also fired.",
            Details(capped: true, timedOut: true, parserWork: null).LimitStopReason);

    [Fact]
    public void ATimeStopIsTheSafetyNetThatDependsOnThisComputer() =>
        Assert.Equal("The parser stopped at the time safety net, which depends on how fast this computer is.",
            Details(capped: false, timedOut: true, parserWork: 9_000).LimitStopReason);

    [Fact]
    public void ASearchThatNoLimitStoppedHasNoStopReason() =>
        Assert.Null(Details(capped: false, timedOut: false, parserWork: 9_000).LimitStopReason);
}
