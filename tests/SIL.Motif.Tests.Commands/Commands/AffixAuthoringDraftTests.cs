using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AffixAuthoringDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void AlternateAddressesAPrefixedEntryStagedEarlierWithoutChangingTheSavedProject()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vern = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();
        var bytes = File.ReadAllBytes(path);
        Assert.True(ProposalCommands.New(new(path, Version, "affix", "A restricted prefix")).Succeeded);
        var entry = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, "affix", JsonSerializer.Serialize(new
        {
            form = "m", formWs = vern, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
            gloss = "one", analysisWs = analysis, msa = new { kind = "inflectional", category },
        })));
        Assert.True(entry.Succeeded, entry.Refusal?.Message);
        var entryId = entry.Value!.Operations.Single(operation => operation.Kind == LexDbEntriesOperationKinds.Create).EntityId;
        var alternate = ProposalCommands.ComposeAuthorAlternateForm(new(path, Version, "affix", JsonSerializer.Serialize(new
        {
            entry = "entry:" + entryId, form = "mw", formWs = vern, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
        })));
        Assert.True(alternate.Succeeded, alternate.Refusal?.Message);
        var proposal = Read(path, "affix");
        var createEntry = proposal.Operations.Single(operation => operation.Kind == LexDbEntriesOperationKinds.Create);
        var createAlternate = proposal.Operations.Single(operation => operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        Assert.Contains(createAlternate.DependsOn, dependency => dependency.OperationId == createEntry.OperationId);
        using var scratch = DryRunScratch.Adopt(new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "affix route");
        var dryRun = ProposalDryRunner.Run(scratch, proposal);
        Assert.NotEmpty(dryRun.ExpectedEffects);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void AlternateBatchRetainsCitationsAndRefusesEveryStepWhenOneShapeIsInvalid()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value;
        var vern = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "alternates", "A conditioned stem")).Succeeded);
        var intent = new { entry, form = "variant", formWs = vern, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value };
        var good = ProposalCommands.ComposeBatch(new(path, Version, "alternates", JsonSerializer.Serialize(new
        {
            intents = new[] { new { verb = "compose-author-alternate-form", intent,
                citation = new { source = "Synthetic grammar", locator = "Forms" } } },
        })));
        Assert.True(good.Succeeded, good.Refusal?.Message);
        var before = Read(path, "alternates");
        Assert.All(before.Operations, operation => Assert.Equal("Synthetic grammar", OperationCitation.FromProvenance(operation.Provenance)!.Source));
        var first = before.Operations.Single(operation => operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms).EntityId!.Value.Value;
        var refused = ProposalCommands.ComposeBatch(new(path, Version, "alternates", JsonSerializer.Serialize(new
        {
            intents = new object[]
            {
                new { verb = "compose-author-alternate-form", intent = new { entry, form = "second", formWs = vern,
                    morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value, placement = new { after = first } } },
                new { verb = "compose-author-alternate-form", intent = new { entry, form = "bad", formWs = vern, morphType = "unknown" } },
            },
        })));
        Assert.False(refused.Succeeded);
        Assert.Equal(before.Operations.Select(operation => operation.OperationId),
            Read(path, "alternates").Operations.Select(operation => operation.OperationId));
    }

    private static Proposal Read(string path, string draft)
    {
        var result = ProjectStoreCommand.Run(path, Version, (database, _) =>
        {
            var value = ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(draft).ProposalJson!);
            return CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(value)));
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }
}
