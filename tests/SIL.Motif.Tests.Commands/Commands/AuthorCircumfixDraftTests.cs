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
public sealed class AuthorCircumfixDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string Verb = "compose-author-circumfix";

    [Fact]
    public void ACircumfixIsStagedInOneComposeAndOfferedToAgents()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "circumfix", "Add ka-...-an")).Succeeded);
        var staged = ProposalCommands.ComposeIntent(new(path, Version, "circumfix", Verb, JsonSerializer.Serialize(new
        {
            prefix = "ka", suffix = "an",
            formWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            gloss = "abstract", analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs),
            msa = new { kind = "unclassified", category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value },
        })));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(2, staged.Value!.Operations.Count(operation => operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms));
        Assert.Contains(staged.Value.Operations, operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == Verb);
        Assert.True(descriptor.IsAgentTool);
    }
}
