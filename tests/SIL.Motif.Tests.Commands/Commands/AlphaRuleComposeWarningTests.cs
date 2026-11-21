using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>The compose response warns about alpha expansion with the Dry Run's own wording.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AlphaRuleComposeWarningTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static string Batch(int placeValues)
    {
        var values = string.Join(",", Enumerable.Range(1, placeValues).Select(i => $"{{\"name\":\"v{i}\"}}"));
        return $$$"""
            {"intents":[
              {"verb":"compose-author-phoneme","intent":{"name":"a","representations":["a"]}},
              {"verb":"compose-author-phonological-feature","as":"place","intent":{"name":"place","values":[{{{values}}}]}},
              {"verb":"compose-author-phonological-feature","as":"arch","intent":{"name":"arch","values":[{"name":"surface"},{"name":"underlying"}]}},
              {"verb":"compose-author-phonological-feature","as":"manner","intent":{"name":"manner","values":[{"name":"stop"},{"name":"nasal"}]}},
              {"verb":"compose-author-natural-class","as":"under","intent":{"name":"underlying","abbreviation":"UND","features":[{"feature":"@arch","value":"@arch:values:underlying"}]}},
              {"verb":"compose-author-natural-class","as":"stops","intent":{"name":"stops","abbreviation":"STP","features":[{"feature":"@manner","value":"@manner:values:stop"}]}},
              {"verb":"compose-author-phonological-rule","intent":{"name":"place assimilation","direction":"simultaneous",
                "input":[{"naturalClass":"@under"}],
                "output":[{"change":[{"feature":"@arch","value":"@arch:values:surface"}],"variables":[{"variable":"alpha","feature":"@place","polarity":"+"}]}],
                "left":[],
                "right":[{"boundary":"morpheme"},{"naturalClass":"@stops","variables":[{"variable":"alpha","feature":"@place","polarity":"+"}]}]}}
            ]}
            """;
    }

    private IReadOnlyList<ComposedRelatedObject> Compose(int placeValues)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "alpha", "Alpha")).Succeeded);
        var composed = ProposalCommands.ComposeBatch(new(path, Version, "alpha", Batch(placeValues)));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        return composed.Value!.Intents[^1].RelatedObjects ?? [];
    }

    [Fact]
    public void ARuleWhoseAlphaVariableTakesTwentyEightValuesCarriesTheWarning()
    {
        var warning = Assert.Single(Compose(28), item => item.Kind == AlphaRuleComposeWarning.Kind);

        Assert.Contains("Rule 'place assimilation'", warning.Name, StringComparison.Ordinal);
        Assert.Contains("place (28 values)", warning.Name, StringComparison.Ordinal);
        Assert.Contains("up to 28 combinations", warning.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleWhoseAlphaVariableTakesThreeValuesCarriesNoWarning()
    {
        Assert.DoesNotContain(Compose(3), item => item.Kind == AlphaRuleComposeWarning.Kind);
    }
}
