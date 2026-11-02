using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class EmptyDraftCollisionTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void ANewDraftNamedLikeAnEmptyDraftIsRefusedSayingItIsEmptyAndHowToClearIt()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        Assert.True(ProposalCommands.New(new(path, Version, "chain", null)).Succeeded);

        var again = ProposalCommands.New(new(path, Version, "chain", null));

        Assert.Equal("draft.name-collision", again.Refusal?.Code);
        Assert.Contains("holds no changes", again.Refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("discard-draft --draft chain", again.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewDraftNamedLikeADraftWithChangesKeepsTheFinalizeOrRenameAdvice()
    {
        var seed = pristine.Seed;
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        Assert.True(ProposalCommands.New(new(path, Version, "work", null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, "work",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag, "gloss")).Succeeded);

        var again = ProposalCommands.New(new(path, Version, "work", null));

        Assert.Equal("draft.name-collision", again.Refusal?.Code);
        Assert.Contains("Finalize it", again.Refusal!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("holds no changes", again.Refusal.Message, StringComparison.Ordinal);
    }
}
