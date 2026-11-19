using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

public sealed class LedgerJoinTests
{
    private static LedgerJoin Join(string[] rows, (string, long)[] frequency, string revision = Revision, PinnedFrequencyList? list = null,
        string? declared = null)
    {
        var ledger = LedgerTsvReader.Read(Tsv(rows));
        return LedgerJoiner.Join(ledger, Sidecar(ledger.Rows.Count, ledger.TokenSum, revision), list ?? Frequency(frequency), declared);
    }

    [Fact]
    public void KeepsAReportRowForEveryFrequencyTypeAndTreatsExtraLedgerRowsAsResidual()
    {
        var join = Join([Row("a", 5, "Unverified"), Row("zz", 4, "Open", cls: "affix-chain")],
            [("a", 5), ("b", 7)]);

        Assert.Equal(2, join.EligibleTypes);
        Assert.Equal(12, join.EligibleTokens);
        Assert.Equal((1L, 7L), (join.MissingTypes, join.MissingWeight));
        Assert.True(join.Types.Single(type => type.Key == "b").Missing);
        var extra = Assert.Single(join.Extra);
        Assert.Equal(("zz", 4L), (extra.Key, extra.DeclaredTokens));
        Assert.Equal(1, join.ResidualTypes);
        Assert.Equal(7, join.ResidualWeight);
    }

    [Fact]
    public void CountsADuplicateNormalizedKeyOnceAndAttributesNoJudgement()
    {
        var composed = "é";
        var decomposed = "é";
        var join = Join([Row(composed, 2, "Open"), Row(decomposed, 2, "Unverified")], [(composed, 3)]);

        var type = Assert.Single(join.Types);
        Assert.Equal(3, type.Weight);
        Assert.Equal(2, type.DuplicateRows.Count);
        Assert.Null(type.Row);
        Assert.Equal((1L, 3L), (join.DuplicateTypes, join.DuplicateWeight));
        Assert.Empty(join.Extra);
    }

    [Fact]
    public void JoinsCasedNfcLedgerKeysToNfdFrequencyKeys()
    {
        var join = Join([Row("Éa", 2, "Unverified")], [("Éa", 2)]);

        Assert.True(Assert.Single(join.Types).Resolved);
        Assert.Equal(0, join.ResidualTypes);
    }

    [Fact]
    public void TakesWeightFromTheFrequencyListAndReportsTheSignedDifference()
    {
        var join = Join([Row("a", 9, "Unverified")], [("a", 5)]);

        var conflict = Assert.Single(join.Conflicts);
        Assert.Equal(4, conflict.SignedDifference);
        Assert.Equal(5, Assert.Single(join.Types).Weight);
        Assert.Equal((1L, 5L), (join.ConflictTypes, join.ConflictWeight));
        Assert.Equal(5, join.ResidualWeight);
    }

    [Fact]
    public void FlagsRowsAtAnotherGrammarRevisionAsIncompatibleWithTheirAffectedWeight()
    {
        var join = Join([Row("a", 5, "Unverified", revision: "older"), Row("b", 2, "Unverified")], [("a", 5), ("b", 2)]);

        Assert.Equal((1L, 5L), (join.RevisionTypes, join.RevisionWeight));
        Assert.False(join.Types[0].Resolved);
        Assert.True(join.Types[1].Resolved);
    }

    [Fact]
    public void BlocksTheJoinOnAnInputPolicyMismatchOrAWrongFrequencyDigest()
    {
        var folded = Frequency(("a", 1)) with { Fold = "lower" };
        Assert.Equal([JoinBlock.InputPolicyMismatch], Join([Row("a", 1, "Unverified")], [], list: folded).Blocks);
        var otherTokenizer = Frequency(("a", 1)) with { TokenizerSha256 = new string('b', 64) };
        Assert.Contains(JoinBlock.InputPolicyMismatch, Join([Row("a", 1, "Unverified")], [], list: otherTokenizer).Blocks);
        Assert.Contains(JoinBlock.FrequencyDigestMismatch, Join([Row("a", 1, "Unverified")], [("a", 1)], declared: "other").Blocks);
        Assert.Contains(JoinBlock.DuplicateFrequencyKey, Join([Row("a", 1, "Unverified")], [("a", 1), ("a", 1)]).Blocks);
        Assert.Contains(JoinBlock.EmptyFrequencyList, Join([Row("a", 1, "Unverified")], []).Blocks);
    }

    [Fact]
    public void ABlockedJoinWithholdsEveryStateAndLeavesDoneUnmeasured()
    {
        var ledger = LedgerTsvReader.Read(Tsv(Row("a", 5, "Unverified")));
        var folded = Frequency(("a", 5)) with { Fold = "lower" };

        var report = LedgerCoverage.Build(new(ledger, Sidecar(1, 5), folded));

        Assert.Null(Assert.Single(report.Rows).Decision.State);
        Assert.Equal(DoneVerdictKind.Unmeasured, report.Done.Accounted.Verdict);
        Assert.False(report.ExactTotals);
    }
}
