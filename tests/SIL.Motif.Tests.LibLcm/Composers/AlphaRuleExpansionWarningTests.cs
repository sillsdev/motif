using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>A rule whose alpha variable ranges over many feature values warns in the Dry Run, naming the rule and the cost.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AlphaRuleExpansionWarningTests(PristineProjectFixture pristine)
{
    private static IEnumerable<string> Warnings(SIL.Motif.Model.DryRun.DryRun dryRun) => dryRun.ExpectedEffects
        .Where(effect => effect.Field == "dryRunWarning").Select(effect => effect.After["warning"]);

    private static void AddPlaceValues(LcmCache cache, FeaturedSegments segments, int count)
    {
        var place = (IFsClosedFeature)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(segments.Place.ToGuid());
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            for (var i = 0; i < count; i++)
            {
                var value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                place.ValuesOC.Add(value);
                value.Name.set_String(cache.DefaultAnalWs, "extra " + i);
            }
        });
    }

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);

    [Fact]
    public void ARuleWhoseAlphaVariableRangesOverManyValuesWarnsWithTheRuleAndTheExpansion()
    {
        using var cache = pristine.NewScratch();
        var segments = new FeaturedSegments(cache);
        AddPlaceValues(cache, segments, 9);
        var ops = AuthorPhonologicalRuleComposer.Build(cache, AuthorPhonologicalRuleIntentParser.Parse(segments.Assimilation()));

        var warning = Assert.Single(Warnings(ScratchDryRun.Of(cache, Proposal(ops))));

        Assert.Contains("Rule 'place assimilation'", warning, StringComparison.Ordinal);
        Assert.Contains("place (12 values)", warning, StringComparison.Ordinal);
        Assert.Contains("up to 12 combinations", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleWhoseAlphaVariableRangesOverAFewValuesDoesNotWarn()
    {
        using var cache = pristine.NewScratch();
        var segments = new FeaturedSegments(cache);
        var ops = AuthorPhonologicalRuleComposer.Build(cache, AuthorPhonologicalRuleIntentParser.Parse(segments.Assimilation()));

        Assert.Empty(Warnings(ScratchDryRun.Of(cache, Proposal(ops))));
    }
}
