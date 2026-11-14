using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>A Proposal's Rationale shown and edited in the shared rich field (ADR 0099, #134).</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class RationaleEditorModelTests(AvaloniaHeadlessFixture avalonia)
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string Id = "proposal/one";
    private const string Guid = "11111111-2222-4333-8444-555555555555";
    private static readonly string Linked = $"Glosses **gloss-1** as [entry-1](/lexicon/entry-1){{guid={Guid}}} does.";

    [Fact]
    public async Task TheRationaleIsShownRenderedWithItsLinkAndIsReadOnlyUntilEdit()
    {
        var (_, editor) = await OpenAsync(Linked);

        Assert.False(editor.IsPlainText);
        Assert.False(editor.IsEditing);
        Assert.True(editor.CanStartEdit);
        var runs = Assert.Single(editor.Document.Paragraphs).Runs;
        Assert.Contains(runs, run => run.Text == "gloss-1");
        var link = Assert.Single(runs, run => run.ObjData is not null);
        Assert.Equal("silfw", RationaleEditorModel.LinkOf(link.ObjData)?.Scheme);
        Assert.Equal(Linked, editor.Text());
    }

    [Fact]
    public async Task ALinkOpensWhatItNames()
    {
        var launcher = new RecordingLauncher();
        var (_, editor) = await OpenAsync(Linked, launcher: launcher);
        var link = editor.Document.Paragraphs[0].Runs.Single(run => run.ObjData is not null);

        Assert.True(await editor.OpenLinkAsync(link.ObjData));
        Assert.Contains(Guid, Assert.Single(launcher.Launches).ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(await editor.OpenLinkAsync(null));
    }

    [Fact]
    public async Task SaveWritesTheFieldsTextAndLeavesEditing()
    {
        var (fake, editor) = await OpenAsync("Why it is right.");

        editor.EditCommand.Execute(null);
        Typed(editor, " And more.");
        await editor.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(fake.SetRationaleRequests);
        Assert.Equal((Id, "Why it is right. And more."), (saved.ProposalId, saved.Text));
        Assert.False(editor.IsEditing);
        Assert.Equal("Why it is right. And more.", editor.Markdown);
        Assert.Empty(editor.Findings);
    }

    [Fact]
    public async Task CancelDiscardsTheEdit()
    {
        var (fake, editor) = await OpenAsync("Why it is right.");

        editor.EditCommand.Execute(null);
        Typed(editor, " Typed.");
        editor.CancelCommand.Execute(null);

        Assert.Empty(fake.SetRationaleRequests);
        Assert.False(editor.IsEditing);
        Assert.Equal("Why it is right.", editor.Text());
    }

    [Fact]
    public async Task ADraftsSaveWithFindingsShowsThemUnderTheField()
    {
        var (fake, editor) = await OpenAsync("Why.", draftName: "draft-1");
        fake.SetRationaleHandler = request => CommandOutcome<ProposalTextChangedResponse>.Success(
            new ProposalTextChangedResponse(request.ProposalId, "rationale", "2026-11-11T00:00:00Z", false)
            {
                Findings = [new TextFinding("fwmd.link.not-found", 1, 5, "No object is at '/lexicon/entry-9'.")],
            });

        editor.EditCommand.Execute(null);
        Typed(editor, " More.");
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.False(editor.IsEditing);
        Assert.Equal(["1:5 No object is at '/lexicon/entry-9'."], editor.Findings);
        Assert.True(editor.HasFindings);
    }

    [Fact]
    public async Task ARefusedSaveOnAReadyProposalKeepsThePersonsTextWithTheFindings()
    {
        var (fake, editor) = await OpenAsync("Why it is right.");
        fake.SetRationaleHandler = _ => CommandOutcome<ProposalTextChangedResponse>.Refused(new Refusal(
            RefusalCodes.ProposalRationaleFindings, FailureReason.Refused, "Not saved:\n1:17 fwmd.link.not-found gone",
            new Dictionary<string, string> { ["findingCount"] = "1", ["findings"] = "1:17 fwmd.link.not-found gone" }));

        editor.EditCommand.Execute(null);
        Typed(editor, " [entry-9](/lexicon/entry-9)");
        var typed = editor.Text();
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(editor.IsEditing);
        Assert.Equal(typed, editor.Text());
        Assert.Equal("Why it is right.", editor.Markdown);
        Assert.Equal(["1:17 fwmd.link.not-found gone"], editor.Findings);
        Assert.NotNull(editor.Refusal);
    }

    [Fact]
    public async Task AnEarlierVersionCanBeRestoredIntoTheFieldAndSaved()
    {
        var (fake, editor) = await OpenAsync("Newest.");
        fake.RationaleRevisions =
        [
            new RationaleRevisionView(2, "Newest.", null, "", "2026-11-11T10:00:00.0000000Z"),
            new RationaleRevisionView(1, "First try.", "agent-1", "", "2026-11-10T10:00:00.0000000Z"),
        ];

        editor.EditCommand.Execute(null);
        await editor.Working;
        var earlier = Assert.Single(editor.Revisions);
        Assert.Contains("agent-1", earlier.Label, StringComparison.Ordinal);
        editor.RestoreCommand.Execute(earlier);
        Assert.Equal("First try.", editor.Text());
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("First try.", Assert.Single(fake.SetRationaleRequests).Text);
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("superseded")]
    public async Task ADecidedProposalsRationaleIsFrozen(string status)
    {
        var (_, editor) = await OpenAsync("Why.", status: status);

        Assert.False(editor.CanEdit);
        Assert.False(editor.EditCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARationaleThatDoesNotReadIsShownAndEditedAsPlainText()
    {
        var (fake, editor) = await OpenAsync("Some <b>raw</b> HTML.\nSecond line.");

        Assert.True(editor.IsPlainText);
        Assert.Equal(2, editor.Document.Paragraphs.Count);
        editor.EditCommand.Execute(null);
        Typed(editor, " Fixed.");
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Some <b>raw</b> HTML.\nSecond line. Fixed.", Assert.Single(fake.SetRationaleRequests).Text);
    }

    [Fact]
    public async Task TheFieldIsReadOnlyUntilEditAndEditsTheModelsText()
    {
        var (_, editor) = await OpenAsync("Why.");
        avalonia.Invoke(() =>
        {
            var view = new RationaleEditor { DataContext = editor };
            Assert.True(view.Field!.IsReadOnly);
            editor.EditCommand.Execute(null);
            Assert.False(view.Field!.IsReadOnly);
            Assert.Same(editor.Document, view.Field.Editor.Document);
            editor.CancelCommand.Execute(null);
            Assert.True(view.Field!.IsReadOnly);
        });
    }

    [Theory]
    [InlineData("Glosses **gloss-1** as the corpus does.")]
    [InlineData("Some *x* and **y** and [z](https://example.org).")]
    public async Task AnEditSavedWithNoChangeKeepsTheStoredTextAndWritesNothing(string markdown)
    {
        var (fake, editor) = await OpenAsync(markdown);

        editor.EditCommand.Execute(null);
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Empty(fake.SetRationaleRequests);
        Assert.False(editor.IsEditing);
        Assert.Equal(markdown, editor.Markdown);
    }

    [Theory]
    [InlineData("**Evidence:**\n- a\n- b")]
    [InlineData("1. x\n\ny\n\n2. z")]
    public async Task TextTheCheckerWouldReadDifferentlyIsEditedAsWrittenAndSavedByteForByte(string markdown)
    {
        var (fake, editor) = await OpenAsync(markdown);

        Assert.True(editor.IsPlainText);
        editor.EditCommand.Execute(null);
        Typed(editor, " c");
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(markdown + " c", Assert.Single(fake.SetRationaleRequests).Text);
    }

    [Fact]
    public async Task AnExplicitWritingSystemIsKeptWhenTheRationaleIsSaved()
    {
        var (fake, editor) = await OpenAsync("[x]{ws=en} and y");

        Assert.False(editor.IsPlainText);
        editor.EditCommand.Execute(null);
        Typed(editor, " more");
        await editor.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(fake.SetRationaleRequests).Text;
        Assert.Contains("{ws=en}", saved, StringComparison.Ordinal);
        Assert.EndsWith("and y more", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingOneParagraphKeepsEveryOtherParagraphExactlyAsWritten()
    {
        const string first = "The first paragraph wraps\nonto a second line with **bold** in it.";
        const string second = "The second paragraph has *italic*\nand a [link](https://example.org).";
        const string list = "- item one\n- item two";
        var (fake, editor) = await OpenAsync(first + "\n\n" + second + "\n\n" + list);
        Assert.False(editor.IsPlainText);

        editor.EditCommand.Execute(null);
        var run = editor.Document.Paragraphs[1].Runs[0];
        run.Text = run.Text.Replace("second", "changed", StringComparison.Ordinal);
        await editor.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(fake.SetRationaleRequests).Text;
        Assert.StartsWith(first + "\n\n", saved, StringComparison.Ordinal);
        Assert.EndsWith("\n\n" + list, saved, StringComparison.Ordinal);
        Assert.Contains("The changed paragraph", saved, StringComparison.Ordinal);
        Assert.Equal("Why it is right.", (await OpenAsync("Why it is right.")).Editor.Text());
    }

    [Fact]
    public async Task ASaveOfARationaleChangedElsewhereKeepsThePersonsTextAndCancelShowsTheCurrentOne()
    {
        var (fake, editor) = await OpenAsync("Why.");
        fake.SetRationaleHandler = _ => CommandOutcome<ProposalTextChangedResponse>.Refused(new Refusal(
            RefusalCodes.ProposalRationaleStale, FailureReason.Refused, "Changed meanwhile.",
            new Dictionary<string, string> { ["proposalId"] = Id }));
        fake.ProposalDetail = new ProposalDetailProjection(Id, "proposed", "Label",
            new RationaleView("Theirs.", null, "2026-11-11T00:00:00Z", false, null, false), null, []);

        editor.EditCommand.Execute(null);
        Typed(editor, " Mine.");
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Why.", Assert.Single(fake.SetRationaleRequests).ExpectedRationale);
        Assert.True(editor.IsEditing);
        Assert.True(editor.IsChangedElsewhere);
        Assert.Equal("Why. Mine.", editor.Text());
        editor.CancelCommand.Execute(null);
        await editor.Working;
        Assert.False(editor.IsEditing);
        Assert.Equal("Theirs.", editor.Markdown);
        Assert.Equal("Theirs.", editor.Text());
    }

    [Fact]
    public async Task AProposalWithNoRationaleGetsItsFirstOne()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        var editor = new RationaleEditorModel(context,
            new ProposalDetailProjection(Id, "proposed", "Label", null, null, []) { DraftName = "draft-1" });
        fake.StoredRationale = null;

        editor.EditCommand.Execute(null);
        Typed(editor, "Why it is right.");
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, Assert.Single(fake.SetRationaleRequests).ExpectedRationale);
        Assert.False(editor.IsEditing);
        Assert.False(editor.IsChangedElsewhere);
        Assert.Equal("Why it is right.", fake.StoredRationale);
    }

    [Fact]
    public async Task ASecondEditAfterASaveTheStoreTidiedStartsFromTheStoredText()
    {
        var (fake, editor) = await OpenAsync("Some <b>raw</b> HTML.");
        Assert.True(editor.IsPlainText);

        editor.EditCommand.Execute(null);
        editor.Document.Paragraphs.Add(new Paragraph
        {
            Style = "Normal",
            DefaultWs = editor.Document.Paragraphs[0].DefaultWs,
            Runs = [new Run { Text = string.Empty, Ws = editor.Document.Paragraphs[0].Runs[0].Ws }],
        });
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Some <b>raw</b> HTML.\n", fake.SetRationaleRequests[0].Text);
        Assert.Equal("Some <b>raw</b> HTML.", editor.Markdown);

        editor.EditCommand.Execute(null);
        Typed(editor, " Again.");
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Some <b>raw</b> HTML.", fake.SetRationaleRequests[1].ExpectedRationale);
        Assert.False(editor.IsEditing);
        Assert.False(editor.IsChangedElsewhere);
        Assert.Equal("Some <b>raw</b> HTML. Again.", fake.StoredRationale);
    }

    private static void Typed(RationaleEditorModel editor, string text)
    {
        var runs = editor.Document.Paragraphs[^1].Runs;
        runs[^1].Text += text;
    }

    private static async Task<(FakeCommandClient Fake, RationaleEditorModel Editor)> OpenAsync(string markdown,
        string status = "proposed", string? draftName = null, IUriLauncher? launcher = null)
    {
        var fake = new FakeCommandClient { StoredRationale = markdown };
        var context = WorkspaceContextTests.NewContext(fake, uriLauncher: launcher);
        await context.OpenProjectAsync(ProjectPath);
        var detail = new ProposalDetailProjection(Id, status, "Label",
            new RationaleView(markdown, null, "2026-11-11T00:00:00Z", false, null, false), null, [])
        {
            DraftName = draftName,
        };
        return (fake, new RationaleEditorModel(context, detail));
    }

    private sealed class RecordingLauncher : IUriLauncher
    {
        public List<Uri> Launches { get; } = [];

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launches.Add(uri);
            return Task.FromResult(true);
        }
    }
}
