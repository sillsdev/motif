using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

public sealed class CoverageStateRuleTests
{
    private static readonly string A = Reading(1, 2);
    private static readonly string B = Reading(3, 4);

    private static CoverageDecision Decide(string row, long weight, TypeCheck? check, string word = "w") =>
        Decide(row, weight, check, out _, word);

    private static CoverageDecision Decide(string row, long weight, TypeCheck? check, out LedgerCoverageReport report, string word = "w")
    {
        report = Report([row], [(word, weight)], check is null ? null : new Dictionary<string, TypeCheck> { [word] = check });
        return Assert.Single(report.Rows).Decision;
    }

    [Fact]
    public void ACompleteComparableCheckThatDropsOneOfTwoProtectedReadingsIsLostEvenWhenTheOtherSurvives()
    {
        var decision = Decide(Row("w", 4, "Expected", A + ";" + B), 4, Complete(true, A), out var report);

        Assert.Equal((ReportState.Lost, StateFreshness.Current, 1), (decision.State, decision.Freshness, decision.Rule));
        Assert.Equal([B], decision.MissingReadings);
        Assert.Equal(new Tally(1, 4), report.OpenByMechanism[CoverageStateRule.Unclassified].Current);
        Assert.Equal(new Tally(1, 4), report.Lost.Current);
        Assert.Equal(1, report.ByState.Values.Sum(item => item.Total.Types));
    }

    [Fact]
    public void ALostTypeCountsOnceAsOpenWhateverNumberOfReadingsItLost()
    {
        var report = Report([Row("w", 4, "Expected", A + ";" + B)], [("w", 4)],
            new Dictionary<string, TypeCheck> { ["w"] = Complete(false) });

        Assert.Equal(new Tally(1, 4), report.OpenByMechanism.Values.Single().Current);
        Assert.Equal(2, Assert.Single(report.Rows).Decision.MissingReadings.Count);
        Assert.Equal(new Tally(1, 4), report.RecurringOpenCurrent);
    }

    [Fact]
    public void APartialCheckProvesPresenceButNeverAbsence()
    {
        var partial = new TypeCheck(CheckCompleteness.Partial, true, true, false, [A]);

        var decision = Decide(Row("w", 4, "Expected", A + ";" + B), 4, partial);

        Assert.Equal((ReportState.Expected, StateFreshness.Current, 4), (decision.State, decision.Freshness, decision.Rule));
        Assert.Empty(decision.MissingReadings);
        Assert.True(decision.ComparisonUnmeasured);
    }

    [Fact]
    public void APartialCheckWithNoProvedReadingKeepsThePriorStateStaleInsteadOfOpeningIt()
    {
        var partial = new TypeCheck(CheckCompleteness.Partial, true, false);

        var decision = Decide(Row("w", 4, "Unverified"), 4, partial);

        Assert.Equal((ReportState.Unverified, StateFreshness.Stale, 8), (decision.State, decision.Freshness, decision.Rule));
    }

    [Fact]
    public void ACheckAtAnotherGrammarProvesNeitherAbsenceNorPresence()
    {
        var other = new TypeCheck(CheckCompleteness.Complete, false, false, false, []);

        var decision = Decide(Row("w", 4, "Expected", A), 4, other);

        Assert.Equal((ReportState.Expected, StateFreshness.Stale, 8), (decision.State, decision.Freshness, decision.Rule));
        Assert.Empty(decision.MissingReadings);
        Assert.Contains("incompatible-check", decision.Reasons);
    }

    [Fact]
    public void AnUnmeasuredRecheckOfALostTypeKeepsLostStaleWithItsSingleOpenCount()
    {
        var unmeasuredCheck = new TypeCheck(CheckCompleteness.Partial, true, true, false, null);

        var decision = Decide(Row("w", 4, "Lost", A, "missing-reading"), 4, unmeasuredCheck, out var report);

        Assert.Equal((ReportState.Lost, StateFreshness.Stale, 2), (decision.State, decision.Freshness, decision.Rule));
        Assert.True(decision.PresenceProved);
        Assert.Equal(new Tally(1, 4), report.OpenByMechanism["missing-reading"].Stale);
        Assert.Equal(new Tally(1, 4), report.Lost.Stale);
        Assert.Equal(new Tally(1, 4), report.RecurringOpenStale);
        Assert.Equal(default, report.RecurringOpenCurrent);
    }

    [Fact]
    public void ACompleteCheckThatRestoresEveryProtectedReadingClearsAPriorLoss()
    {
        var decision = Decide(Row("w", 4, "Lost", A + ";" + B, "missing-reading"), 4, Complete(true, A, B));

        Assert.Equal((ReportState.Expected, StateFreshness.Current), (decision.State, decision.Freshness));
    }

    [Fact]
    public void ALostTypeStillLostAtTheCurrentGrammarIsCurrent()
    {
        var decision = Decide(Row("w", 4, "Lost", A + ";" + B, "missing-reading"), 4, Complete(true, A));

        Assert.Equal((ReportState.Lost, StateFreshness.Current, 1), (decision.State, decision.Freshness, decision.Rule));
        Assert.Equal("missing-reading", decision.Mechanism);
    }

