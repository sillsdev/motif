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

    [Fact]
    public void ABatchNamesTheTwoPartsOfACircumfixByTheirMorphType()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var suffixType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphSuffix).Value;
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        foreach (var draft in new[] { "right", "swapped", "unknown" })
            Assert.True(ProposalCommands.New(new(path, Version, draft, "Add ka-...-an")).Succeeded);
        object Circumfix() => new
        {
            verb = Verb, @as = "circ",
            intent = new
            {
                prefix = "ka", suffix = "an", formWs = vernacular, gloss = "abstract", analysisWs = analysis,
                msa = new { kind = "unclassified", category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value },
            },
        };
        object Form(object placement) => new
        {
            verb = "compose-author-alternate-form",
            intent = new { entry = "@circ", form = "a", formWs = vernacular, morphType = suffixType, placement },
        };
        string Batch(object placement) => JsonSerializer.Serialize(new { intents = new[] { Circumfix(), Form(placement) } });

        var right = ProposalCommands.ComposeBatch(new(path, Version, "right",
            Batch(new { after = "@circ:prefix", before = "@circ:suffix" })));
        var swapped = ProposalCommands.ComposeBatch(new(path, Version, "swapped",
            Batch(new { after = "@circ:suffix", before = "@circ:prefix" })));
        var unknown = ProposalCommands.ComposeBatch(new(path, Version, "unknown", Batch(new { before = "@circ:infix" })));

        Assert.True(right.Succeeded, right.Refusal?.Message);
        Assert.Equal(3, right.Value!.Intents.SelectMany(intent => intent.Operations)
            .Count(operation => operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms));
        Assert.False(swapped.Succeeded);
        Assert.Contains("not adjacent", swapped.Refusal!.Message, StringComparison.Ordinal);
        Assert.False(unknown.Succeeded);
        Assert.Contains("@circ:infix", unknown.Refusal!.Message, StringComparison.Ordinal);
    }
}
