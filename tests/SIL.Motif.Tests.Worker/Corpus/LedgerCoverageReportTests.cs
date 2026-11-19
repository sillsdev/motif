using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

public sealed class LedgerCoverageReportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-ledger-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly string A = Reading(1, 2);

    private static IReadOnlyDictionary<string, TypeCheck> Checks(params (string, TypeCheck)[] checks) =>
        checks.ToDictionary(item => item.Item1, item => item.Item2);

    [Fact]
    public void TalliesTypesAndTokensByStateDispositionKindMechanismAndSingletonResidual()
    {
        var rows = new[]
        {
            Row("e", 6, "Expected", A),
            Row("u", 20, "Unverified"),
            Row("n", 2, "Disposition", "", "name", "ev"),
            Row("m", 3, "Disposition", "", "misspelling", "intended=x"),
            Row("o1", 5, "Open", "", "clitic"),
            Row("o2", 3, "Open"),
            Row("s1", 1, "Open", "", "clitic"),
            Row("s2", 1, "Open"),
            Row("none", 4, "Unverified"),
        };
        var freq = rows.Select(row => (row.Split('\t')[0], long.Parse(row.Split('\t')[1]))).ToArray();
        var checks = Checks(("e", Complete(true, A)), ("u", Complete(true, A)), ("n", Complete(false)), ("m", Complete(false)),
            ("o1", Complete(false)), ("o2", Complete(false)), ("s1", Complete(false)), ("s2", Complete(false)), ("none", Complete(false)));

        var report = Report(rows, freq, checks);

        Assert.Equal(45, report.Join.EligibleTokens);
        Assert.Equal(new Tally(1, 6), report.ByState[ReportState.Expected].Current);
        Assert.Equal(new Tally(1, 20), report.ByState[ReportState.Unverified].Current);
        Assert.Equal(new Tally(2, 5), report.ByState[ReportState.Disposition].Current);
        Assert.Equal(new Tally(1, 2), report.DispositionByKind["name"].Current);
        Assert.Equal(new Tally(1, 3), report.DispositionByKind["misspelling"].Current);
        Assert.Equal(new Tally(5, 14), report.ByState[ReportState.Open].Current);
        Assert.Equal(new Tally(5, 14), report.NoAnalysis.Current);
        Assert.Equal(new Tally(2, 6), report.OpenByMechanism["clitic"].Current);
        Assert.Equal(new Tally(3, 8), report.OpenByMechanism[CoverageStateRule.Unclassified].Current);
        Assert.Equal(new Tally(3, 12), report.RecurringOpenCurrent);
        Assert.Equal(new Tally(2, 2), report.OpenSingletons);
        Assert.Equal(2d / 45, report.OpenSingletonTokenShare, 12);
        Assert.Equal(new Tally(2, 26), report.Reach);
        Assert.True(report.ExactTotals);
    }

    [Fact]
    public void RanksRecurringOpenTypesByDescendingWeightThenExactKeyWithStaleLabelledSeparately()
    {
        var rows = new[]
        {
            Row("b", 5, "Open"), Row("a", 5, "Open"), Row("c", 9, "Open"), Row("d", 2, "Unverified"), Row("s", 1, "Open"),
        };
        var freq = rows.Select(row => (row.Split('\t')[0], long.Parse(row.Split('\t')[1]))).ToArray();
        var checks = Checks(("b", Complete(false)), ("a", Complete(false)), ("d", Complete(true, A)), ("s", Complete(false)));

        var report = Report(rows, freq, checks);

        Assert.Equal(["c", "a", "b"], report.RankedRecurringOpen.Select(item => item.Key));
        Assert.Equal([true, false, false], report.RankedRecurringOpen.Select(item => item.Stale));
        Assert.DoesNotContain(report.RankedRecurringOpen, item => item.Key == "s");
        Assert.Equal(new Tally(2, 10), report.RecurringOpenCurrent);
        Assert.Equal(new Tally(1, 9), report.RecurringOpenStale);
    }

    [Fact]
    public void ExactTotalsAreUnavailableWhileAnyStateIsStaleOrUnresolved()
    {
        var report = Report([Row("a", 5, "Unverified")], [("a", 5)]);

        Assert.False(report.ExactTotals);
        Assert.Equal(new Tally(1, 5), report.ByState[ReportState.Unverified].Stale);
    }

    [Fact]
    public void AccountedFailsOnAProvedRecurringOpenTypeAndCountsLostOnce()
    {
        var report = Report([Row("a", 5, "Expected", A), Row("b", 1, "Open")], [("a", 5), ("b", 1)],
            Checks(("a", Complete(false)), ("b", Complete(false))));

        Assert.Equal(DoneVerdictKind.Fail, report.Done.Accounted.Verdict);
        Assert.Equal(1, report.Done.Accounted.Counts["recurringOpenTypes"]);
        Assert.Equal(1, report.Done.Accounted.Counts["openSingletonTypes"]);
        Assert.Equal(DoneVerdictKind.Fail, report.Done.Verdict);
    }

    [Fact]
    public void AStaleCarriedRecurringOpenTypeLeavesAccountedUnmeasuredAndKeepsTheStaleSubtotal()
    {
        var report = Report([Row("a", 5, "Open", "", "clitic"), Row("b", 4, "Unverified")], [("a", 5), ("b", 4)]);

        Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Accounted.Verdict);
        Assert.Contains("recurring-open-unproved", report.Done.Accounted.Reasons);
        Assert.Equal(1, report.Done.Accounted.Counts["recurringOpenStaleTypes"]);
        Assert.Equal(0, report.Done.Accounted.Counts["recurringOpenCurrentTypes"]);
        Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Verdict);
    }

    [Fact]
    public void AStaleLostRecurringTypeLeavesAccountedUnmeasuredBecauseNoCurrentCheckProvesTheLoss()
    {
        var report = Report([Row("a", 5, "Lost", A, "missing-reading")], [("a", 5)]);

        Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Accounted.Verdict);
        Assert.Contains("recurring-lost-stale", report.Done.Accounted.Reasons);
        Assert.Equal(1, report.Done.Accounted.Counts["recurringLostStaleTypes"]);
        Assert.Equal(1, report.Done.Accounted.Counts["recurringOpenStaleTypes"]);
    }

    [Fact]
    public void ACurrentRecurringOpenTypeFailsAccountedWithItsOwnReason()
    {
        var report = Report([Row("a", 5, "Open")], [("a", 5)], Checks(("a", Complete(false))));

        Assert.Equal(DoneVerdictKind.Fail, report.Done.Accounted.Verdict);
        Assert.Equal(["recurring-open-current"], report.Done.Accounted.Reasons);
    }

    [Fact]
    public void ACompleteComparableCheckWithoutAProducedReadingSetIsRefusedAsMalformed()
    {
        var malformed = new TypeCheck(CheckCompleteness.Complete, true, false, false, null);

        var exception = Assert.Throws<LedgerRefusalException>(() =>
            Report([Row("a", 5, "Expected", A)], [("a", 5)], Checks(("a", malformed))));

        Assert.Equal(LedgerRefusalCode.CheckMalformed, exception.Code);
    }

    [Fact]
    public void AnUnsupportedDispositionOnARecurringTypeLeavesAccountedUnmeasuredWithItsOwnReason()
    {
        var report = Report([Row("a", 5, "Disposition", "", "metalinguistic", "ev")], [("a", 5)]);

        Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Accounted.Verdict);
        Assert.Contains("recurring-disposition-in-review", report.Done.Accounted.Reasons);
        Assert.DoesNotContain("recurring-open-unproved", report.Done.Accounted.Reasons);
        Assert.Equal(1, report.Done.Accounted.Counts["recurringUnsupportedDispositionTypes"]);
        Assert.Equal(1, report.Done.Accounted.Counts["recurringOpenStaleTypes"]);
    }

    [Fact]
    public void AccountedPassesOnlyWithZeroRecurringOpenAndCurrentChecksOnEveryRecurringType()
    {
        var rows = new[] { Row("a", 5, "Unverified"), Row("s", 1, "Open") };
        var checks = Checks(("a", Complete(true, A)), ("s", Complete(false)));

        var report = Report(rows, [("a", 5), ("s", 1)], checks);

        Assert.Equal(DoneVerdictKind.Pass, report.Done.Accounted.Verdict);
        Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Verdict);
    }

    [Fact]
    public void AZeroSubtotalNeverPassesWhileAJoinOrCheckIsMissing()
    {
        var noChecks = Report([Row("a", 5, "Unverified")], [("a", 5)]);
        var missingRow = Report([Row("zzz", 1, "Unverified")], [("a", 5)], Checks(("a", Complete(true, A))));
        var conflicting = Report([Row("a", 7, "Unverified")], [("a", 5)], Checks(("a", Complete(true, A))));
        var incompatible = Report([Row("a", 5, "Unverified", revision: "older")], [("a", 5)], Checks(("a", Complete(true, A))));

        foreach (var report in new[] { noChecks, missingRow, conflicting, incompatible })
        {
            Assert.Equal(0, report.Done.Accounted.Counts["recurringOpenTypes"]);
            Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Accounted.Verdict);
        }
        Assert.Contains("recurring-types-without-current-check", noChecks.Done.Accounted.Reasons);
        Assert.Contains("ledger-rows-missing", missingRow.Done.Accounted.Reasons);
        Assert.Contains("conflicting-weights", conflicting.Done.Accounted.Reasons);
        Assert.Contains("incompatible-revisions", incompatible.Done.Accounted.Reasons);
    }

    [Fact]
    public void CorrectAndStableAreUnmeasuredWhenTheirArtifactsAreAbsent()
    {
        var report = Report([Row("a", 5, "Unverified")], [("a", 5)], Checks(("a", Complete(true, A))));

        Assert.Equal(["audit-sample-absent"], report.Done.Correct.Reasons);
        Assert.Equal(["protected-set-absent"], report.Done.Stable.Reasons);
        Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Verdict);
    }

    private string WriteAudit(int size, int right, int wrongOnly, int miss, Action<Dictionary<string, object>>? tweak = null,
        string? grammarRevision = null, int extra = 0)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var ids = Enumerable.Range(0, size).Select(index => "occ-" + index).ToArray();
        var selection = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(ids.Select(id => id + "\n")))))
            .ToLowerInvariant();
        File.WriteAllLines(Path.Combine(directory, "selection.jsonl"),
            ids.Select(id => $"{{\"occurrenceId\":\"{id}\",\"source\":\"s\",\"documentVersion\":\"1\",\"location\":\"l\"}}"));
        var manifest = new Dictionary<string, object>
        {
            ["sources"] = new[] { "s1" }, ["membershipDigest"] = "m", ["frequencySha256"] = "f-digest", ["tokenizerSha256"] = Pin,
            ["samplingMethod"] = "uniform", ["seed"] = 7, ["selectionDigest"] = selection, ["scorerId"] = "scorer",
            ["scoringPolicyVersion"] = "1", ["referenceRevision"] = "r1", ["referenceDigest"] = "ref",
        };
        tweak?.Invoke(manifest);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
        var outcomes = ids.Select((id, index) => new
        {
            occurrenceId = id, selectionDigest = selection, referenceDigest = "ref", grammarRevision = grammarRevision ?? Revision,
            outcome = index < right ? "right" : index < right + wrongOnly ? "wrong-only" : index < right + wrongOnly + miss ? "miss" : "unmeasured",
        }).Where(row => row.outcome != "unmeasured").Select(row => System.Text.Json.JsonSerializer.Serialize(row))
            .Concat(Enumerable.Range(0, extra).Select(index => "{\"occurrenceId\":\"stray-" + index + "\"}"));
        File.WriteAllLines(Path.Combine(directory, "outcomes.jsonl"), outcomes);
        return directory;
    }

    private DoneTest Correct(string directory) =>
        Report([Row("a", 5, "Unverified")], [("a", 5)], null, directory).Done.Correct;

    [Fact]
    public void CorrectPassesAtTheExactThresholds()
    {
        var test = Correct(WriteAudit(1500, 1470, 15, 15));

        Assert.Equal(DoneVerdictKind.Pass, test.Verdict);
        Assert.Equal(1470, test.Counts["right"]);
        Assert.Equal(15, test.Counts["wrongOnly"]);
        Assert.Equal(1500, test.Counts["size"]);
    }

    [Fact]
    public void CorrectFailsWhenWrongOnlyExceedsOnePercentOrRightCannotReachNinetyEight()
    {
        Assert.Equal(DoneVerdictKind.Fail, Correct(WriteAudit(1500, 1470, 16, 14)).Verdict);
        Assert.Equal(DoneVerdictKind.Fail, Correct(WriteAudit(1500, 1400, 0, 100)).Verdict);
    }

    [Fact]
    public void CorrectStaysInTheDenominatorAndIsUnmeasuredWhenUnmeasuredRowsCouldDecideIt()
    {
        var test = Correct(WriteAudit(1500, 1400, 0, 0));

        Assert.Equal(DoneVerdictKind.Unmeasured, test.Verdict);
        Assert.Equal(100, test.Counts["unmeasured"]);
    }

    [Fact]
    public void CorrectIsUnmeasuredForASmallSampleBadDigestWrongPinsOrAnotherGrammar()
    {
        Assert.Equal(["audit-sample-too-small"], Correct(WriteAudit(1499, 1499, 0, 0)).Reasons);
        Assert.Equal(["audit-selection-digest-mismatch"], Correct(WriteAudit(1500, 1500, 0, 0, m => m["selectionDigest"] = "0")).Reasons);
        Assert.Equal(["audit-pins-mismatch"], Correct(WriteAudit(1500, 1500, 0, 0, m => m["frequencySha256"] = "other")).Reasons);
        Assert.Equal(["audit-pins-incomplete"], Correct(WriteAudit(1500, 1500, 0, 0, m => m.Remove("scorerId"))).Reasons);
        var otherGrammar = Correct(WriteAudit(1500, 1500, 0, 0, grammarRevision: "another"));
        Assert.Equal(DoneVerdictKind.Unmeasured, otherGrammar.Verdict);
        Assert.Equal(1500, otherGrammar.Counts["unmeasured"]);
    }

    [Fact]
    public void CorrectReportsAnExtraOutcomeAsAReconciliationResidualThatBlocksAPass()
    {
        var test = Correct(WriteAudit(1500, 1500, 0, 0, extra: 2));

        Assert.Equal(DoneVerdictKind.Unmeasured, test.Verdict);
        Assert.Equal(2, test.Counts["extraOutcomes"]);
    }

    private string WriteProtected(string reading, string? digestOverride = null, string revision = Revision)
    {
        var pairs = new[] { new { type = "a", reading } };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("a\t" + reading + "\n"))).ToLowerInvariant();
        return WriteJson(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json"),
            new { grammarRevision = revision, digest = digestOverride ?? digest, pairs });
    }

    private static DoneTest Stable(string path, TypeCheck check) =>
        Report([Row("a", 5, "Unverified")], [("a", 5)], Checks(("a", check)), null, path).Done.Stable;

    [Fact]
    public void StablePassesWhenEveryProtectedReadingSurvivesAndFailsOnAProvedLoss()
    {
        Assert.Equal(DoneVerdictKind.Pass, Stable(WriteProtected(A), Complete(true, A, Reading(7, 8))).Verdict);
        var lost = Stable(WriteProtected(A), Complete(true, Reading(7, 8)));
        Assert.Equal(DoneVerdictKind.Fail, lost.Verdict);
        Assert.Equal(1, lost.Counts["lost"]);
    }

    [Fact]
    public void StableIsUnmeasuredWithoutACompleteComparableCheckWithReadingIdentity()
    {
        var path = WriteProtected(A);
        Assert.Equal(DoneVerdictKind.Unmeasured, Stable(path, new(CheckCompleteness.Partial, true, true, false, [A])).Verdict);
        Assert.Equal(DoneVerdictKind.Unmeasured, Stable(path, new(CheckCompleteness.Complete, false, true, false, [A])).Verdict);
        Assert.Equal(["protected-set-digest-mismatch"], Stable(WriteProtected(A, "0"), Complete(true, A)).Reasons);
        Assert.Equal(["protected-set-revision-mismatch"], Stable(WriteProtected(A, revision: "x"), Complete(true, A)).Reasons);
    }

    [Fact]
    public void ConjunctionFailsOnAnyFailAndPassesOnlyWhenAllThreePass()
    {
        var pass = DoneVerdictKind.Pass;
        var fail = DoneVerdictKind.Fail;
        var none = DoneVerdictKind.Unmeasured;
        Assert.Equal(pass, DoneResult.Conjunction(pass, pass, pass));
        Assert.Equal(fail, DoneResult.Conjunction(pass, none, fail));
        Assert.Equal(none, DoneResult.Conjunction(pass, pass, none));
    }

    [Fact]
    public void AllThreePassingCertifiesDone()
    {
        var rows = new[] { Row("a", 5, "Unverified") };
        var report = Report(rows, [("a", 5)], Checks(("a", Complete(true, A))), WriteAudit(1500, 1500, 0, 0), WriteProtected(A));

        Assert.Equal(DoneVerdictKind.Pass, report.Done.Verdict);
    }
}
