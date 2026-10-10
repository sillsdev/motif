using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class ComposerProvenanceTests(PristineProjectFixture pristine)
{
    [Fact]
    public void EveryComposerProvenanceEntryListsItsProducedOperations()
    {
        using var cache = pristine.NewScratch();
        var projectPath = cache.ProjectId.Path;
        cache.Dispose();
        const string draftName = "provenance";
        Assert.True(ProposalCommands.New(new NewDraftRequest(projectPath, "1.0", draftName, "Add a phoneme"))
            .Succeeded);

        var composed = ProposalCommands.ComposeAuthorPhoneme(new ComposeAuthorPhonemeRequest(
            projectPath, "1.0", draftName,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(projectPath);
        var draft = new ProposalRepository(database).GetDraft(draftName);
        using var document = JsonDocument.Parse(draft.ProposalJson!);
        var expected = document.RootElement.GetProperty("operations").EnumerateArray()
            .Select(operation => operation.GetProperty("operationId").GetString()).ToArray();
        var provenance = Assert.Single(document.RootElement.GetProperty("composerProvenance").EnumerateArray());
        var actual = provenance.GetProperty("operationIds").EnumerateArray()
            .Select(operation => operation.GetString()).ToArray();

        Assert.Equal(expected, actual);
    }
}
