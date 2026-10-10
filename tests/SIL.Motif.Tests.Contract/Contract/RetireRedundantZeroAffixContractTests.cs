using System;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class RetireRedundantZeroAffixContractTests
{
    private static RetireRedundantZeroAffixIntentDocument Example() => new(
        new RetireRedundantZeroAffixIntent(CanonicalId.FromGuid(
            Guid.Parse("2a3b4c5d-6e7f-4081-92a3-b4c5d6e7f809")).Value));

    [Fact]
    public void RetiringOneEntryRoundTripsAsClosedCanonicalIntent()
    {
        var json = RetireRedundantZeroAffixCodec.ToJson(Example());
        var parsed = RetireRedundantZeroAffixCodec.Parse(json);

        Assert.Equal("motif-retire-redundant-zero-affix", parsed.Format);
        Assert.Equal(1, parsed.Version);
        Assert.Equal(Example().Retirement.Entry, parsed.Retirement.Entry);
        Assert.Equal(json, RetireRedundantZeroAffixCodec.ToJson(parsed));
        Assert.Equal(RetireRedundantZeroAffixCodec.IntentDigest(parsed),
            RetireRedundantZeroAffixCodec.IntentDigest(RetireRedundantZeroAffixCodec.Parse(json)));
    }

    [Fact]
    public void RetiringOneEntryRejectsOpenOrMalformedIntent()
    {
        var json = JsonNode.Parse(RetireRedundantZeroAffixCodec.ToJson(Example()))!.AsObject();
        json["retirement"]!["unexpected"] = true;
        Assert.Throws<FormatException>(() => RetireRedundantZeroAffixCodec.Parse(json.ToJsonString()));

        json = JsonNode.Parse(RetireRedundantZeroAffixCodec.ToJson(Example()))!.AsObject();
        json["retirement"]!["entry"] = "not-a-canonical-id";
        Assert.Throws<FormatException>(() => RetireRedundantZeroAffixCodec.Parse(json.ToJsonString()));

        json = JsonNode.Parse(RetireRedundantZeroAffixCodec.ToJson(Example()))!.AsObject();
        json["version"] = 2;
        Assert.Throws<FormatException>(() => RetireRedundantZeroAffixCodec.Parse(json.ToJsonString()));
    }
}