    [Theory]
    [InlineData("name", "e", "name")]
    [InlineData("misspelling", "intended=wat | note", "misspelling")]
    [InlineData("misspelling", "intended=wat", "misspelling")]
    [InlineData("artifact", "split by the tokenizer", "non-word")]
    [InlineData("abbreviation", "e", "abbreviation")]
    [InlineData("numeral", "e", "numeral")]
    public void ASupportedDispositionAccountsForTheTypeOnceACompatibleCheckCompleted(string cls, string evidence, string kind)
    {
        var decision = Decide(Row("w", 4, "Disposition", "", cls, evidence), 4, Complete(false));

        Assert.Equal((ReportState.Disposition, StateFreshness.Current, 3, kind), (decision.State, decision.Freshness, decision.Rule, decision.DispositionKind));
    }

    [Theory]
    [InlineData("misspelling", "")]
    [InlineData("misspelling", "intended=")]
    [InlineData("misspelling", "looks wrong")]
    [InlineData("metalinguistic", "e")]
    [InlineData("name", "")]
    public void AnUnsupportedDispositionGrantsNoClosureAndStaysOpen(string cls, string evidence)
    {
        var decision = Decide(Row("w", 4, "Disposition", "", cls, evidence), 4, null, out var report);

        Assert.Equal((ReportState.Open, StateFreshness.Stale, OpenKind.UnsupportedDisposition), (decision.State, decision.Freshness, decision.Open));
        Assert.Equal(new Tally(1, 4), report.RecurringOpenStale);
    }

    [Fact]
    public void ADispositionWithoutACheckIsKeptButStale()
    {
        var decision = Decide(Row("w", 4, "Disposition", "", "name", "e"), 4, null);

        Assert.Equal((ReportState.Disposition, StateFreshness.Stale, "name"), (decision.State, decision.Freshness, decision.DispositionKind));
    }

    [Fact]
    public void AnEvidencedWrongOnlyOutcomeIsOpenAndCurrent()
    {
        var wrong = new TypeCheck(CheckCompleteness.Complete, true, true, true, [Reading(9, 10)]);

        var decision = Decide(Row("w", 4, "Unverified"), 4, wrong);

        Assert.Equal((ReportState.Open, StateFreshness.Current, 5, OpenKind.WrongOnly), (decision.State, decision.Freshness, decision.Rule, decision.Open));
    }

    [Fact]
    public void AProvedReadingWithNoJudgementIsUnverifiedAndCurrent()
    {
        var decision = Decide(Row("w", 4, "Open", "", "clitic"), 4, Complete(true, A));

        Assert.Equal((ReportState.Unverified, StateFreshness.Current, 6), (decision.State, decision.Freshness, decision.Rule));
    }

    [Fact]
    public void ACompletedZeroResultIsOpenNoAnalysisWithTheLedgerMechanismOrUnclassified()
    {
        var withMechanism = Decide(Row("w", 4, "Open", "", "clitic"), 4, Complete(false));
        var without = Decide(Row("w", 4, "Unverified"), 4, Complete(false));

        Assert.Equal((ReportState.Open, StateFreshness.Current, 7, OpenKind.NoAnalysis, "clitic"),
            (withMechanism.State, withMechanism.Freshness, withMechanism.Rule, withMechanism.Open, withMechanism.Mechanism));
        Assert.Equal(CoverageStateRule.Unclassified, without.Mechanism);
    }

    [Fact]
    public void WithNoPriorStateAnUnmeasuredTypeHasNullStateAndUnavailableFreshness()
    {
        var report = Report([Row("other", 1, "Unverified")], [("w", 4)]);

        var decision = Assert.Single(report.Rows).Decision;

        Assert.Null(decision.State);
        Assert.Equal(StateFreshness.Unavailable, decision.Freshness);
        Assert.Equal(new Tally(1, 4), report.NoState);
    }

    [Fact]
    public void AnEmptyExpectedCellLeavesTheExpectedClaimUnmeasured()
    {
        var decision = Decide(Row("w", 4, "Expected"), 4, Complete(true, A));

        Assert.Equal(ReportState.Unverified, decision.State);
        Assert.Contains("expected-readings-empty", decision.Reasons);
    }

    [Fact]
    public void AnUnsupportedDispositionWithAPresentCheckBecomesUnverifiedNotOpen()
    {
        var decision = Decide(Row("w", 4, "Disposition", "", "metalinguistic", "e"), 4, Complete(true, A));

        Assert.Equal((ReportState.Unverified, StateFreshness.Current, 6), (decision.State, decision.Freshness, decision.Rule));
        Assert.Null(decision.DispositionKind);
    }

    [Theory]
    [InlineData("Expected")]
    [InlineData("Lost")]
    public void AnExplicitEmptyReadingSetOnACompleteCheckProvesEveryProtectedReadingLost(string state)
    {
        var zero = Complete(false);

        var decision = Decide(Row("w", 4, state, A + ";" + B, "missing-reading"), 4, zero);

        Assert.Equal((ReportState.Lost, StateFreshness.Current, 1), (decision.State, decision.Freshness, decision.Rule));
        Assert.Equal(2, decision.MissingReadings.Count);
        Assert.Equal(OpenKind.Lost, decision.Open);
    }
}
