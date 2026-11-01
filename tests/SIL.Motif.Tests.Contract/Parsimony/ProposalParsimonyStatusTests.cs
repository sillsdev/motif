using System.Reflection;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class ProposalParsimonyStatusTests
{
    private static readonly JsonSerializerOptions Strict = Options();

    private static JsonSerializerOptions Options()
    {
        var options = MotifJson.CreateOptions();
        options.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
        return options;
    }

    private static ProposalParsimonyStatus Sample() => new(
        ProposalParsimonyStatus.CurrentSchemaVersion, "proposal/aaaaaaaaaaaaaaaaaaaaaa", "sha256:abc", "job/t1",
        ProposalParsimonyState.Stale, new ProposalParsimonyReason("stale", "Out of date."),
        new ProposalParsimonyJob("job/p1", "completed", new JobStageProgress("facts", 2, 5, "candidate"),
            "2026-11-01T00:00:00.0000000Z", "2026-11-01T00:00:01.0000000Z", "2026-11-01T00:00:09.0000000Z",
            "trial"),
        new ProposalParsimonyStale(
            [ProposalParsimonyStaleReason.BaselineChanged, ProposalParsimonyStaleReason.ParserChanged], "k1", "k2"),
        new ProposalParsimonyFailure(ProposalParsimonyFailureCategory.TimedOut, "Too large.", false,
            "2026-11-01T00:00:09.0000000Z"),
        [
            new ProposalParsimonyGroup(ParsimonyMeasureGroup.Grammar, ProposalParsimonyGroupState.Abstained, null,
                new ProposalParsimonyCandidate("b1", "b2", "id"), [],
                [new ProposalParsimonyMeasureChange("P-adhoc-duplicate", 1, 2, 3, 4, "r1", "r2")]),
            new ProposalParsimonyGroup(ParsimonyMeasureGroup.Text, ProposalParsimonyGroupState.NotSetUp,
                "Choose Texts.", null, [], []),
        ]);

    [Fact]
    public void TheStatusRoundTripsThroughClosedJson()
    {
        var json = JsonSerializer.Serialize(Sample(), Strict);

        var read = JsonSerializer.Deserialize<ProposalParsimonyStatus>(json, Strict)!;

        Assert.Equal(JsonSerializer.Serialize(Sample(), Strict), JsonSerializer.Serialize(read, Strict));
        Assert.True(read.Advisory);
        Assert.Equal(ProposalParsimonyState.Stale, read.State);
    }

    [Theory]
    [InlineData(ProposalParsimonyState.NotSetUp, "not-set-up")]
    [InlineData(ProposalParsimonyState.NotYetRun, "not-yet-run")]
    [InlineData(ProposalParsimonyState.Queued, "queued")]
    [InlineData(ProposalParsimonyState.Running, "running")]
    [InlineData(ProposalParsimonyState.Ready, "ready")]
    [InlineData(ProposalParsimonyState.Stale, "stale")]
    [InlineData(ProposalParsimonyState.Failed, "failed")]
    public void EveryStateHasItsWireName(ProposalParsimonyState state, string wire) =>
        Assert.Equal("\"" + wire + "\"", JsonSerializer.Serialize(state, Strict));

    [Theory]
    [InlineData(ProposalParsimonyGroupState.NotSetUp, "not-set-up")]
    [InlineData(ProposalParsimonyGroupState.NotYetRun, "not-yet-run")]
    [InlineData(ProposalParsimonyGroupState.Ready, "ready")]
    [InlineData(ProposalParsimonyGroupState.Abstained, "abstained")]
    [InlineData(ProposalParsimonyGroupState.Failed, "failed")]
    public void EveryGroupStateHasItsWireName(ProposalParsimonyGroupState state, string wire) =>
        Assert.Equal("\"" + wire + "\"", JsonSerializer.Serialize(state, Strict));

    [Fact]
    public void AnUnknownStateIsRefused()
    {
        var json = JsonSerializer.Serialize(Sample(), Strict).Replace("\"stale\"", "\"almost-ready\"");

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ProposalParsimonyStatus>(json, Strict));
    }

    [Fact]
    public void AnUnknownFieldIsRefused()
    {
        var json = JsonSerializer.Serialize(Sample(), Strict).Replace("\"Advisory\":true",
            "\"Advisory\":true,\"Score\":0.9");

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ProposalParsimonyStatus>(json, Strict));
    }

    [Fact]
    public void TheStatusCarriesNoScoreOrNumericAggregate()
    {
        var types = typeof(ProposalParsimonyStatus).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ProposalParsimonyStatus).Namespace &&
                type.Name.StartsWith("ProposalParsimony", StringComparison.Ordinal) && type.IsClass)
            .ToArray();
        Assert.NotEmpty(types);

        var properties = types.SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .ToArray();

        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(double) ||
            property.PropertyType == typeof(double?) || property.PropertyType == typeof(float) ||
            property.PropertyType == typeof(decimal));
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Score", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheStatusIsAdvisoryByDefault() => Assert.True(Sample().Advisory);
}
