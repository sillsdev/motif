using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class DiscardEmptyDraftsTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void EmptyDraftsGoAndADraftWithChangesStays()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        foreach (var name in new[] { "orphan-a", "orphan-b", "work" })
            Assert.True(ProposalCommands.New(new(path, Version, name, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, "work",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag, "gloss")).Succeeded);

        var outcome = ProposalCommands.DiscardEmptyDrafts(new DiscardEmptyDraftsRequest(path, Version));

        Assert.True(outcome.Succeeded);
        Assert.Equal(["orphan-a", "orphan-b"], outcome.Value!.Discarded.Order(StringComparer.Ordinal));
        Assert.Empty(outcome.Value.Kept);
        Assert.True(ProposalCommands.New(new(path, Version, "orphan-a", null)).Succeeded);
        Assert.Equal("draft.name-collision", ProposalCommands.New(new(path, Version, "work", null)).Refusal?.Code);
    }

    [Fact]
    public void APrefixLimitsWhichEmptyDraftsAreConsidered()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        foreach (var name in new[] { "chain-1", "other" })
            Assert.True(ProposalCommands.New(new(path, Version, name, null)).Succeeded);

        var outcome = ProposalCommands.DiscardEmptyDrafts(new DiscardEmptyDraftsRequest(path, Version, "chain-"));

        Assert.Equal(["chain-1"], outcome.Value!.Discarded);
        Assert.Equal("draft.name-collision", ProposalCommands.New(new(path, Version, "other", null)).Refusal?.Code);
    }

    [Fact]
    public void NothingToDiscardSucceedsWithAnEmptyList()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var outcome = ProposalCommands.DiscardEmptyDrafts(new DiscardEmptyDraftsRequest(path, Version));
        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Discarded);
    }
}
