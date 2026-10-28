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
public sealed class SetEntryDoNotParseDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ComposeStagesTheFlagAndRefusesAStaleValueWithoutChangingTheProject()
    {
        string path;
        using (var cache = pristine.NewScratch()) path = cache.ProjectId.Path;
        var target = CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value;
        Assert.True(ProposalCommands.New(new(path, "1.0", "entry-flag", null)).Succeeded);
        var stale = Compose(true, false);
        Assert.False(stale.Succeeded);
        Assert.Contains("expected", stale.Refusal!.Message, StringComparison.OrdinalIgnoreCase);
        var staged = Compose(false, true);
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(LexEntryDoNotUseForParsingOperationKinds.SetDoNotUseForParsing,
            Assert.Single(staged.Value!.Operations).Kind);
        using var reopened = new FwDataProjectLoader().LoadCache(path);
        Assert.False(reopened.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId).DoNotUseForParsing);

        SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Contract.Responses.ComposedOperationsResponse> Compose(
            bool expected, bool desired) => ProposalCommands.ComposeIntent(new(path, "1.0", "entry-flag",
                "compose-set-entry-do-not-parse", JsonSerializer.Serialize(new
                { target, expectedDoNotParse = expected, doNotParse = desired })));
    }
}
