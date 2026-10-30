using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditMorphTypeDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public void QueuedDryRunReturnsTheStaleSourceRefusalCodeAndFacts()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var form = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId).LexemeFormOA;
        var formId = CanonicalId.FromGuid(form.Guid).Value;
        new FwDataProjectLoader().Save(cache);
        var created = ProposalCommands.New(new(path, "test", "stale-morph", "Morph type"));
        Assert.True(created.Succeeded);
        var intent = JsonSerializer.Serialize(new { target = formId,
            morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphEnclitic).Value });
        var composed = ProposalCommands.ComposeIntent(new(path, "test", "stale-morph", "compose-edit-morph-type", intent));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => form.MorphTypeRA =
            cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphRoot));
        new FwDataProjectLoader().Save(cache);
        var dry = DryRunJobRunner.Run(path, "test", created.Value!.ProposalId);
        Assert.False(dry.Succeeded);
        Assert.Equal(RefusalCodes.MorphTypeStaleSource, dry.Refusal!.Code);
        Assert.Equal(formId, dry.Refusal.Facts["form"]);
        Assert.Equal(CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphRoot).Value, dry.Refusal.Facts["actualMorphType"]);
    }

    [Fact]
    public void DraftComposesStemEditAndReportsCrossFamilyRefusal()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var target = CanonicalId.FromGuid(entry.LexemeFormOA.Guid).Value;
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, "test", "morph", "Morph type")).Succeeded);
        string Input(Guid type) => JsonSerializer.Serialize(new { target, morphType = CanonicalId.FromGuid(type).Value });
        var refused = ProposalCommands.ComposeIntent(new(path, "test", "morph", "compose-edit-morph-type",
            Input(MoMorphTypeTags.kguidMorphPrefix)));
        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.MorphTypeIncompatibleFamily, refused.Refusal!.Code);
        var accepted = ProposalCommands.ComposeIntent(new(path, "test", "morph", "compose-edit-morph-type",
            Input(MoMorphTypeTags.kguidMorphEnclitic)));
        Assert.True(accepted.Succeeded, accepted.Refusal?.Message);
        Assert.Single(accepted.Value!.Operations);
    }
}
