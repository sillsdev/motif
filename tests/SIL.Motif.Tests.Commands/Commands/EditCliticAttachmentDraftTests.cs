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
public sealed class EditCliticAttachmentDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public void OneDraftSetsEncliticThenAttachmentAndDryRunsWithoutRefusal()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var form = CanonicalId.FromGuid(entry.LexemeFormOA.Guid).Value;
        var msa = CanonicalId.FromGuid(entry.SensesOS[0].MorphoSyntaxAnalysisRA.Guid).Value;
        string noun = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, "Noun");
            noun = CanonicalId.FromGuid(category.Guid).Value;
        });
        new FwDataProjectLoader().Save(cache);
        var created = ProposalCommands.New(new(path, "test", "clitic", "Clitic"));
        Assert.True(created.Succeeded);
        var steps = JsonSerializer.Serialize(new
        {
            intents = new object[]
            {
                new { verb = "compose-edit-morph-type", intent = new { target = form,
                    morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphEnclitic).Value } },
                new { verb = "compose-edit-clitic-attachment", intent = new { target = msa,
                    expectedCategories = Array.Empty<string>(), categories = new[] { noun } } },
            },
        });
        var composed = ProposalCommands.ComposeBatch(new(path, "test", "clitic", steps));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var dry = DryRunJobRunner.Run(path, "test", created.Value!.ProposalId);
        Assert.True(dry.Succeeded, dry.Refusal?.Message);
    }

    [Fact]
    public void ANonCliticTargetIsRefusedWithItsCatalogueCodeOnTheSingleVerb()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var msa = CanonicalId.FromGuid(entry.SensesOS[0].MorphoSyntaxAnalysisRA.Guid).Value;
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, "test", "no-clitic", "Clitic")).Succeeded);
        var intent = JsonSerializer.Serialize(new { target = msa, expectedCategories = Array.Empty<string>(),
            categories = Array.Empty<string>() });
        var refused = ProposalCommands.ComposeIntent(new(path, "test", "no-clitic", "compose-edit-clitic-attachment", intent));
        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.CliticAttachmentNotClitic, refused.Refusal!.Code);
    }
}
