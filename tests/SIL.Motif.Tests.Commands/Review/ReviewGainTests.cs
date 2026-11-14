using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewGainTests
{
    [Fact]
    public void AWordInTheTextsIsAnAttestedGainAndAGeneratedProbeIsNot()
    {
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("written", false, true, [Token("t1")]));
        accumulator.Add(Case("probe", false, true, [Cell("c1")]));
        accumulator.Add(Case("touched", false, true, []));
        var facts = accumulator.Snapshot();
        Assert.Equal(1, facts.AttestedGained);
        Assert.Equal(2, facts.GeneratedGained);
    }

    [Fact]
    public void AnApprovedAnalysisMakesAGainedWordAttestedWithoutATokenMembership()
    {
        var approved = new ReviewAssertion("a", "reading", null, ReferenceAuthority.Human, "actor", "rev", true, false, true, []);
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("approved", false, true, []) with { Assertions = [approved] });
        accumulator.Add(Case("reference", false, true, [new("ref", "r", "reference", 1, null, null, [])]));
        var facts = accumulator.Snapshot();
        Assert.Equal(1, facts.AttestedGained);
        Assert.Equal(1, facts.GeneratedGained);
    }

    [Fact]
    public void OnlyANoParseToParseChangeIsAGain()
    {
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("already", true, true, [Token("a")]));
        accumulator.Add(Case("lost", true, false, [Token("b")]));
        accumulator.Add(Case("still", false, false, [Token("c")]));
        accumulator.Add(Case("unknown", false, null, [Token("d")]));
        var facts = accumulator.Snapshot();
        Assert.Equal(0, facts.AttestedGained);
        Assert.Equal(0, facts.GeneratedGained);
    }

    [Fact]
    public void ADuplicateCaseAndAWordInTwoSetsCountOnce()
    {
        var accumulator = new ReviewSummaryAccumulator();
        var both = Case("both", false, true, [Cell("c"), Token("t")]);
        accumulator.Add(both);
        accumulator.Add(both);
        var facts = accumulator.Snapshot();
        Assert.Equal(1, facts.AttestedGained);
        Assert.Equal(0, facts.GeneratedGained);
    }

    [Fact]
    public void AFrozenHoldPolicySurvivesTheArtifactAndMakesThePublisherHold()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-gain-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = new SIL.Motif.Contract.Projects.ProjectLocator(Path.Combine(root, "p.fwdata"), "p");
            using var database = SIL.Motif.Host.Store.MotifDatabase.OpenOwned(Path.Combine(root, "p.db"), project,
                SIL.Motif.Host.Store.MotifSchema.CurrentSchema, new Version(1, 0));
            var policy = new GainGatePolicy(GainGateMode.Hold, 3);
            var inputs = new FrozenReviewInputs("project", "proposal/x", "scope", "manifest", null, [], [], []) { GainGate = policy };
            var digest = "sha256:" + new string('a', 64);
            var baseline = new SIL.Motif.Contract.Baselines.BaselineToken("project", digest, "v1", "2026-01-01T00:00:00Z", digest);
            var reference = SIL.Motif.Worker.Jobs.ReviewInputArtifacts.Store(database,
                new SIL.Motif.Worker.Jobs.ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
            var stored = SIL.Motif.Worker.Jobs.ReviewInputArtifacts.Read(database, reference).Inputs;
            Assert.Equal(policy, stored.GainGate);

            var accumulator = new ReviewSummaryAccumulator();
            accumulator.Add(Case("written", false, true, [Token("t1")]));
            var entry = SIL.Motif.Contract.Ids.CanonicalId.Mint("x/");
            var operations = new[] { new SIL.Motif.Contract.Model.OperationEnvelope(SIL.Motif.Contract.Ids.CanonicalId.Mint("x/"),
                "lexical/lexDb/createEntries", entityId: entry, after: System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()),
                new SIL.Motif.Contract.Model.OperationEnvelope(SIL.Motif.Contract.Ids.CanonicalId.Mint("x/"),
                "lexical/lexDb/createEntries", entityId: SIL.Motif.Contract.Ids.CanonicalId.Mint("x/"),
                after: System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()) };
            var gains = TrialReviewPublisher.BuildGains(accumulator.Snapshot(), stored, operations);
            Assert.Equal(GainGateMode.Hold, gains.Gate.Mode);
            Assert.Equal(GainOutcome.UnderThreshold, gains.Gate.Outcome);
            Assert.True(gains.Gate.Hold);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AccumulatingGainsForCebScaleWordsTakesUnderOneSecond()
    {
        var accumulator = new ReviewSummaryAccumulator();
        var cases = Enumerable.Range(0, 62_000).Select(i => Case("w" + i, false, i % 3 != 0,
            i % 4 == 0 ? [Token("t" + i)] : [Cell("c" + i)])).ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var item in cases) accumulator.Add(item);
        var facts = accumulator.Snapshot();
        watch.Stop();
        Assert.Equal(facts.AttestedGained + facts.GeneratedGained, cases.Count(item => item.After.Parsed == true));
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms for {cases.Length} cases");
    }

    private static ReviewMembership Token(string id) => new("tokens", id, "sampled-tokens", 1, null, null, []);
    private static ReviewMembership Cell(string id) => new("cells", id, "paradigm-cells", 1, id, "v", []);
    private static ReviewCaseDelta Case(string key, bool? before, bool? after, IReadOnlyList<ReviewMembership> memberships) =>
        new(key, key, key, "ws", false, false, false, [], [], memberships, [])
        { Before = Side(before), After = Side(after) };
    private static ReviewCaseSideFacts Side(bool? parsed) =>
        new(parsed.HasValue ? Completion.Complete : Completion.NotMeasured, parsed, [], [], [], []);
}
