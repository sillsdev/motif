using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AddMorphoSyntaxAnalysisDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string Verb = "compose-add-morphosyntax-analysis";

    [Fact]
    public void AnAnalysisIsStagedOnAnExistingEntryInOneComposeAndOfferedToAgents()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var entry = CanonicalId.FromGuid(pristine.Seed.FirstEntryId);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "copula", "Add a Copula analysis")).Succeeded);
        var staged = ProposalCommands.ComposeIntent(new(path, Version, "copula", Verb, JsonSerializer.Serialize(new
        {
            entry = entry.Value,
            msa = new { kind = "stem", category = category.Value },
            gloss = "second sense", analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs),
        })));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Contains(staged.Value!.Operations, operation => operation.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Assert.Contains(staged.Value.Operations, operation => operation.Kind == LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis);
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == Verb);
        Assert.True(descriptor.IsAgentTool);
    }
}
