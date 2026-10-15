using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class FeatureAuthoringDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SingleCallsReportTheActualStructureCreateAndAllowValueOmission(bool inflectional, bool stagedMsa)
    {
        var setup = Prepare();
        const string draft = "feature-selection";
        Assert.True(ProposalCommands.New(new(setup.Path, Version, draft, "Synthetic grammatical selection")).Succeeded);
        var msa = stagedMsa ? StageMsa(setup, draft, inflectional) : inflectional ? setup.InflMsa : setup.StemMsa;
        var bytes = File.ReadAllBytes(setup.Path);
        var structure = ProposalCommands.ComposeAuthorFeatureStructure(new(setup.Path, Version, draft,
            JsonSerializer.Serialize(new { msa })));
        Assert.True(structure.Succeeded, structure.Refusal?.Message);
        var created = Assert.Single(structure.Value!.CreatedObjects);
        Assert.Equal(StructureKind(inflectional), created.Kind);
        Assert.Equal(created.Id, Assert.Single(structure.Value.Operations).EntityId);
        Assert.Equal(msa, ReadDraft(setup.Path, draft).Operations.Single(operation => operation.EntityId == created.Id).Target);
        var before = ReadDraft(setup.Path, draft).Operations.Select(operation => operation.OperationId).ToArray();
        var occupied = ProposalCommands.ComposeAuthorFeatureStructure(new(setup.Path, Version, draft,
            JsonSerializer.Serialize(new { msa })));
        Assert.False(occupied.Succeeded);
        Assert.Contains("already has a feature structure", occupied.Refusal!.Message);
        Assert.Equal(before, ReadDraft(setup.Path, draft).Operations.Select(operation => operation.OperationId));
        var selection = ProposalCommands.ComposeIntent(new(setup.Path, Version, draft, "compose-author-feature-value",
            JsonSerializer.Serialize(new { featStruc = created.Id, feature = setup.Feature })));
        Assert.True(selection.Succeeded, selection.Refusal?.Message);
        var spec = Assert.Single(selection.Value!.CreatedObjects);
        Assert.Equal(FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs, spec.Kind);
        Assert.Equal(2, selection.Value.Operations.Count);
        Assert.DoesNotContain(selection.Value.Operations, operation => operation.Kind == FsClosedValueValueOperationKinds.SetValue);
        var authored = ReadDraft(setup.Path, draft);
        var structureCreate = authored.Operations.Single(operation => operation.EntityId == created.Id);
        var specCreate = authored.Operations.Single(operation => operation.EntityId == spec.Id);
        Assert.Equal(created.Id, specCreate.Target);
        Assert.Contains(structureCreate.OperationId, specCreate.DependsOn);
        Assert.Equal(bytes, File.ReadAllBytes(setup.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchesCarryBothDispatchedKindsExactCreatedIdsAndCitations(bool stagedMsas)
    {
        var setup = Prepare();
        const string draft = "cited-features";
        Assert.True(ProposalCommands.New(new(setup.Path, Version, draft, "Synthetic cited feature requirements")).Succeeded);
        var stem = stagedMsas ? StageMsa(setup, draft, false) : setup.StemMsa;
        var infl = stagedMsas ? StageMsa(setup, draft, true) : setup.InflMsa;
        var bytes = File.ReadAllBytes(setup.Path);
        var citation = new { source = "Synthetic grammar", locator = "section 1" };
        var structureBatch = ProposalCommands.ComposeBatch(new(setup.Path, Version, draft,
            JsonSerializer.Serialize(new
            {
                intents = new[]
                {
                    new { verb = "compose-author-feature-structure", intent = new { msa = infl }, citation },
                    new { verb = "compose-author-feature-structure", intent = new { msa = stem }, citation },
                },
            })));
        Assert.True(structureBatch.Succeeded, structureBatch.Refusal?.Message);
        var structures = structureBatch.Value!.Intents.SelectMany(intent => intent.CreatedObjects)
            .ToDictionary(created => created.Kind, created => created.Id);
        Assert.Equal(2, structures.Count);
        var valueBatch = ProposalCommands.ComposeBatch(new(setup.Path, Version, draft,
            JsonSerializer.Serialize(new
            {
                intents = structures.Values.Select(id => new
                {
                    verb = "compose-author-feature-value",
                    intent = new { featStruc = id, feature = setup.Feature, value = setup.Value },
                    citation,
                }).ToArray(),
            })));
        Assert.True(valueBatch.Succeeded, valueBatch.Refusal?.Message);
        Assert.All(valueBatch.Value!.Intents, intent =>
        {
            var spec = Assert.Single(intent.CreatedObjects);
            Assert.Equal(FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs, spec.Kind);
            Assert.Equal(spec.Id, Assert.Single(intent.Operations, operation => operation.Kind == spec.Kind).EntityId);
            Assert.Contains(intent.Operations, operation => operation.Kind == FsClosedValueValueOperationKinds.SetValue);
        });
        var authored = ReadDraft(setup.Path, draft);
        Assert.Equal(stem, authored.Operations.Single(operation => operation.EntityId == structures[StructureKind(false)]).Target);
        Assert.Equal(infl, authored.Operations.Single(operation => operation.EntityId == structures[StructureKind(true)]).Target);
        var ids = structureBatch.Value.Intents.Concat(valueBatch.Value.Intents).SelectMany(intent => intent.Operations)
            .Select(operation => operation.OperationId).ToHashSet(StringComparer.Ordinal);
        var cited = authored.Operations.Where(operation => ids.Contains(operation.OperationId)).ToArray();
        Assert.Equal(8, cited.Length);
        Assert.All(cited, operation =>
        {
            var actual = OperationCitation.FromProvenance(operation.Provenance);
            Assert.NotNull(actual);
            Assert.Equal((citation.source, citation.locator), (actual!.Source, actual.Locator));
        });
        Assert.Equal(bytes, File.ReadAllBytes(setup.Path));
    }

    private sealed record Fixture(string Path, string StemMsa, string InflMsa, string Feature, string Value,
        string Category, string Vern, string Anal);

    private Fixture Prepare()
    {
        using var cache = pristine.NewScratch();
        var seed = pristine.Seed;
        var category = CanonicalId.FromGuid(seed.PartOfSpeechId);
        var infl = CanonicalId.Mint();
        var create = new OperationEnvelope(CanonicalId.Mint(), LexEntryMorphoSyntaxAnalysesOperationKinds.Create,
            target: CanonicalId.FromGuid(seed.FirstEntryId), entityId: infl,
            after: JsonSerializer.SerializeToElement(new { @class = "MoInflAffMsa" }));
        var partOfSpeech = new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
            target: infl, after: JsonSerializer.SerializeToElement(new { @ref = category.Value }));
        var anal = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var featureOps = AuthorInflectionFeatureComposer.Build(cache, new("synthetic-nc", anal, [new("seven")]));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in new[] { create, partOfSpeech }.Concat(featureOps))
                OperationHandlerRegistry.Resolve(operation.Kind, "feature command fixture")
                    .ApplyAndCaptureEffect(cache, operation, []);
        });
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path,
            CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<ILexSenseRepository>()
                .GetObject(seed.FirstSenseId).MorphoSyntaxAnalysisRA.Guid).Value, infl.Value,
            featureOps.Single(operation => operation.Kind == FsFeatureSystemFeaturesOperationKinds.Create).EntityId!.Value.Value,
            featureOps.Single(operation => operation.Kind == FsClosedFeatureValuesOperationKinds.Create).EntityId!.Value.Value,
            category.Value, cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), anal);
    }

    private static string StageMsa(Fixture setup, string draft, bool inflectional)
    {
        var authored = ProposalCommands.ComposeAuthorLexicalEntry(new(setup.Path, Version, draft,
            JsonSerializer.Serialize(new
            {
                form = inflectional ? "ki" : "root",
                formWs = setup.Vern,
                morphType = CanonicalId.FromGuid(inflectional ? MoMorphTypeTags.kguidMorphPrefix : MoMorphTypeTags.kguidMorphStem).Value,
                gloss = "synthetic",
                analysisWs = setup.Anal,
                msa = new { kind = inflectional ? "inflectional" : "stem", category = setup.Category },
            })));
        Assert.True(authored.Succeeded, authored.Refusal?.Message);
        return Assert.Single(authored.Value!.CreatedObjects,
            created => created.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create).Id;
    }

    private static DraftDocument ReadDraft(string path, string name)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(name).ProposalJson!);
    }

    private static string StructureKind(bool inflectional) => inflectional
        ? MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats : MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures;
}
