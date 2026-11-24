using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// The <c>effects</c> client against replies recorded from a real PanGloss v0.31.0 (<see cref="RecordedEffects"/>): the request
/// line, the reader, the capability check and the resident pool's round trip.
/// </summary>
public sealed class ServeEffectsTests : IDisposable
{
    private const string Entry = "00000000-0000-0000-0000-000000000030";
    private const string Msa = "00000000-0000-0000-0000-000000000032";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-effects-" + Guid.NewGuid().ToString("N"));

    public ServeEffectsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void TheRequestLineNamesTheMorphemeAndOnlyTheLimitsGiven()
    {
        var bare = JsonNode.Parse(new ServeEffectsRequest(new EffectsMorpheme(Entry, Msa)).ToLine("7"))!.AsObject();
        Assert.Equal(["id", "op", "morpheme"], bare.Select(pair => pair.Key));
        Assert.Equal("effects", (string?)bare["op"]);
        Assert.Equal(Entry, (string?)bare["morpheme"]!["entryGuid"]);
        Assert.Null(bare["morpheme"]!["allomorphId"]);

        var full = JsonNode.Parse(new ServeEffectsRequest(new EffectsMorpheme(Entry, Msa, 3), ["left"], 5, 1000, 2500).ToLine("8"))!.AsObject();
        Assert.Equal(3, (int?)full["morpheme"]!["allomorphId"]);
        Assert.Equal("left", (string?)full["edges"]![0]);
        Assert.Equal(5, (int?)full["rootsPerEnvironment"]);
        Assert.Equal(1000, (long?)full["maxWork"]);
        Assert.Equal(2500, (long?)full["wordTimeoutMs"]);
    }

