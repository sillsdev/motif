using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public class AllomorphRetirementContractTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Id(int n) => CanonicalId.FromGuid(Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}")).Value;

    internal static ReplaceListedAllomorphsWithRuleIntent Example()
    {
        var retired = new AllomorphIdentity(Id(2), Id(1), "MoAffixAllomorph", "alternate", "suffix", Digest, null);
        var replacement = retired with { Id = Id(3), Location = "lexeme" };
        var nodes = new List<RetirementOperationBinding>
        {
            new(Id(20), "class-create", Id(4), null, []),
            new(Id(21), "class-members", Id(4), null, [Id(20)])
        };
        var ruleSlots = new[] { "rule-create", "rule-input", "rule-output", "rule-left", "rule-right", "rule-placement", "rule-enabled" };
        for (var i = 0; i < ruleSlots.Length; i++)
            nodes.Add(new(Id(30 + i), ruleSlots[i], Id(5), null, i == 0 ? [Id(21)] : [Id(30), Id(21)]));
        var ruleOperations = Enumerable.Range(30, 7).Select(Id).ToArray();
        nodes.Add(new(Id(40), "bundle-morph", Id(6), null, ruleOperations));
        nodes.Add(new(Id(41), "adhoc-rest", Id(7), null, ruleOperations));
        nodes.Add(new(Id(42), "alternate-delete", Id(1), Id(2), [Id(40), Id(41), .. ruleOperations]));
        return new(
            new("create", Id(4), "Non-front vowels", "NFV", [Id(8), Id(9)], null),
            new(Id(5), "l to r after non-front vowels", [Id(10)], [Id(11)], [new("natural-class", Id(4))], [], new("last", null), true),
            [new(Id(1), AllomorphRetirementScope.Affix, [retired], [new(Id(2), Id(12), null, "whole", replacement)],
                [new(Id(6), Id(13), Id(14), Id(2), Id(12), null, "whole")],
                [new(Id(7), "RestOfAllos", 0, Id(2), replacement)])],
            nodes,
            new("Listed l/r alternation", "Replace one listed suffix with a sound rule."));
    }

    private static JsonObject Json() => JsonNode.Parse(AllomorphRetirementCodec.ToJson(Example()))!.AsObject();
    private static void Refuses(JsonObject value) => Assert.Throws<FormatException>(() => AllomorphRetirementCodec.Parse(value.ToJsonString()));

    [Fact]
    public void CompleteCompositionRoundTripsWithExplicitNullInflType()
    {
        var json = AllomorphRetirementCodec.ToJson(Example());
        Assert.Contains("\"inflType\":null", json);
        Assert.Equal(json, AllomorphRetirementCodec.ToJson(AllomorphRetirementCodec.Parse(json)));
        Assert.False(AllomorphRetirementCapabilities.CanApply);
    }

    [Theory]
    [InlineData("flid")]
    [InlineData("property")]
    [InlineData("extensions")]
    public void UnknownKeysAreRefused(string key)
    {
        var json = Json();
        json["retirements"]![0]![key] = 1;
        Refuses(json);
    }

    [Fact]
    public void OmittedInflTypeIsNotImplicitNull()
    {
        var json = Json();
        json["retirements"]![0]!["roleReplacements"]![0]!.AsObject().Remove("inflType");
        Refuses(json);
    }

    [Theory]
    [InlineData("class", "MoAffixProcess")]
    [InlineData("class", "MoStemAllomorph")]
    [InlineData("class", "UnrecognizedForm")]
    [InlineData("location", "lexeme")]
    [InlineData("position", "circumfix")]
    public void UnsupportedSourceScopeIsRefused(string property, string value)
    {
        var json = Json();
        json["retirements"]![0]!["retiredForms"]![0]![property] = value;
        Refuses(json);
    }

    [Fact]
    public void CrossEntryAndSelfReplacementAreRefused()
    {
        var json = Json();
        json["retirements"]![0]!["roleReplacements"]![0]!["replacement"]!["entry"] = Id(99);
        Refuses(json);
        json = Json();
        json["retirements"]![0]!["roleReplacements"]![0]!["replacement"]!["id"] = Id(2);
        Refuses(json);
    }

    [Fact]
    public void ChainsCyclesAndRetiredTargetAliasesAreRefused()
    {
        var json = Json();
        var retirement = json["retirements"]![0]!;
        retirement["retiredForms"]!.AsArray().Add(retirement["retiredForms"]![0]!.DeepClone());
        retirement["retiredForms"]![1]!["id"] = Id(3);
        Refuses(json);
        retirement["roleReplacements"]!.AsArray().Add(retirement["roleReplacements"]![0]!.DeepClone());
        retirement["roleReplacements"]![1]!["retiredForm"] = Id(3);
        retirement["roleReplacements"]![1]!["replacement"]!["id"] = Id(2);
        Refuses(json);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("inferred")]
    public void OnlyWholeExpansionRoleIsAccepted(string role)
    {
        var json = Json();
        json["retirements"]![0]!["roleReplacements"]![0]!["expansionRole"] = role;
        Refuses(json);
    }

    [Fact]
    public void DuplicateRoleKeysAndUnmappedBundlesAreRefused()
    {
        var json = Json();
        var mappings = json["retirements"]![0]!["roleReplacements"]!.AsArray();
        mappings.Add(mappings[0]!.DeepClone());
        Refuses(json);
        json = Json();
        json["retirements"]![0]!["bundles"]![0]!["msa"] = Id(99);
        Refuses(json);
    }

    [Fact]
    public void AdhocMeaningNeedsItsOwnDestinationAndCannotCollapseMembers()
    {
        var json = Json();
        var refs = json["retirements"]![0]!["adhocReplacements"]!.AsArray();
        refs[0]!["replacement"]!["entry"] = Id(99);
        Refuses(json);
        json = Json();
        refs = json["retirements"]![0]!["adhocReplacements"]!.AsArray();
        refs.Add(refs[0]!.DeepClone());
        refs[1]!["ordinal"] = 1;
        Refuses(json);
    }

    [Fact]
    public void IncompleteClassRuleOrPlacementIsRefused()
    {
        foreach (var key in new[] { "naturalClass", "rule" })
        {
            var json = Json();
            json.Remove(key);
            Refuses(json);
        }
        var missing = Json();
        missing["rule"]!["output"] = new JsonArray();
        Refuses(missing);
        missing = Json();
        missing["rule"]!["enabled"] = false;
        Refuses(missing);
        missing = Json();
        missing["rule"]!["placement"] = new JsonObject { ["kind"] = "before", ["anchor"] = null };
        Refuses(missing);
    }

    [Fact]
    public void GraphRequiresRealRuleWritesAndEveryRetargetBeforeDeletion()
    {
        var json = Json();
        json["operations"]!.AsArray().RemoveAt(8);
        Refuses(json);
        json = Json();
        json["operations"]![9]!["dependsOn"] = new JsonArray();
        Refuses(json);
        json = Json();
        json["operations"]![11]!["dependsOn"] = new JsonArray(Id(40));
        Refuses(json);
    }

    [Fact]
    public void CyclesAndMultipleWritersAreRefused()
    {
        var json = Json();
        json["operations"]![0]!["dependsOn"] = new JsonArray(Id(42));
        Refuses(json);
        json = Json();
        var ops = json["operations"]!.AsArray();
        var duplicate = ops[9]!.DeepClone();
        duplicate["operationId"] = Id(90);
        ops.Add(duplicate);
        Refuses(json);
    }

    [Fact]
    public void DependencyPermutationAndDisplayDoNotChangeIntentDigest()
    {
        var original = Example();
        var reordered = original with
        {
            Operations = original.Operations.Reverse().Select(o => o with { DependsOn = o.DependsOn.Reverse().ToArray() }).ToArray(),
            Display = new("Different title", "Different description")
        };
        Assert.Equal(AllomorphRetirementCodec.IntentDigest(original), AllomorphRetirementCodec.IntentDigest(reordered));
        var json = Json();
        json["retirements"]![0]!["roleReplacements"]![0]!["inflType"] = Id(100);
        json["retirements"]![0]!["bundles"]![0]!["inflType"] = Id(100);
        Assert.NotEqual(AllomorphRetirementCodec.IntentDigest(original),
            AllomorphRetirementCodec.IntentDigest(AllomorphRetirementCodec.Parse(json.ToJsonString())));
    }

    [Fact]
    public void DuplicateJsonPropertiesAreRefused()
    {
        var json = AllomorphRetirementCodec.ToJson(Example());
        Assert.Throws<FormatException>(() => AllomorphRetirementCodec.Parse(json.Replace("\"enabled\":true", "\"enabled\":true,\"enabled\":true")));
    }

    [Fact]
    public void CanonicalAliasesDoNotHideSelfReplacementOrChangeDigest()
    {
        var json = Json();
        json["retirements"]![0]!["roleReplacements"]![0]!["replacement"]!["id"] = "form_" + Id(2);
        Refuses(json);
        json = Json();
        json["retirements"]![0]!["roleReplacements"]![0]!["retiredForm"] = "form_" + Id(2);
        Assert.Equal(AllomorphRetirementCodec.IntentDigest(Example()),
            AllomorphRetirementCodec.IntentDigest(AllomorphRetirementCodec.Parse(json.ToJsonString())));
    }

    [Fact]
    public void DifferentBundleRolesDoNotSelectAnAdhocDestination()
    {
        var json = Json();
        var mappings = json["retirements"]![0]!["roleReplacements"]!.AsArray();
        var second = mappings[0]!.DeepClone();
        second["msa"] = Id(100);
        second["replacement"]!["id"] = Id(101);
        mappings.Add(second);
        json["retirements"]![0]!["adhocReplacements"]![0]!["replacement"]!["id"] = Id(102);
        var parsed = AllomorphRetirementCodec.Parse(json.ToJsonString());
        Assert.Equal(Id(102), parsed.Retirements[0].AdhocReplacements[0].Replacement.Id);
        Assert.DoesNotContain(parsed.Retirements[0].RoleReplacements, m => m.Replacement.Id == Id(102));
        json["retirements"]![0]!["adhocReplacements"]![0]!.AsObject().Remove("replacement");
        Refuses(json);
    }

    [Fact]
    public void LinguisticRuleAndBaselineAssertionsRemainHashed()
    {
        var original = AllomorphRetirementCodec.IntentDigest(Example());
        var json = Json();
        json["rule"]!["name"] = "A changed semantic rule name";
        Assert.NotEqual(original, AllomorphRetirementCodec.IntentDigest(AllomorphRetirementCodec.Parse(json.ToJsonString())));
        json = Json();
        json["retirements"]![0]!["retiredForms"]![0]!["semanticDigest"] = "sha256:" + new string('b', 64);
        Assert.NotEqual(original, AllomorphRetirementCodec.IntentDigest(AllomorphRetirementCodec.Parse(json.ToJsonString())));
    }
}
