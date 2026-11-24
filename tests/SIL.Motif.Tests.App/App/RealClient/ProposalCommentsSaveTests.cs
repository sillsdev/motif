using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ProposalCommentsSaveTests(PristineProjectFixture pristine)
{
    [UiThreadTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARefusedCommentSaveKeepsTypingAndARefreshFreezesTheClosedProposal(bool existing)
    {
        using var project = new WalkthroughProject(pristine);
        const string draft = "comment-save";
        var created = ProposalCommands.New(new NewDraftRequest(project.FwDataPath, MotifProductVersion.CurrentText,
            draft, "Comment save"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(project.FwDataPath, MotifProductVersion.CurrentText,
            draft, CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, "en", "A revised gloss"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        DraftRationale.Author(project.FwDataPath, draft, draft, "Explain the revised gloss.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(project.FwDataPath,
            MotifProductVersion.CurrentText, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var id = finalized.Value!.ProposalId;
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        if (existing)
        {
            var added = await client.AddCommentAsync(new AddCommentRequest(project.FwDataPath,
                MotifProductVersion.CurrentText, id, "A stored comment", "Person"), CancellationToken.None);
            Assert.True(added.Succeeded, added.Refusal?.Message);
        }
        var context = WorkspaceContextTests.NewContext(client);
        await context.OpenProjectAsync(project.FwDataPath);
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open(id);
        try
        {
            await document.RefreshAsync();
            if (!existing) document.AddCommentCommand.Execute(null);
            var editor = Assert.Single(document.Comments);
            if (existing) editor.EditCommand.Execute(null);
            editor.Document.Paragraphs[0].Runs[0].Text += " typed";
            var typed = editor.Text();
            var declined = ProposalLifecycleCommand.Decline(new DeclineProposalRequest(project.FwDataPath, id));
            Assert.True(declined.Succeeded, declined.Refusal?.Message);
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.NotNull(editor.Refusal);
            Assert.True(editor.IsEditing);
            Assert.Equal(typed, editor.Text());
            await document.RefreshAsync();
            Assert.Contains(editor, document.Comments);
            Assert.Equal(typed, editor.Text());
            Assert.False(editor.SaveCommand.CanExecute(null));
            Assert.False(document.CanAddComment);
            editor.CancelCommand.Execute(null);
        }
        finally
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }
}
