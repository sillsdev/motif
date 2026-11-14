using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CommentEditorTests(AvaloniaHeadlessFixture avalonia)
{
    private static async Task<(FakeCommandClient Fake, RationaleEditorModel Editor, List<RationaleEditorModel> Removed)> OpenAsync(
        CommentView? comment, string status = "proposed")
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync(@"C:\projects\one.fwdata");
        var removed = new List<RationaleEditorModel>();
        var detail = new ProposalDetailProjection("p/one", status, null, null, null, []);
        return (fake, new RationaleEditorModel(context, detail, comment, removed.Add), removed);
    }

    private static CommentView AgentComment() => new(42, "agent", "Agent", "", "A **word**.", null,
        "2026-01-01T00:00:00Z", null, false, null);

    [Fact]
    public async Task AnAgentCommentIsRenderedInTheSharedFieldAndCanBeEdited()
    {
        var (_, editor, _) = await OpenAsync(AgentComment());
        avalonia.Invoke(() =>
        {
            var view = new RationaleEditor { DataContext = editor };
            Assert.True(view.Field!.IsReadOnly);
            Assert.Same(editor.Document, view.Field.Editor.Document);
            editor.EditCommand.Execute(null);
            Assert.False(view.Field!.IsReadOnly);
        });
    }

    [Fact]
    public async Task APersonAddsACommentAndSeesFindings()
    {
        var (fake, editor, _) = await OpenAsync(null);
        fake.CommentFindings = [new TextFinding("bad", 1, 2, "A finding")];
        editor.Document.Paragraphs[0].Runs[0].Text = "A comment";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("A comment", Assert.Single(fake.AddCommentRequests).Text);
        Assert.Equal(["1:2 A finding"], editor.Findings);
        Assert.False(editor.IsEditing);
        Assert.True(editor.CanDeleteComment);
    }

    [Fact]
    public async Task APersonEditsAndDeletesAnAgentComment()
    {
        var (fake, editor, removed) = await OpenAsync(AgentComment());
        Assert.Equal("Agent", editor.Author);
        Assert.False(editor.IsPlainText);
        editor.EditCommand.Execute(null);
        editor.Document.Paragraphs[0].Runs[^1].Text += " More";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(42, Assert.Single(fake.EditCommentRequests).CommentId);
        await editor.DeleteCommentCommand.ExecuteAsync(null);
        Assert.Equal(42, Assert.Single(fake.DeleteCommentRequests).CommentId);
        Assert.Same(editor, Assert.Single(removed));
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("rejected")]
    [InlineData("superseded")]
    [InlineData("withdrawn")]
    public async Task DecidedProposalsFreezeComments(string status)
    {
        var (_, editor, _) = await OpenAsync(AgentComment(), status);
        Assert.False(editor.CanStartEdit);
        Assert.False(editor.CanDeleteComment);
    }
}
