using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class EditPhonemeDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void ComposeIntentStagesDigraphsOnAnEarlierStagedPhonemeAtomically()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "sounds", "Digraphs")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "sounds",
            JsonSerializer.Serialize(new { name = "n", representations = new[] { "n" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var id = Assert.Single(phoneme.Value!.Operations, o => o.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;

        var edited = ProposalCommands.ComposeIntent(new(path, Version, "sounds", "compose-edit-phoneme",
            JsonSerializer.Serialize(new { target = id, add = new[] { new { representation = "ng" } } })));

        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        Assert.Equal(phoneme.Value.OperationCount + 2, edited.Value!.OperationCount);
    }

    [Fact]
    public void ARefusedEditStagesNothingAndNamesTheRefusal()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "dup", "Digraphs")).Succeeded);
        var first = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "dup",
            JsonSerializer.Serialize(new { name = "n", representations = new[] { "n" } })));
        var id = Assert.Single(first.Value!.Operations, o => o.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;

        var refused = ProposalCommands.ComposeIntent(new(path, Version, "dup", "compose-edit-phoneme",
            JsonSerializer.Serialize(new { target = id, add = new[] { new { representation = "n" } } })));

        Assert.False(refused.Succeeded);
        Assert.Contains("phoneme-representation-duplicate", refused.Refusal!.Message);
    }
}
