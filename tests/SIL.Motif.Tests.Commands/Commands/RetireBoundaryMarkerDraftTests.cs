using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RetireBoundaryMarkerDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static IPhPhonemeSet FirstSet(LcmCache cache)
    {
        var data = cache.LangProject.PhonologicalDataOA;
        if (data.PhonemeSetsOS.Count == 0)
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                data.PhonemeSetsOS.Add(cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create()));
        return data.PhonemeSetsOS[0];
    }

    [Fact]
    public void AStrayDuplicateMarkerIsStagedForRetirementInOneCompose()
    {
        using var cache = pristine.NewScratch();
        var stray = Guid.NewGuid();
        var set = FirstSet(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var guid in new[] { LangProjectTags.kguidPhRuleMorphBdry, stray })
            {
                if (set.BoundaryMarkersOC.Any(item => item.Guid == guid)) continue;
                var marker = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid);
                set.BoundaryMarkersOC.Add(marker);
                var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
                marker.CodesOS.Add(code);
                code.Representation.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("+", cache.DefaultVernWs));
            }
        });
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "boundary", "Retire a duplicate marker")).Succeeded);

        var staged = ProposalCommands.ComposeIntent(new(path, Version, "boundary", "compose-retire-boundary-marker",
            JsonSerializer.Serialize(new { marker = CanonicalId.FromGuid(stray).Value })));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Contains(staged.Value!.Operations,
            operation => operation.Kind == BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers);
    }
}
