using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
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
    public async Task CommentFormattingAndLinkAreRendered()
    {
        var comment = AgentComment() with { Markdown = "A **bold** [link](/lexicon/entry-1){guid=11111111-2222-4333-8444-555555555555}." };
        var (_, editor, _) = await OpenAsync(comment);
        Assert.False(editor.IsPlainText);
        var runs = Assert.Single(editor.Document.Paragraphs).Runs;
        var bold = Assert.Single(runs, run => run.Text == "bold");
        Assert.True(bold.Props.TryGetInt(TextPropertyNumbers.Bold, out var weight));
        Assert.Equal(TextPropertyNumbers.ToggleOn, weight);
        var link = Assert.Single(runs, run => run.ObjData is not null);
        Assert.Equal("silfw", RationaleEditorModel.LinkOf(link.ObjData)?.Scheme);
        Assert.Equal(comment.Markdown, editor.Text());
    }

    [Fact]
    public async Task StoredCommentShowsItsFindings()
    {
        var comment = AgentComment() with { Markdown = "An <b>unread</b> tag." };
        var (_, editor, _) = await OpenAsync(comment);
        Assert.NotEmpty(editor.Findings);
        var before = editor.Findings.ToArray();
        editor.EditCommand.Execute(null);
        editor.CancelCommand.Execute(null);
        Assert.Equal(before, editor.Findings);
        var checkedComment = AgentComment() with { Findings = [new TextFinding("project", 1, 3, "Missing link")] };
        var (_, decided, _) = await OpenAsync(checkedComment, "rejected");
        Assert.Equal(["1:3 Missing link"], decided.Findings);
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
        var request = Assert.Single(fake.EditCommentRequests);
        Assert.Equal(42, request.CommentId);
        Assert.Contains(" More", request.Text);
        Assert.Equal(request.Text, editor.Markdown);
        var (_, reloaded, _) = await OpenAsync(AgentComment() with { Markdown = request.Text });
        Assert.Equal(request.Text, reloaded.Text());
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
