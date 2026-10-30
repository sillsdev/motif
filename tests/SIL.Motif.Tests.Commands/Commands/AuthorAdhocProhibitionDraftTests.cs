using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AuthorAdhocProhibitionDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string Verb = "compose-author-adhoc-prohibition";

    [Fact]
    public void ProhibitionsBetweenTwoNewEntriesDependOnThemAndApplyAfterADryRun()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var (vernacular, analysis) = Writing(cache);
        new FwDataProjectLoader().Save(cache);
        const string draft = "both-new";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Forbid a split")).Succeeded);
        var one = Author(path, draft, "pamil", vernacular, analysis);
        var two = Author(path, draft, "ya", vernacular, analysis);
        var morphemes = ProposalCommands.ComposeIntent(new(path, Version, draft, Verb,
            Json(new { kind = "morpheme", first = one.Msa, rest = new[] { two.Msa }, adjacency = "adjacent-to-left" })));
        Assert.True(morphemes.Succeeded, morphemes.Refusal?.Message);
        var allomorphs = ProposalCommands.ComposeIntent(new(path, Version, draft, Verb,
            Json(new { kind = "allomorph", first = one.Form, rest = new[] { two.Form } })));
        Assert.True(allomorphs.Succeeded, allomorphs.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, draft, "Stop pamil + ya from being read as two morphemes.").Succeeded);
        var proposal = ReadFinal(path, draft);
        var creates = proposal.Operations.Where(operation => operation.Kind == AdhocProhibitionCreateOperationKinds.Create).ToArray();
        Assert.Equal(2, creates.Length);
        var morphemeCreate = creates.Single(operation => operation.After!.Value.GetProperty("kind").GetString() == "morpheme");
        var allomorphCreate = creates.Single(operation => operation.After!.Value.GetProperty("kind").GetString() == "allomorph");
        Assert.Contains(morphemeCreate.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(one.MsaOperation));
        Assert.Contains(morphemeCreate.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(two.MsaOperation));
        Assert.Contains(allomorphCreate.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(one.FormOperation));
        Assert.Contains(allomorphCreate.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(two.FormOperation));
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "both new");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        var statements = dry.ExpectedEffects.Where(effect => effect.Field == SnapshotFields.MoMorphDataAdhocCoProhibitions)
            .Select(effect => effect.Preview!.Value.GetProperty("prohibits").GetString()).ToArray();
        Assert.Contains("morpheme 'pamil' cannot occur immediately to the left of ya (the first comes before the rest in the word)", statements);
        Assert.Contains("allomorph 'pamil' cannot occur in the same word as ya", statements);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");
        var created = cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC;
        Assert.Equal(2, created.Count);
        var morph = Assert.IsAssignableFrom<IMoMorphAdhocProhib>(created.Single(item => item is IMoMorphAdhocProhib));
        Assert.Equal(CanonicalId.Parse(one.Msa).ToGuid(), morph.FirstMorphemeRA.Guid);
        Assert.Equal(4, morph.Adjacency);
        Assert.IsAssignableFrom<IMoAlloAdhocProhib>(created.Single(item => item is IMoAlloAdhocProhib));
    }

    [Fact]
    public void ADraftRefusesADuplicateAndAnUnknownTarget()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var (vernacular, analysis) = Writing(cache);
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "dup", "Duplicate")).Succeeded);
        var one = Author(path, "dup", "a", vernacular, analysis);
        var two = Author(path, "dup", "b", vernacular, analysis);
        var intent = Json(new { kind = "allomorph", first = one.Form, rest = new[] { two.Form } });
        Assert.True(ProposalCommands.ComposeIntent(new(path, Version, "dup", Verb, intent)).Succeeded);
        var duplicate = ProposalCommands.ComposeIntent(new(path, Version, "dup", Verb, intent));
        Assert.False(duplicate.Succeeded);
        Assert.Equal(RefusalCodes.AdhocProhibitionDuplicate, duplicate.Refusal!.Code);
        var unknown = ProposalCommands.ComposeIntent(new(path, Version, "dup", Verb,
            Json(new { kind = "morpheme", first = one.Msa, rest = new[] { CanonicalId.Mint().Value } })));
        Assert.Equal(RefusalCodes.AdhocProhibitionInvalidMorpheme, unknown.Refusal!.Code);
    }

    [Fact]
    public void TheVerbIsOfferedToAnAgentAsADraftComposer()
    {
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == Verb);
        Assert.True(descriptor.IsAgentTool);
        Assert.Contains(Verb, ProposalCommands.DraftComposers.Keys);
    }

    private sealed record Authored(string Form, string Msa, string FormOperation, string MsaOperation);

    private static (string Vernacular, string Analysis) Writing(LcmCache cache) =>
        (cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs));

    private Authored Author(string path, string draft, string form, string vernacular, string analysis)
    {
        var authored = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, draft, Json(new
        {
            form, formWs = vernacular, morphType = Id(MoMorphTypeTags.kguidMorphStem),
            gloss = "meaning " + form, analysisWs = analysis,
            msa = new { kind = "stem", category = Id(pristine.Seed.PartOfSpeechId) },
        })));
        Assert.True(authored.Succeeded, authored.Refusal?.Message);
        var formOperation = Assert.Single(authored.Value!.Operations, operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var msaOperation = Assert.Single(authored.Value.Operations, operation => operation.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        return new(formOperation.EntityId!, msaOperation.EntityId!, formOperation.OperationId, msaOperation.OperationId);
    }

    private static Proposal ReadFinal(string path, string draft)
    {
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        return stored.Value!;
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;
    private static string Json(object value) => JsonSerializer.Serialize(value);
}
