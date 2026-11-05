using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ProposalTextsTests(PristineProjectFixture pristine)
{
    private const string RationaleEntry = "proposal.rationale";
    private static string Version => MotifProductVersion.CurrentText;

    [Fact]
    public void AfterComposingOnceMoreTheRationaleIsOutOfDateAndCountsTheOperationsSince()
    {
        var (path, id) = Finalized("first");
        Assert.False(Show(path, id).Rationale!.OutOfDate);

        Assert.True(ProposalCommands.Reopen(new ReopenRequest(path, Version, "second", id)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, "second",
            CanonicalId.Mint().Value, "en", "another")).Succeeded);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, Version, "second")).Succeeded);

        var rationale = Show(path, id).Rationale!;
        Assert.True(rationale.OutOfDate);
        Assert.Equal(1, rationale.ChangesSince!.Added);
        Assert.Equal(0, rationale.ChangesSince.Removed);
        Assert.Equal(0, rationale.ChangesSince.Changed);

        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Now both glosses.")).Succeeded);
        var rewritten = Show(path, id).Rationale!;
        Assert.False(rewritten.OutOfDate);
        Assert.Null(rewritten.ChangesSince);
    }

    [Fact]
    public void AnOperationEditedInPlaceCountsAsChangedNotAsNothing()
    {
        var (path, id) = Finalized("edited");
        var draftId = Show(path, id).Operations.Single().Target!;

        Assert.True(ProposalCommands.Reopen(new ReopenRequest(path, Version, "edit-again", id)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, "edit-again",
            draftId, "en", "a different gloss")).Succeeded);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, Version, "edit-again")).Succeeded);

        var rationale = Show(path, id).Rationale!;
        Assert.True(rationale.OutOfDate);
        Assert.Equal(0, rationale.ChangesSince!.Added);
        Assert.Equal(0, rationale.ChangesSince.Removed);
        Assert.Equal(1, rationale.ChangesSince.Changed);
    }

    [Fact]
    public void TheRationaleIsNewUntilThePersonLooksAndNewAgainAfterARewrite()
    {
        var (path, id) = Finalized("newness");
        Assert.True(Show(path, id).Rationale!.New);
        Assert.True(Standing(path, id).RationaleNew);

        Assert.True(ReviewMarks.Set(new ReviewMarkRequest(path, id, RationaleEntry, "", "", true)).Succeeded);
        Assert.False(Show(path, id).Rationale!.New);
        Assert.False(Standing(path, id).RationaleNew);

        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "A different account.")).Succeeded);
        Assert.True(Show(path, id).Rationale!.New);
    }

    [Fact]
    public void ARationaleMayBeRewrittenOnAReadyProposalButNotAfterItIsClosed()
    {
        var (path, id) = Finalized("closing");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Rewritten while ready.")).Succeeded);
        Assert.Equal("Rewritten while ready.", Show(path, id).Rationale!.Markdown);

        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);

        var rationale = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Too late."));
        var note = ProposalComments.Add(new AddCommentRequest(path, Version, id, "Too late.", "John"));
        Assert.Equal("proposal.closed", rationale.Refusal?.Code);
        Assert.Equal("proposal.closed", note.Refusal?.Code);
        Assert.Equal("Rewritten while ready.", Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void TheCommentsChangedFlagIsTrueAfterThePersonsCommentAndFalseAfterTheNextRationale()
    {
        var (path, id) = Finalized("noted");
        Assert.False(Show(path, id).CommentsChanged);

        var noted = ProposalComments.Add(new AddCommentRequest(path, Version, id, "Please reconsider the second gloss.", "John"));
        Assert.True(noted.Succeeded, noted.Refusal?.Message);
        Assert.True(noted.Value!.CommentsChanged);
        var shown = Show(path, id);
        Assert.True(shown.CommentsChanged);
        Assert.Equal("Please reconsider the second gloss.", Assert.Single(shown.Comments!).Markdown);
        Assert.True(Standing(path, id).HasNote);

        var answered = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Reconsidered."));
        Assert.False(answered.Value!.CommentsChanged);
        Assert.False(Show(path, id).CommentsChanged);

        Assert.True(ProposalComments.Delete(new DeleteCommentRequest(path, Version, id, noted.Value.CommentId)).Succeeded);
        Assert.Empty(Show(path, id).Comments!);
    }

    [Fact]
    public void AnAgentsCommentIsAttributedAndDoesNotRaiseTheNoteFlag()
    {
        var (path, id) = Finalized("agent");

        var added = ProposalComments.Add(new AddCommentRequest(path, Version, id, "Checked the gloss.", "John",
            "Claude (Opus 5.5)"));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.False(added.Value!.CommentsChanged);
        var comment = Assert.Single(Show(path, id).Comments!);
        Assert.Equal("Claude (Opus 5.5)", comment.AuthorLabel);
        Assert.Equal("John", comment.PersonName);
        Assert.Equal(added.Value.CommentId, comment.CommentId);
        Assert.False(Show(path, id).CommentsChanged);
        Assert.Contains("Claude (Opus 5.5) for John", SIL.Motif.Projection.Rendering.CommandTextRenderer.Render(Show(path, id)));
    }

    [Fact]
    public void APersonEditsAndDeletesAnyCommentAndAnEditShowsTheTimeWithoutHistory()
    {
        var (path, id) = Finalized("editing");
        var agent = ProposalComments.Add(new AddCommentRequest(path, Version, id, "Agent says.", "John", "Claude")).Value!;

        var edited = ProposalComments.Edit(new EditCommentRequest(path, Version, id, agent.CommentId, "Person fixed it."));

        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        var comment = Assert.Single(Show(path, id).Comments!);
        Assert.Equal("Person fixed it.", comment.Markdown);
        Assert.NotNull(comment.EditedUtc);
        Assert.Equal("Claude", comment.AuthorLabel);

        Assert.True(ProposalComments.Delete(new DeleteCommentRequest(path, Version, id, agent.CommentId)).Succeeded);
        Assert.Empty(Show(path, id).Comments!);
        Assert.Equal("proposal.not-found",
            ProposalComments.Delete(new DeleteCommentRequest(path, Version, id, agent.CommentId)).Refusal?.Code);
    }

    [Fact]
    public void ACommentWithNoTextOrAnOverlongLabelIsRefused()
    {
        var (path, id) = Finalized("limits-comment");
        var tooLong = new string('x', ProposalTexts.MaxAuthorLength + 1);

        Assert.Equal("proposal.comment-invalid",
            ProposalComments.Add(new AddCommentRequest(path, Version, id, " ", "John")).Refusal?.Code);
        Assert.Equal("proposal.comment-invalid",
            ProposalComments.Add(new AddCommentRequest(path, Version, id, "Text", "John", tooLong)).Refusal?.Code);
        Assert.Equal("proposal.comment-invalid",
            ProposalComments.Add(new AddCommentRequest(path, Version, id, "Text", tooLong)).Refusal?.Code);
        Assert.Equal("proposal.comment-invalid",
            ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Text", tooLong)).Refusal?.Code);
        Assert.True(ProposalComments.Add(new AddCommentRequest(path, Version, id, "Text", "John",
            new string('x', ProposalTexts.MaxAuthorLength))).Succeeded);
    }

    [Fact]
    public void RationaleHistoryGrowsWhileOpenAndIsDroppedAtEveryKindOfDecision()
    {
        foreach (var decision in new[] { "withdraw", "decline", "supersede" })
        {
            var (path, id) = Finalized("history-" + decision);
            Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Second.", "Claude", "John")).Succeeded);
            Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Third.")).Succeeded);
            Assert.Equal(new[] { "Second.", "Third." }, Revisions(path, id).Select(r => r.Text).Skip(1));
            Assert.Equal(3, Revisions(path, id).Count);
            Assert.Equal("Claude", Revisions(path, id)[1].Author.Label);

            Assert.True(Decide(path, id, decision), decision);

            Assert.Empty(Revisions(path, id));
            Assert.Equal("Third.", Show(path, id).Rationale!.Markdown);
        }
    }

    [Fact]
    public void CommentsFreezeAtTheDecisionAndThawWhenTheProposalIsReopened()
    {
        var (path, id) = Finalized("freeze");
        var comment = ProposalComments.Add(new AddCommentRequest(path, Version, id, "Open.", "John")).Value!.CommentId;
        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);

        Assert.Equal("proposal.closed", ProposalComments.Add(new AddCommentRequest(path, Version, id, "Late.", "John")).Refusal?.Code);
        Assert.Equal("proposal.closed", ProposalComments.Edit(new EditCommentRequest(path, Version, id, comment, "Late.")).Refusal?.Code);
        Assert.Equal("proposal.closed", ProposalComments.Delete(new DeleteCommentRequest(path, Version, id, comment)).Refusal?.Code);
        Assert.Equal("Open.", Assert.Single(Show(path, id).Comments!).Markdown);

        Assert.True(ProposalCommands.Reopen(new ReopenRequest(path, Version, "thaw", id)).Succeeded);
        Assert.True(ProposalComments.Edit(new EditCommentRequest(path, Version, id, comment, "Thawed.")).Succeeded);
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "New history.")).Succeeded);
        Assert.Equal(["New history."], Revisions(path, id).Select(r => r.Text));
    }

    [Fact]
    public void DeletingAnUnappliedProposalRemovesItsRevisionsAndComments()
    {
        var (path, id) = Finalized("deleting");
        Assert.True(ProposalComments.Add(new AddCommentRequest(path, Version, id, "Gone soon.", "John")).Succeeded);
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Revised.")).Succeeded);
        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);

        var deleted = ProposalLifecycleCommand.Delete(new DeleteProposalRequest(path, id));

        Assert.True(deleted.Succeeded, deleted.Refusal?.Message);
        foreach (var table in new[] { "ProposalRationaleRevisions", "ProposalComments", "ProposalPictures" })
            Assert.Equal(0L, Count(path, table));
    }

    [Fact]
    public void AnEmptyRationaleIsRefusedAndAPrefixIsNotAProposal()
    {
        var (path, id) = Finalized("limits");

        var empty = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "  "));
        var prefix = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id[..12], "A prefix."));

        Assert.Equal("proposal.rationale-empty", empty.Refusal?.Code);
        Assert.Equal("proposal.not-found", prefix.Refusal?.Code);
    }

    [Fact]
    public void AgentsMayAddCommentsButEditAndDeleteAreHumanOnlyAndNoNoteCommandRemains()
    {
        var add = CommandCatalog.All.Single(command => command.Name == "proposal comment add");
        var edit = CommandCatalog.All.Single(command => command.Name == "proposal comment edit");
        var delete = CommandCatalog.All.Single(command => command.Name == "proposal comment delete");
        var rationale = CommandCatalog.All.Single(command => command.Name == "proposal rationale");

        Assert.Equal(AgentClass.Draft, add.Agent);
        Assert.Equal(AgentClass.HumanOnly, edit.Agent);
        Assert.Equal(AgentClass.HumanOnly, delete.Agent);
        Assert.All(new[] { add, edit, delete, rationale }, command => Assert.Equal(CommandSurface.Released, command.Surface));
        Assert.Equal(AgentClass.Draft, rationale.Agent);
        Assert.DoesNotContain(CommandCatalog.All, command => command.Name is "proposal note" or "comment");
    }

    private bool Decide(string path, string id, string decision) => decision switch
    {
        "withdraw" => ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded,
        "decline" => ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id)).Succeeded,
        _ => Supersede(path, id),
    };

    private bool Supersede(string path, string id)
    {
        var other = Finalized("replacement", path).Id;
        return ProposalCommands.Supersede(new SupersedeRequest(path, Version, id, other)).Succeeded;
    }

    private static IReadOnlyList<RationaleRevisionRecord> Revisions(string path, string id) =>
        ProjectStoreCommand.Run(path, Version, (database, _) => CommandOutcome<IReadOnlyList<RationaleRevisionRecord>>.Success(
            new ProposalDiscussionRepository(database).ListRationaleRevisions(CanonicalId.Parse(id)))).Value!;

    private static long Count(string path, string table) =>
        ProjectStoreCommand.Run(path, Version, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return CommandOutcome<string>.Success(Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!);
        }).Value is { } count ? long.Parse(count, System.Globalization.CultureInfo.InvariantCulture) : -1;

    private string NewProject()
    {
        using var scratch = pristine.NewScratch();
        return scratch.ProjectId.Path;
    }

    private (string Path, string Id) Finalized(string name, string? existing = null)
    {
        var path = existing ?? NewProject();
        var created = ProposalCommands.New(new NewDraftRequest(path, Version, name, name));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, name,
            CanonicalId.Mint().Value, "en", name)).Succeeded);
        DraftRationale.Author(path, name, name, "Why " + name + " is right.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return (path, created.Value!.ProposalId);
    }

    private static SIL.Motif.Contract.Responses.ProposalDetailProjection Show(string path, string id)
    {
        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private static SIL.Motif.Contract.Responses.ProposalCard Standing(string path, string id)
    {
        var standings = SIL.Motif.Commands.Catalog.ProposalStandingsCommand.Read(
            new SIL.Motif.Contract.Requests.ProposalStandingsRequest(path, true));
        Assert.True(standings.Succeeded, standings.Refusal?.Message);
        return standings.Value!.Proposals.Single(card => card.ProposalId == id);
    }
}
