using System.Diagnostics;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsimony.RuleAttempts;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony.RuleAttempts;

public sealed class RedundancyAndBlockingTests
{
    [Fact]
    public void PolarityFlipsWhenAMajorityOfCandidateHostsCarryTheTag()
    {
        var flips = RedundancyMeasures.PolarityFlips([
            new TagPolarityFacts("d>r", "r1", 400, 395), new TagPolarityFacts("rare", "r1", 400, 20),
            new TagPolarityFacts("empty", "r1", 0, 0)]);
        Assert.Equal("d>r", Assert.Single(flips).TagId);
    }

    [Fact]
    public void ATagIsImpliedOnlyWhenEveryCarrierMatchesTheContext()
    {
        var implied = RedundancyMeasures.ImpliedByContext([
            new TagContextFacts("cluster", "r1", 1205, 1205), new TagContextFacts("other", "r1", 10, 9),
            new TagContextFacts("none", "r1", 0, 0)]);
        Assert.Equal("cluster", Assert.Single(implied).TagId);
    }

    [Fact]
    public void NearDuplicatesFoldCaseAndIyY()
    {
        var groups = RedundancyMeasures.NearDuplicateLexemes([
            new("1", "kumpaniya"), new("2", "kumpanya"), new("3", "Nun"), new("4", "nun"), new("5", "same"),
            new("6", "same")]);
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void NothingBlocksByDefaultOrOutsideAutonomousMode()
    {
        var fired = new[] { (BlockingPolicy.RuleAttemptMeasureId, false), ("R-word-negative-accepted", true) };
        Assert.False(BlockingPolicy.Advisory.Decide(true, fired).Blocked);
        var optedIn = new BlockingPolicy([BlockingPolicy.RuleAttemptMeasureId, "P-adhoc-duplicate"]);
        Assert.False(optedIn.Decide(false, fired).Blocked);
        Assert.Equal([BlockingPolicy.RuleAttemptMeasureId], optedIn.Decide(true, fired).BlockingMeasureIds);
    }

    [Fact]
    public void AWrongReadingMeasureBlocksOnlyWithAMeasuredWrongReading()
    {
        var policy = new BlockingPolicy(["R-word-negative-accepted"]);
        Assert.False(policy.Decide(true, [("R-word-negative-accepted", false)]).Blocked);
        Assert.True(policy.Decide(true, [("R-word-negative-accepted", true)]).Blocked);
    }

    [Fact]
    public void TrendSumsObjectsByClass()
    {
        var rows = SweepTrend.Rows(new DateOnly(2026, 11, 13), [
            new ParsimonyMeasureRun("a", ParsimonyMeasureStatus.Computed, 100, 3, "allomorphs", null, null, null),
            new ParsimonyMeasureRun("b", ParsimonyMeasureStatus.Computed, 100, 2, "allomorphs", null, null, null),
            new ParsimonyMeasureRun("c", ParsimonyMeasureStatus.NotAvailable, null, null, "entries", null, null, null)]);
        Assert.Equal(["2026-11-13\tallomorphs\t100\t5"], rows);
    }

    [Fact]
    public void CebScaleChecksRunWellUnderOneSecond()
    {
        var entries = Enumerable.Range(0, 8000).Select(i => new LexemeForm($"e{i}", i % 50 == 0 ? $"Kumpaniya{i / 2}" : $"w{i}iy")).ToList();
        var variants = Enumerable.Range(0, 62000).Select(i => new AddedObjectShape($"v{i}", "t", ["e"], [],
            StringCorrespondence.Of("kata" + i + "a", "kata" + i + "-"))).ToList();
        var tags = Enumerable.Range(0, 1200).Select(i => new TagPolarityFacts($"t{i}", "r", 500, i % 500)).ToList();
        var watch = Stopwatch.StartNew();
        RedundancyMeasures.NearDuplicateLexemes(entries);
        SameShapeGroups.Find(variants);
        RedundancyMeasures.PolarityFlips(tags);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
    }
}
