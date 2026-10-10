using System.Text.Json;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class PairedTests
{
    [Fact]
    public void SerializesTheNamedSidesAndFlagsAsCanonicalJson()
    {
        var pair = new Paired<string>("today", "proposal", true, true, true, null);
        var json = JsonSerializer.Serialize(pair);

        Assert.Equal("""{"after":"proposal","afterRefusal":null,"before":"today","hasAfter":true,"hasBefore":true,"moved":true}""",
            CanonicalJson.Canonicalize(json));
    }

    [Fact]
    public void MissingProposalValueSerializesAsNullAndRemainsExplicit()
    {
        var pair = new Paired<int>(7, default, true, false, false, null);
        var json = JsonSerializer.Serialize(pair);

        Assert.Equal("""{"after":null,"afterRefusal":null,"before":7,"hasAfter":false,"hasBefore":true,"moved":false}""",
            CanonicalJson.Canonicalize(json));
    }

    [Fact]
    public void ARefusedSideHasNoValueAndKeepsItsRefusal()
    {
        var refusal = new SIL.Motif.Contract.Commands.Refusal("trial.none",
            SIL.Motif.Contract.Responses.FailureReason.NotFound, "No Trial is recorded.");
        var pair = new Paired<string>("today", null, true, false, false, refusal);
        var json = JsonSerializer.Serialize(pair);
        var restored = JsonSerializer.Deserialize<Paired<string>>(json);

        Assert.Equal("trial.none", restored.AfterRefusal?.Code);
        Assert.False(restored.HasAfter);
        Assert.Null(restored.After);
    }
}
