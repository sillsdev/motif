using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class MetathesisRuleDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AMetathesisRuleIsStagedInADraftAndReplaysThroughTheDryRun()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        const string draft = "h-c-metathesis";
        const string version = "0.1.0";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Switch h and a consonant")).Succeeded);
        string Phoneme(string letter)
        {
            var composed = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
                JsonSerializer.Serialize(new { name = letter, representations = new[] { letter } })));
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
            return Assert.Single(composed.Value!.Operations, o => o.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value;
        }
        var h = Phoneme("h");
        var m = Phoneme("m");
        var composedRule = ProposalCommands.ComposeAuthorMetathesisRule(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                name = "h and m switch", direction = "simultaneous", leftSwitch = new { phoneme = h },
                rightSwitch = new { phoneme = m },
            })));
        Assert.True(composedRule.Succeeded, composedRule.Refusal?.Message);
        Assert.Contains(composedRule.Value!.Operations, o => o.Kind == PhMetathesisRuleStrucChangeOperationKinds.Set);
        Assert.Contains(composedRule.Value.CreatedObjects, o => o.Kind == PhPhonDataPhonRulesOperationKinds.Create);

        var loaded = ProjectStoreCommand.Run(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(
                ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(draft).ProposalJson!)))));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        using var scratch = DryRunScratch.Adopt(new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path),
            "Metathesis draft replay");
        var result = ProposalDryRunner.Run(scratch, loaded.Value!);
        Assert.Contains(result.ExpectedEffects, effect => effect.Field == SIL.Motif.Model.Snapshot.SnapshotFields.PhMetathesisRuleStrucChange);
    }
}
