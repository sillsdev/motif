using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RemoveOperationsContractGroupTests(PristineProjectFixture pristine)
{
    private const string Version = "1.0";
    private const string DraftName = "groups";

    [Fact]
    public void RemovingTheOnlyOperationOfAGroupDropsItFromTheContractVersionsAndTheDraftStillParses()
    {
        var path = NewDraft();
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new ComposeAuthorPhonemeRequest(path, Version, DraftName,
            JsonSerializer.Serialize(new { name = "ɸ", representations = new[] { "ɸ" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, DraftName,
            SIL.Motif.Contract.Ids.CanonicalId.Mint().Value, "en", "a gloss"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        Assert.Equal(2, Groups(path).Count);

        var removed = ProposalCommands.RemoveOperations(new RemoveOperationsRequest(path, Version, DraftName,
            phoneme.Value!.Operations.Select(operation => operation.OperationId).ToArray(), Force: true));

        Assert.True(removed.Succeeded, removed.Refusal?.Message);
        Assert.Equal(["lexical"], Groups(path));
        Assert.True(DraftRationale.Write(path, Version, DraftName, "A comment.").Succeeded);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, Version, DraftName)).Succeeded);
    }

    [Fact]
    public void RemovingEveryOperationLeavesADraftThatStillParses()
    {
        var path = NewDraft();
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, DraftName,
            SIL.Motif.Contract.Ids.CanonicalId.Mint().Value, "en", "a gloss"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);

        var removed = ProposalCommands.RemoveOperations(new RemoveOperationsRequest(path, Version, DraftName,
            [gloss.Value!.OperationId], Force: false));

        Assert.True(removed.Succeeded, removed.Refusal?.Message);
        Assert.Empty(Groups(path));
        using var database = ProjectMotifDatabase.Open(path);
        var draft = ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(DraftName).ProposalJson!);
        _ = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
    }

    private string NewDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, Version, DraftName, "Groups")).Succeeded);
        return path;
    }

    private static List<string> Groups(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(DraftName).ProposalJson!)
            .ContractVersions.Keys.Order(StringComparer.Ordinal).ToList();
    }
}
