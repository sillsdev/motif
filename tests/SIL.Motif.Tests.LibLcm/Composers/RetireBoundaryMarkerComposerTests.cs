using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RetireBoundaryMarkerComposerTests(PristineProjectFixture pristine)
{
    private static IPhPhonemeSet FirstSet(LcmCache cache)
    {
        var data = cache.LangProject.PhonologicalDataOA;
        if (data.PhonemeSetsOS.Count == 0)
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                data.PhonemeSetsOS.Add(cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create()));
        return data.PhonemeSetsOS[0];
    }

    private static IPhBdryMarker Marker(LcmCache cache, Guid guid, string spelling)
    {
        IPhBdryMarker marker = null!;
        var set = FirstSet(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            marker = set.BoundaryMarkersOC.FirstOrDefault(item => item.Guid == guid)!;
            if (marker is null)
            {
                marker = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid);
                set.BoundaryMarkersOC.Add(marker);
                var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
                marker.CodesOS.Add(code);
                code.Representation.set_String(cache.DefaultVernWs, TsStringUtils.MakeString(spelling, cache.DefaultVernWs));
            }
        });
        return marker;
    }

    private static string Refusal(Action act) => Assert.Throws<InvalidOperationException>(act).Message;

    private static CanonicalId Id(IPhBdryMarker marker) => CanonicalId.FromGuid(marker.Guid);

    [Fact]
    public void MergingAUsedDuplicateRepointsItsContextsAndTheProjectThenTakesABoundaryRule()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, ["n", "m"]);
        var stray = Marker(cache, Guid.NewGuid(), "+");
        IPhSimpleContextBdry context = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            context = cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create();
            cache.LangProject.PhonologicalDataOA.ContextsOS.Add(context);
            context.FeatureStructureRA = stray;
        });
        var phoneme = CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.First().Guid);
        var rule = new AuthorPhonologicalRuleIntent("n to m", PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: phoneme)], [], [], [new(Boundary: "morpheme")]);
        var onStray = rule with { Right = [new(BoundaryMarker: Id(stray))] };
        Assert.Contains("conflicting boundary markers", Refusal(() => AuthorPhonologicalRuleComposer.Build(cache, onStray)));

        var operations = MergeBoundaryMarkersComposer.Build(cache, new(Id(stray)));
        // This intermediate fixture state needs direct handler setup.
        SoundSystemComposerTests.ExecuteHandlers(cache, operations);

        Assert.Equal(LangProjectTags.kguidPhRuleMorphBdry, context.FeatureStructureRA!.Guid);
        Assert.DoesNotContain(FirstSet(cache).BoundaryMarkersOC, item => item.Guid == stray.Guid);
        Assert.NotEmpty(AuthorPhonologicalRuleComposer.Build(cache, rule));
    }

    [Fact]
    public void RetiresAnUnusedDuplicateOfTheStandardMorphemeMarkerAndKeepsTheStandardOne()
    {
        using var cache = pristine.NewScratch();
        var standard = Marker(cache, LangProjectTags.kguidPhRuleMorphBdry, "+");
        var stray = Marker(cache, Guid.NewGuid(), "+");

        var operations = RetireBoundaryMarkerComposer.Build(cache, new(Id(stray)));
        SoundSystemComposerTests.Execute(cache, operations);

        var set = FirstSet(cache);
        Assert.Equal(BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers, Assert.Single(operations).Kind);
        Assert.DoesNotContain(set.BoundaryMarkersOC, item => item.Guid == stray.Guid);
        Assert.Contains(set.BoundaryMarkersOC, item => item.Guid == standard.Guid);
    }

    [Fact]
    public void AMarkerDeletedOrNewlyUsedAfterTheDryRunFailsItsFootprintCleanlyNamingTheMarker()
    {
        using var cache = pristine.NewScratch();
        Marker(cache, LangProjectTags.kguidPhRuleMorphBdry, "+");
        var gone = Marker(cache, Guid.NewGuid(), "+");
        var used = Marker(cache, Guid.NewGuid(), "+");
        var goneOperation = Assert.Single(RetireBoundaryMarkerComposer.Build(cache, new(Id(gone))));
        var usedOperation = Assert.Single(RetireBoundaryMarkerComposer.Build(cache, new(Id(used))));
        var handler = OperationHandlerRegistry.Resolve(BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers, "retire-marker-tests");
        var goneId = Id(gone);
        handler.ReadCurrentFootprint(cache, goneOperation);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => gone.Delete());
        Assert.Contains(goneId.Value, Assert.Throws<InvalidOperationException>(
            () => handler.ReadCurrentFootprint(cache, goneOperation)).Message);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create();
            cache.LangProject.PhonologicalDataOA.ContextsOS.Add(context);
            context.FeatureStructureRA = used;
        });
        Assert.Contains("still used", Assert.Throws<InvalidOperationException>(
            () => handler.ReadCurrentFootprint(cache, usedOperation)).Message);
    }

    [Fact]
    public void RefusesTheStandardMarkerAndAMarkerThatIsNotADuplicate()
    {
        using var cache = pristine.NewScratch();
        var standard = Marker(cache, LangProjectTags.kguidPhRuleMorphBdry, "+");
        var odd = Marker(cache, Guid.NewGuid(), "@");

        Assert.Contains("standard boundary marker", Refusal(() => RetireBoundaryMarkerComposer.Build(cache, new(Id(standard)))));
        Assert.Contains("not a duplicate", Refusal(() => RetireBoundaryMarkerComposer.Build(cache, new(Id(odd)))));
    }

    [Fact]
    public void RefusesADuplicateWhenNoStandardMarkerHoldsTheSameSpelling()
    {
        using var cache = pristine.NewScratch();
        var set = FirstSet(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var marker in set.BoundaryMarkersOC.ToArray()) marker.Delete();
        });
        var lone = Marker(cache, Guid.NewGuid(), "+");

        Assert.Contains("not in the phoneme set", Refusal(() => RetireBoundaryMarkerComposer.Build(cache, new(Id(lone)))));
    }

    [Fact]
    public void RefusesADuplicateThatARuleContextStillUsesNamingTheReferrer()
    {
        using var cache = pristine.NewScratch();
        Marker(cache, LangProjectTags.kguidPhRuleMorphBdry, "+");
        var stray = Marker(cache, Guid.NewGuid(), "+");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextBdryFactory>().Create();
            cache.LangProject.PhonologicalDataOA.ContextsOS.Add(context);
            context.FeatureStructureRA = stray;
        });

        var message = Refusal(() => RetireBoundaryMarkerComposer.Build(cache, new(Id(stray))));

        Assert.Contains("still used by a PhSimpleContextBdry", message);
    }
}
