using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ExceptionFeatureDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    private static string Intent(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void ComposeIntentStagesANewExceptionFeatureWithItsNameAndAbbreviation()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "feature", "Add a feature")).Succeeded);

        var staged = ProposalCommands.ComposeIntent(new(path, Version, "feature", "compose-author-exception-feature",
            Intent(new { name = "Loan stem", ws, abbreviation = "loan" })));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(["lists/cmPossibilityList/createPossibilities", "lists/cmPossibility/setName",
                "lists/cmPossibility/setAbbreviation"],
            staged.Value!.Operations.Select(item => item.Kind));
    }

    [Fact]
    public void ATagWithAFeatureTheProjectDoesNotHoldIsRefusedAndStagesNothing()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "tag", "Tag a stem")).Succeeded);

        var refused = ProposalCommands.ComposeIntent(new(path, Version, "tag", "compose-edit-exception-features",
            Intent(new
            {
                msa = CanonicalId.FromGuid(Guid.NewGuid()).Value,
                exceptionFeatures = new[] { CanonicalId.FromGuid(Guid.NewGuid()).Value },
            })));

        Assert.False(refused.Succeeded);
    }
}
