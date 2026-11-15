using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalCommentsIsolationTests
{
    [Fact]
    public async Task SwitchingAndRefreshingKeepTypingInEveryDocumentAndClosingHonorsTheGuard()
    {
        var fake = new FakeCommandClient();
        fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, "proposed", request.ProposalId, null, "intent", [], Comments:
                [new CommentView(1, "agent", "Builder", "", request.ProposalId, null, "now", null, false, null)])));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var one = documents.Open("one");
        await one.RefreshAsync();
        var comment = Assert.Single(one.Comments);
        comment.EditCommand.Execute(null);
        comment.Document.Paragraphs[0].Runs[0].Text += " typed";
        var two = documents.Open("two");
        await two.RefreshAsync();
        two.AddCommentCommand.Execute(null);
        var second = Assert.Single(two.Comments, editor => editor.CommentId is null);
        second.Document.Paragraphs[0].Runs[0].Text = "second typing";
        await documents.RefreshAsync();
        Assert.Same(comment, Assert.Single(one.Comments));
        Assert.Contains("typed", comment.Text());
        Assert.Contains(second, two.Comments);
        Assert.Contains("second typing", second.Text());
        Assert.False(await documents.CloseAsync(one));
        Assert.True(comment.IsSwitchRefused);
        comment.CancelCommand.Execute(null);
        Assert.True(await documents.CloseAsync(one));
        Assert.Same(two, documents.Selected);
        second.CancelCommand.Execute(null);
        await documents.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedProposalFreezesCommentsWithoutDiscardingTyping(bool draft)
    {
        var fake = new FakeCommandClient();
        var status = "proposed";
        fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, status, request.ProposalId, null, "intent", [], DraftName: draft ? "working" : null, Comments:
                [new CommentView(1, "agent", "Builder", "", "Old", null, "now", null, false, null)])));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("one");
        await document.RefreshAsync();
        var rationale = document.RationaleEditor!;
        rationale.EditCommand.Execute(null);
        rationale.Document.Paragraphs[0].Runs[0].Text = "rationale typing";
        var rationaleTyping = rationale.Text();
        rationale.MarkChangedElsewhere();
        Assert.True(rationale.CanCompare);
        var editor = Assert.Single(document.Comments);
        editor.EditCommand.Execute(null);
        editor.Document.Paragraphs[0].Runs[0].Text += " typed";
        status = "rejected";
        await document.RefreshAsync();
        Assert.Same(editor, Assert.Single(document.Comments));
        Assert.Contains("typed", editor.Text());
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.RestoreCommand.CanExecute(null));
        Assert.False(document.CanAddComment);
        Assert.Same(rationale, document.RationaleEditor);
        Assert.Equal(rationaleTyping, rationale.Text());
        Assert.False(rationale.CanCompare);
        Assert.False(rationale.RestoreCommand.CanExecute(null));
        editor.CancelCommand.Execute(null);
        Assert.False(Assert.Single(document.Comments).CanStartEdit);
        await documents.StopAsync();
    }
}
