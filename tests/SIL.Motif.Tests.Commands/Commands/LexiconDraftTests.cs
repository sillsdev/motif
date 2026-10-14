using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class LexiconDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public void InflectionPiecesAndAnEntryComposeTogetherWithCrossIntentDependencies()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        cache.Dispose();
        const string version = "0.1.0";
        const string draft = "inflection-noun";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Add inflection pieces")).Succeeded);
        var feature = ProposalCommands.ComposeAuthorInflectionFeature(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "Number", ws, values = new[] { new { name = "Plural", abbreviation = "pl" } } })));
        Assert.True(feature.Succeeded, feature.Refusal?.Message);
        AssertCreatedObjectsMatch(feature.Value!);
        var featureCreate = feature.Value!.Operations.Single(op => op.Kind == "grammar/fsFeatureSystem/createFeatures");
        var valueCreate = feature.Value.Operations.Single(op => op.Kind == "grammar/fsClosedFeature/createValues");
        var cls = ProposalCommands.ComposeAuthorInflectionClass(new(path, version, draft,
            JsonSerializer.Serialize(new { category = category.Value, name = "Class Seven", ws })));
        Assert.True(cls.Succeeded, cls.Refusal?.Message);
        AssertCreatedObjectsMatch(cls.Value!);
        var classCreate = cls.Value!.Operations.Single(op => op.Kind == "grammar/partOfSpeech/createInflectionClasses");
        var stem = ProposalCommands.ComposeAuthorStemName(new(path, version, draft, JsonSerializer.Serialize(new
        {
            category = category.Value, name = "Plural stem", ws,
            regions = new[] { new[] { new { feature = featureCreate.EntityId, value = valueCreate.EntityId } } },
        })));
        Assert.True(stem.Succeeded, stem.Refusal?.Message);
        AssertCreatedObjectsMatch(stem.Value!);
        var entry = ProposalCommands.ComposeAuthorLexicalEntry(new(path, version, draft, JsonSerializer.Serialize(new
        {
            form = "vitabu", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            gloss = "books", analysisWs = ws, msa = new { kind = "stem", category = category.Value, inflectionClass = classCreate.EntityId },
        })));
        Assert.True(entry.Succeeded, entry.Refusal?.Message);
        AssertCreatedObjectsMatch(entry.Value!);
        Assert.Equal(4, entry.Value!.CreatedObjects.Count);
        Assert.True(ProposalCommands.Label(new(path, version, draft, "Add an inflected noun")).Succeeded);
        Assert.True(ProposalCommands.Comment(new(path, version, draft, "Explicit grammar pieces and a noun.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run<Proposal>(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;
        foreach (var (kind, predecessor) in new[]
        {
            ("grammar/fsFeatureSpecification/setFeature", featureCreate.OperationId),
            ("grammar/fsClosedValue/setValue", valueCreate.OperationId),
            ("grammar/moStemMsa/setInflectionClass", classCreate.OperationId),
        })
            Assert.Contains(CanonicalId.Parse(predecessor), Assert.Single(proposal.Operations, op => op.Kind == kind)
                .DependsOn.Select(dependency => dependency.OperationId));
        using var scratch = DryRunScratch.Adopt(new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "inflection pieces");
        var effects = ProposalDryRunner.Run(scratch, proposal).ExpectedEffects;
        foreach (var field in new[] { "grammar/fsFeatureSystem/features", "grammar/moStemName/regions", "grammar/moStemMsa/inflectionClass" })
            Assert.Contains(effects, effect => effect.Field == field);
    }

    [Fact]
    public void AnEntryUsesAPartOfSpeechCreatedEarlierInTheSameDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();
        const string version = "0.1.0";
        const string draft = "new-noun";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Add a noun")).Succeeded);

        var category = ProposalCommands.ComposeAuthorPartOfSpeech(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "Nomino", ws = analysis, abbreviation = "n" })));
        Assert.True(category.Succeeded, category.Refusal?.Message);
        AssertCreatedObjectsMatch(category.Value!);
        var categoryCreate = category.Value!.Operations.Single(operation => operation.EntityId is not null);

        var entry = ProposalCommands.ComposeAuthorLexicalEntry(new(path, version, draft, JsonSerializer.Serialize(new
        {
            form = "kitabu",
            formWs = vernacular,
            morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            gloss = "book",
            analysisWs = analysis,
            definition = "a bound set of written pages",
            msa = new { kind = "stem", category = categoryCreate.EntityId },
        })));
        Assert.True(entry.Succeeded, entry.Refusal?.Message);
        AssertCreatedObjectsMatch(entry.Value!);

        var duplicate = ProposalCommands.ComposeAuthorPartOfSpeech(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "Nomino", ws = analysis })));
        Assert.False(duplicate.Succeeded);
        Assert.Contains("already exists", duplicate.Refusal!.Message, StringComparison.Ordinal);

        Assert.True(ProposalCommands.Label(new(path, version, draft, "Add kitabu")).Succeeded);
        Assert.True(ProposalCommands.Comment(new(path, version, draft, "A noun and its first entry.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run<Proposal>(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;
        var setCategory = Assert.Single(proposal.Operations,
            operation => operation.Kind == "grammar/moStemMsa/setPartOfSpeech");
        Assert.Contains(CanonicalId.Parse(categoryCreate.OperationId),
            setCategory.DependsOn.Select(dependency => dependency.OperationId));

        using var scratch = DryRunScratch.Adopt(
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "new noun entry");
        var effects = ProposalDryRunner.Run(scratch, proposal).ExpectedEffects;
        Assert.Contains(effects, effect => effect.Field == "lexical/lexDb/entries");
        Assert.Contains(effects, effect => effect.Field == "lexical/lexSense/definition");
    }

    private static void AssertCreatedObjectsMatch(ComposedOperationsResponse response)
    {
        var expected = response.Operations.Where(operation => operation.EntityId is not null)
            .Select(operation => new CreatedObject(operation.EntityId!, operation.Kind)).ToArray();
        Assert.Equal(expected, response.CreatedObjects);
    }
}