    [Fact]
    public void TheRequestTimeoutOutlastsPanGlossOwnDeadline()
    {
        var floor = TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(40), new ServeEffectsRequest(new EffectsMorpheme(Entry, Msa)).Timeout(floor));
        Assert.Equal(TimeSpan.FromHours(1), new ServeEffectsRequest(new EffectsMorpheme(Entry, Msa), WordTimeoutMs: 600_000_000).Timeout(floor));
    }

    [Fact]
    public void ARootWithoutTheRulesTargetIsNotOnFormAtBothEdges()
    {
        var result = EffectsResult.Read(RecordedEffects.Fill(RecordedEffects.RootNotOnForm, "ranna"));

        Assert.True(result.Complete);
        Assert.False(result.Partial);
        Assert.Null(result.StopReason);
        var allomorph = Assert.Single(result.Allomorphs);
        Assert.Equal(("ranna", false), (allomorph.Form, allomorph.FormPartial));
        Assert.Equal(["r", "a", "n", "n", "a"], allomorph.Segments);
        Assert.Equal(["left", "right"], result.Effects.Select(row => row.Edge));
        Assert.All(result.Effects, row =>
        {
            Assert.True(row.NotOnForm);
            Assert.Equal("no_lhs_match", row.Reason);
            Assert.Equal(("rewrite", "Voicing", "compiled", 0), (row.Rule.Kind, row.Rule.Name, row.Rule.Status, row.Subrule));
            Assert.Empty(row.LhsSpans);
        });
        Assert.Empty(result.Survivors);
    }

    [Fact]
    public void AFormWithTheTargetCarriesItsSpansAndIsASurvivor()
    {
        var result = EffectsResult.Read(RecordedEffects.Fill(RecordedEffects.RootWithTarget, "kat"));

        Assert.Equal(2, result.Survivors.Count());
        Assert.All(result.Effects, row =>
        {
            Assert.Equal(("unknown", "environment_unchecked"), (row.MayApply, row.Reason));
            Assert.Equal([(2, 3)], row.LhsSpans);
        });
    }

    [Fact]
    public void AnAffixThatCopiesTheStemIsFormPartial()
    {
        var allomorph = Assert.Single(EffectsResult.Read(RecordedEffects.Fill(RecordedEffects.PartialAffix, "+s")).Allomorphs);

        Assert.Equal("+s", allomorph.Form);
        Assert.True(allomorph.FormPartial);
        Assert.Equal(["s"], allomorph.Segments);
    }

    [Fact]
    public void AMetathesisRuleHasNoSubruleAndMayStraddleTheEdge()
    {
        var row = Assert.Single(EffectsResult.Read(RecordedEffects.StraddlingMetathesis).Effects);

        Assert.Null(row.Subrule);
        Assert.Equal("metathesis", row.Rule.Kind);
        Assert.Equal("lhs_may_straddle_edge", row.Reason);
        Assert.False(row.NotOnForm);
    }

    [Fact]
    public void AWorkLimitedScanIsPartialWithItsStopReasonAndTheRowsSoFar()
    {
        var result = EffectsResult.Read(RecordedEffects.Fill(RecordedEffects.WorkLimited, "ranni", "ranna"));

        Assert.False(result.Complete);
        Assert.True(result.Partial);
        Assert.Equal("work_limit", result.StopReason);
        Assert.Equal(["ranni", "ranna"], result.Allomorphs.Select(a => a.Form));
        Assert.Equal([1], result.Effects.Select(row => row.AllomorphId).Distinct());
    }

    [Theory]
    [InlineData(nameof(RecordedEffects.UnknownMorpheme))]
    [InlineData(nameof(RecordedEffects.NoAuthoredIdentity))]
    public void AnErrorLineIsNotAResult(string name)
    {
        var line = (string)typeof(RecordedEffects).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;
        Assert.Throws<FormatException>(() => EffectsResult.Read(line));
    }

    [Fact]
    public void TheCapabilityComesFromTheDescriptionAndItsAbsenceNamesTheRelease()
    {
        var node = JsonNode.Parse(WhyNotRecordedTests.Recorded("describe"))!;
        var interfaces = node["interfaces"] as JsonObject ?? (JsonObject)(node["interfaces"] = new JsonObject());
        var ops = interfaces["serveOps"] as JsonObject ?? (JsonObject)(interfaces["serveOps"] = new JsonObject());
        ops.Remove("effects");
        var without = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.Contains("interfaces.serveOps.effects", without.ServeEffectsRefusal());

        ops["effects"] = JsonNode.Parse(RecordedEffects.Describe);
        var with = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        Assert.Null(with.ServeEffectsRefusal());
        Assert.Equal(1, with.ServeEffects!.Version);
        Assert.Equal(["unboundedQuantifier", "alphaAgreement", "iterativeSpreading"], with.ServeEffects.NonLocalCriteria.ToArray());
        Assert.Contains("v0.31.0", PanGlossInterfaceVersions.ServeEffectsNeeded);
    }

    [Fact]
    public async Task ARecordedReplyComesBackThroughTheResidentPoolWithTheRequestOnTheWire()
    {
        var (pool, grammar, log) = Declaring("declares", RecordedEffects.RootWithTarget);
        using var disposable = pool;

        var answer = Assert.IsType<WarmEffectsResult.Completed>(await pool.TryEffectsAsync(grammar, "t1", grammar,
            new ServeEffectsRequest(new EffectsMorpheme(Entry, Msa), ["left", "right"]), CancellationToken.None));

        Assert.Equal(2, EffectsResult.Read(RecordedEffects.Fill(answer.Json, "kat")).Survivors.Count());
        var sent = JsonDocument.Parse(Assert.Single(File.ReadAllLines(log))).RootElement;
        Assert.Equal("effects", sent.GetProperty("op").GetString());
        Assert.Equal(Entry, sent.GetProperty("morpheme").GetProperty("entryGuid").GetString());
    }

    [Fact]
    public async Task APanGlossErrorIsRejectedWithItsCodeAndTheWholeLine()
    {
        var (pool, grammar, _) = Declaring("rejects", RecordedEffects.UnknownMorpheme);
        using var disposable = pool;

        var answer = Assert.IsType<WarmEffectsResult.Rejected>(await pool.TryEffectsAsync(grammar, "t1", grammar,
            new ServeEffectsRequest(new EffectsMorpheme(Entry, Msa)), CancellationToken.None));

        Assert.Equal("unknown_morpheme", answer.Code);
        Assert.Contains("no entry", answer.Message);
    }

    [Fact]
    public async Task AParserThatDeclaresNoEffectsIsNeverSentTheOp()
    {
        var (grammar, directory) = Project("none");
        var log = Path.Combine(directory, "effects.log");
        FakeParser.Behave(directory, new Dictionary<string, object?> { ["ServeEffectsLog"] = log });
        using var pool = Pool(FakeParser.ExecutablePath);

        var answer = Assert.IsType<WarmEffectsResult.Unsupported>(await pool.TryEffectsAsync(grammar, "t1", grammar,
            new ServeEffectsRequest(new EffectsMorpheme(Entry, Msa)), CancellationToken.None));

        Assert.Contains("interfaces.serveOps.effects", answer.Detail);
        Assert.False(File.Exists(log));
    }

    private (WarmPanGlossPool Pool, string Grammar, string Log) Declaring(string name, string reply)
    {
        var copy = FakeParser.CopyDeclaringServeEffects(Path.Combine(_root, name), RecordedEffects.Describe);
        var (grammar, directory) = Project(name + "-project");
        var log = Path.Combine(directory, "effects.log");
        FakeParser.Behave(directory, new Dictionary<string, object?>
        {
            ["ServeEffectsLog"] = log,
            ["ServeEffects"] = JsonDocument.Parse(reply).RootElement,
        });
        return (Pool(copy), grammar, log);
    }

    private (string Grammar, string Directory) Project(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var grammar = Path.Combine(directory, "p.fwdata");
        File.WriteAllText(grammar, "the fake parser never reads this.");
        return (grammar, directory);
    }

    private static WarmPanGlossPool Pool(string executable) => new(executable,
        new MachinePanGlossQueue(["Local\\MotifEffectsTests-" + Guid.NewGuid().ToString("N") + "-0"]), ownsQueue: true);
}
