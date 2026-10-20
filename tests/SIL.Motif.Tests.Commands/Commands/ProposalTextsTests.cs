using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
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
        var note = ProposalTexts.SetNote(new SetNoteRequest(path, Version, id, "Too late."));
        Assert.Equal("proposal.closed", rationale.Refusal?.Code);
        Assert.Equal("proposal.closed", note.Refusal?.Code);
        Assert.Equal("Rewritten while ready.", Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void TheNoteChangedFlagIsTrueAfterTheNoteAndFalseAfterTheNextRationale()
    {
        var (path, id) = Finalized("noted");
        Assert.False(Show(path, id).NoteChanged);

        var noted = ProposalTexts.SetNote(new SetNoteRequest(path, Version, id, "Please reconsider the second gloss."));
        Assert.True(noted.Succeeded, noted.Refusal?.Message);
        Assert.True(noted.Value!.NoteChanged);
        var shown = Show(path, id);
        Assert.True(shown.NoteChanged);
        Assert.Equal("Please reconsider the second gloss.", shown.Note!.Markdown);
        Assert.True(Standing(path, id).HasNote);

        var answered = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Reconsidered."));
        Assert.False(answered.Value!.NoteChanged);
        Assert.False(Show(path, id).NoteChanged);

        Assert.True(ProposalTexts.SetNote(new SetNoteRequest(path, Version, id, null)).Succeeded);
        Assert.Null(Show(path, id).Note);
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
    public void ProposalNoteIsAHumanOnlyCommandAndRationaleIsAReleasedDraftCommand()
    {
        var note = CommandCatalog.All.Single(command => command.Name == "proposal note");
        var rationale = CommandCatalog.All.Single(command => command.Name == "proposal rationale");

        Assert.Equal(AgentClass.HumanOnly, note.Agent);
        Assert.Equal(CommandSurface.Released, note.Surface);
        Assert.Equal(AgentClass.Draft, rationale.Agent);
        Assert.Equal(CommandSurface.Released, rationale.Surface);
        Assert.DoesNotContain(CommandCatalog.All, command => command.Name == "comment");
    }

    private string NewProject()
    {
        using var scratch = pristine.NewScratch();
        return scratch.ProjectId.Path;
    }

    private (string Path, string Id) Finalized(string name)
    {
        var path = NewProject();
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
