using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class LexiconDraftTests(PristineProjectFixture pristine)
{
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
}
