using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RewriteLexemeFormDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string Verb = "compose-rewrite-lexeme-form";

    [Fact]
    public void TheComposerIsADraftAgentToolAndAStaleBatchStagesNothing()
    {
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == Verb);
        Assert.True(descriptor.IsAgentTool);

        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var seeds = Enumerable.Range(0, 3).Select(i => EmptySenseSeed.Add(cache, "var" + i)).ToArray();
        new FwDataProjectLoader().Save(cache);
        var ws = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
        Assert.True(ProposalCommands.New(new(path, Version, "d", "Rewrite")).Succeeded);

        object Entry(EmptySenseSeed seed, string expected) =>
            new { entryGuid = CanonicalId.FromGuid(seed.EntryId).Value, writingSystem = ws, expectedForm = expected, newForm = "x" };

        var stale = ProposalCommands.ComposeIntent(new(path, Version, "d", Verb, JsonSerializer.Serialize(new
        {
            entries = new[] { Entry(seeds[0], "var0"), Entry(seeds[1], "wrong") },
        })));
        Assert.False(stale.Succeeded);
        Assert.Contains("not the expected 'wrong'", stale.Refusal!.Message, StringComparison.Ordinal);

        var staged = ProposalCommands.ComposeIntent(new(path, Version, "d", Verb, JsonSerializer.Serialize(new
        {
            setEmptyCitationForm = true,
            entries = seeds.Select((seed, i) => Entry(seed, "var" + i)).ToArray(),
        })));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(6, staged.Value!.Operations.Count);
        Assert.Equal(3, staged.Value.Operations.Count(op => op.Kind == MoFormFormOperationKinds.SetForm));
    }
}
