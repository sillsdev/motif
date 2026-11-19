using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AuthorCompoundRuleDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string Verb = "compose-author-compound-rule";

    [Fact]
    public void ADraftStagesAnEndocentricAndAnExocentricRuleThenDryRunsAndAppliesThem()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var noun = Id(pristine.Seed.PartOfSpeechId);
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "compounds", "Author compounds")).Succeeded);
        var endocentric = ProposalCommands.ComposeIntent(new(path, Version, "compounds", Verb,
            Json(new { kind = "endocentric", name = "N+N", left = noun, right = noun })));
        Assert.True(endocentric.Succeeded, endocentric.Refusal?.Message);
        var exocentric = ProposalCommands.ComposeIntent(new(path, Version, "compounds", Verb,
            Json(new { kind = "exocentric", left = noun, right = noun, result = noun })));
        Assert.True(exocentric.Succeeded, exocentric.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, "compounds", "Compounds that were listed as allomorphs.").Succeeded);

        var proposal = ReadFinal(path, "compounds");
        var creates = proposal.Operations.Where(operation => operation.Kind == CompoundRuleCreateOperationKinds.Create).ToArray();
        Assert.Equal(2, creates.Length);
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "compound rules");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        var statements = dry.ExpectedEffects.Where(effect => effect.Field == SnapshotFields.MoMorphDataCompoundRules)
            .Select(effect => effect.Preview!.Value.GetProperty("compounds").GetString()).ToArray();
        Assert.Contains(statements, text => text!.StartsWith("endocentric:", StringComparison.Ordinal));
        Assert.Contains(statements, text => text!.StartsWith("exocentric:", StringComparison.Ordinal));

        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");
        var rules = cache.LangProject.MorphologicalDataOA.CompoundRulesOS;
        Assert.Equal(2, rules.Count);
        Assert.IsAssignableFrom<IMoEndoCompound>(rules[0]);
        Assert.IsAssignableFrom<IMoExoCompound>(rules[1]);
    }

    [Fact]
    public void ADraftRefusesAnExocentricRuleWithoutAResultAnUnknownCategoryAndADuplicate()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var noun = Id(pristine.Seed.PartOfSpeechId);
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "bad", "Bad compounds")).Succeeded);

        var noResult = ProposalCommands.ComposeIntent(new(path, Version, "bad", Verb,
            Json(new { kind = "exocentric", left = noun, right = noun })));
        Assert.False(noResult.Succeeded);
        Assert.Contains("needs a result category", noResult.Refusal!.Message, StringComparison.Ordinal);

        var unknown = ProposalCommands.ComposeIntent(new(path, Version, "bad", Verb,
            Json(new { kind = "endocentric", left = noun, right = CanonicalId.Mint().Value })));
        Assert.Equal(RefusalCodes.CompoundRuleInvalidCategory, unknown.Refusal!.Code);

        var intent = Json(new { kind = "endocentric", left = noun, right = noun });
        Assert.True(ProposalCommands.ComposeIntent(new(path, Version, "bad", Verb, intent)).Succeeded);
        var duplicate = ProposalCommands.ComposeIntent(new(path, Version, "bad", Verb, intent));
        Assert.Equal(RefusalCodes.CompoundRuleDuplicate, duplicate.Refusal!.Code);
    }

    [Fact]
    public void TheVerbIsOfferedToAnAgentAsADraftComposer()
    {
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == Verb);
        Assert.True(descriptor.IsAgentTool);
        Assert.Contains(Verb, ProposalCommands.DraftComposers.Keys);
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
