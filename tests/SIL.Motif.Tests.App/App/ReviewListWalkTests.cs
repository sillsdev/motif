using SIL.Motif.Commands.Queries;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewListWalkTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string ProposalId = "proposal/one";

    private static readonly BaselineToken Token = new("project-id", "sha256:" + new string('1', 64),
        "projection-v1", "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));

    [Fact]
    public async Task NextMovesAndMarksTheEntryItLeftLookedAt()
    {
        var (fake, _, page) = await OpenWith(Rule("a"), Rule("b"), Rule("c"));

        await page.NextCommand.ExecuteAsync(null);

        Assert.Equal("b", page.Current!.Key);
        Assert.Equal(["a"], fake.ReviewMarkRequests.Where(mark => mark.LookedAt).Select(mark => mark.EntryKey));
        Assert.True(page.Entries[0].IsLookedAt);
        Assert.False(page.Entries[1].IsLookedAt);
    }

    [Fact]
    public async Task PreviousMarksNothing()
    {
        var (fake, _, page) = await OpenWith(Rule("a"), Rule("b"));
        await page.NextCommand.ExecuteAsync(null);
        fake.ReviewMarkRequests.Clear();

        page.PreviousCommand.Execute(null);

        Assert.Equal("a", page.Current!.Key);
        Assert.Empty(fake.ReviewMarkRequests);
    }

    [Fact]
    public async Task KeepMarksAndMovesToTheNextEntryWithoutChangingTheProposal()
    {
        var (fake, _, page) = await OpenWith(Rule("a"), Rule("b"));
        var revision = fake.Review.Revision;

        await page.KeepCommand.ExecuteAsync(null);

        Assert.Equal("b", page.Current!.Key);
        Assert.True(page.Entries[0].IsLookedAt);
        Assert.Equal(revision, fake.Review.Revision);
        Assert.Empty(fake.RemoveOperationsRequests);
    }

    [Fact]
    public async Task LookedAtTextCounts()
    {
        var (_, _, page) = await OpenWith(Rule("a"), Rule("b"), Rule("c"), Rule("d"));
        Assert.Equal("0 of 4 looked at", page.LookedAtText);

        await page.KeepCommand.ExecuteAsync(null);
        await page.KeepCommand.ExecuteAsync(null);

        Assert.Equal("2 of 4 looked at", page.LookedAtText);
    }

    [Fact]
    public async Task KeepAllMarksEveryEntry()
    {
        var (_, _, page) = await OpenWith(Rule("a"), Rule("b"));

        await page.KeepAllCommand.ExecuteAsync(null);

        Assert.Equal("2 of 2 looked at", page.LookedAtText);
    }

    [Fact]
    public async Task ApplyNeverRequiresTheTally()
    {
        var (_, _, page) = await OpenWith(Rule("a"), Rule("b"));

        Assert.Equal("0 of 2 looked at", page.LookedAtText);
        Assert.True(page.CanApply, page.ApplyBlockReason);
    }

    [Fact]
    public async Task UndoOnAFinalizedProposalReopensItFirstAndNeedsAnotherTry()
    {
        var (fake, context, page) = await OpenWith(Rule("a"), Rule("b"));
        fake.ReadTrialCompletesWith(Trial(TrialFreshness.ProposalChanged));
        fake.ReadTrialRequests.Clear();

        await page.UndoCommand.ExecuteAsync(null);

        var reopen = Assert.Single(fake.ReopenRequests);
        Assert.Equal("review/" + ProposalId, reopen.DraftName);
        var removal = Assert.Single(fake.RemoveOperationsRequests);
        Assert.Equal("review/" + ProposalId, removal.DraftName);
        Assert.Equal(["op-a"], removal.OperationIds);
        Assert.Equal(["b"], page.Entries.Select(entry => entry.Key));
        Assert.NotEmpty(fake.ReadTrialRequests);
        Assert.False(page.CanApply);
        Assert.Contains(page.ApplyBlockers, blocker => blocker.Kind == ApplyBlockerKind.TrialNeeded);
        var blocker = page.ApplyBlockers.Single(blocker => blocker.Kind == ApplyBlockerKind.TrialNeeded);
        Assert.Equal(TrialStandingWords.ProposalChanged.Sentence, blocker.Sentence);
        Assert.Equal("Try again", blocker.ActionText);
        Assert.Equal(TrialFreshness.ProposalChanged, context.Lens!.Trial!.Freshness);
    }

    [Fact]
    public async Task UndoRemovesTheEntryAndClearsItsMark()
    {
        var (fake, _, page) = await OpenWith(Rule("a"), Rule("b"));
        await page.KeepCommand.ExecuteAsync(null);
        page.PreviousCommand.Execute(null);
        fake.ReviewMarkRequests.Clear();

        await page.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["a"], fake.ReviewMarkRequests.Where(mark => !mark.LookedAt).Select(mark => mark.EntryKey));
    }

    [Fact]
    public async Task TheTallyDropsAnUndoneEntryFromTheDenominator()
    {
        var (_, _, page) = await OpenWith(Rule("a"), Rule("b"), Rule("c"));

        await page.UndoCommand.ExecuteAsync(null);

        Assert.Equal("0 of 2 looked at", page.LookedAtText);
    }

    [Fact]
    public async Task UndoAllMarksNothingAndEmptiesTheList()
    {
        var (fake, _, page) = await OpenWith(Rule("a"), Rule("b"));

        await page.UndoAllCommand.ExecuteAsync(null);

        Assert.Empty(page.Entries);
        Assert.DoesNotContain(fake.ReviewMarkRequests, mark => mark.LookedAt);
        var removal = Assert.Single(fake.RemoveOperationsRequests);
        Assert.Equal(["op-b", "op-a"], removal.OperationIds);
    }

    [Fact]
    public async Task UndoOfARuleWithDependentsPromptsWithTheirTitles()
    {
        var (fake, _, page) = await OpenWith(Rule("rule"), Rule("retarget", dependsOn: "rule"), Rule("other"));

        await page.UndoCommand.ExecuteAsync(null);

        Assert.Empty(fake.RemoveOperationsRequests);
        var prompt = page.UndoPrompt!;
        Assert.Equal(["Title retarget"], prompt.Titles);
        Assert.Equal("Undoing “Title rule” also undoes “Title retarget”. Try the rest again before Apply.",
            prompt.Sentence);
        Assert.Equal("Undo both", prompt.UndoAllText);

        await prompt.UndoAllOfThem.ExecuteAsync(null);

        Assert.Equal(["other"], page.Entries.Select(entry => entry.Key));
        Assert.Null(page.UndoPrompt);
        Assert.Equal(["op-retarget", "op-rule"], Assert.Single(fake.RemoveOperationsRequests).OperationIds.Order());
    }

    [Fact]
    public async Task CancellingTheUndoPromptChangesNothing()
    {
        var (fake, _, page) = await OpenWith(Rule("rule"), Rule("retarget", dependsOn: "rule"));
        await page.UndoCommand.ExecuteAsync(null);

        page.UndoPrompt!.Cancel.Execute(null);

        Assert.Null(page.UndoPrompt);
        Assert.Empty(fake.RemoveOperationsRequests);
        Assert.Equal(2, page.Entries.Count);
    }

    [Fact]
    public async Task UndoOfTheLastEntryLeavesAnEmptyListAndBlocksApply()
    {
        var (fake, _, page) = await OpenWith(Rule("only"));
        fake.ReadTrialCompletesWith(Trial(TrialFreshness.ProposalChanged));

        await page.UndoCommand.ExecuteAsync(null);

        Assert.Empty(page.Entries);
        Assert.False(page.CanApply);
        Assert.Contains(page.ApplyBlockers, blocker => blocker.Sentence == "Nothing to apply.");
    }

    [Fact]
    public async Task OutsideProposalModeTheListIsThePendingDraft()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);

        await context.OpenProjectAsync(ProjectPath);
        await page.LoadReviewAsync();

        Assert.Null(fake.ProposalReviewRequests.Last().ProposalId);
        Assert.False(page.InProposalMode);
    }

    [Fact]
    public async Task ApplyCallsApplyProposalForTheOpenProposal()
    {
        var (fake, context, page) = await OpenWith(Rule("a"));
        var applied = new ApplyProjection(ProposalId, false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary(ProposalId, "2026-01-01", "Motif", "sha256:intent"));
        fake.ApplyProposalHandler = (_, _) =>
            Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(applied, "ok")));

        await page.ApplyCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.ApplyProposalRequests);
        Assert.Equal(ProposalId, request.ProposalId);
        Assert.Equal(fake.Review.Revision, request.ExpectedRevision);
        Assert.Empty(fake.ApplyPendingRequests);
        Assert.Null(context.Lens);
        Assert.NotNull(page.Receipt);
    }

    [Fact]
    public async Task TheApplyCardShowsForGrammarEntriesWithNoWordChanges()
    {
        var (_, _, page) = await OpenWith(Rule("a"));

        Assert.True(page.ShowsSideCards);
        Assert.False(page.HasWordRows);
    }

    [Fact]
    public async Task TheApplyCardShowsNothingToApplyForAnEmptyProposal()
    {
        var (_, _, page) = await OpenWith();

        Assert.True(page.ShowsSideCards);
        Assert.Contains(page.ApplyBlockers, blocker => blocker.Kind == ApplyBlockerKind.NothingToApply);
    }

    [Fact]
    public async Task AnotherProposalIsNotBlockedByTheUnfitChangesOfThePendingDraft()
    {
        var stale = WordChange("c1");
        var (_, _, page) = await OpenWith([Rule("a")], proposalWords: [],
            pendingWords: [(stale, new ChangeFit("c1", false, ["FieldWorks changed the word."]))]);

        Assert.True(page.CanApply, page.ApplyBlockReason);
        Assert.DoesNotContain(page.ApplyBlockers, blocker => blocker.Kind == ApplyBlockerKind.NoLongerFits);
    }

    [Fact]
    public async Task TheOpenProposalsOwnUnfitChangeBlocksApply()
    {
        var unfit = WordChange("w1");
        var (_, _, page) = await OpenWith([Rule("a"), WordEntry(unfit)],
            proposalWords: [(unfit, new ChangeFit("w1", false, ["gone"]))]);

        Assert.False(page.CanApply);
        Assert.Contains(page.ApplyBlockers, blocker => blocker.Kind == ApplyBlockerKind.NoLongerFits);
    }

    [Fact]
    public async Task TheListReadsTheDraftChangesHoldsAndBuildsOneEntryPerChange()
    {
        var word = WordChange("w1");
        var (fake, context, page) = await OpenWith([Rule("a"), WordEntry(word)],
            proposalWords: [(word, new ChangeFit("w1", true, []))]);

        Assert.Equal(ProposalId, context.Changes.TargetProposalId);
        Assert.Equal(ProposalId, fake.ProposalReviewRequests.Last().ProposalId);
        Assert.Equal(["a", "w1"], page.Entries.Select(entry => entry.Key));
        Assert.Same(context.Changes.Items.Single(), page.Entries[1].Change);
    }

    [Fact]
    public async Task AWordChangeMadeSinceTheListWasReadIsStillAnEntry()
    {
        var word = WordChange("w1");
        var (_, _, page) = await OpenWith([Rule("a")], proposalWords: [(word, new ChangeFit("w1", true, []))]);

        var entry = page.Entries.Single(entry => entry.Change is not null);
        Assert.Null(entry.Entry);
        Assert.Equal("w1", entry.Key);
        Assert.False(entry.KeepCommand.CanExecute(null));
        Assert.Equal("Sound change · 2 changes", page.CountText);
    }

    [Fact]
    public async Task TheHeadingTheApplyCardAndTheTallyCountTheSameEntries()
    {
        var first = WordChange("w1");
        var second = WordChange("w2");
        var (_, _, page) = await OpenWith([Rule("a"), Rule("b"), WordEntry(first), WordEntry(second)],
            proposalWords: [(first, new ChangeFit("w1", true, [])), (second, new ChangeFit("w2", true, []))]);

        Assert.Equal("Sound change · 4 changes", page.CountText);
        Assert.Equal("0 of 4 looked at", page.LookedAtText);
        Assert.Equal("4", page.Badge);
        Assert.Equal("Keep all 4", page.KeepAllText);
    }

    [Fact]
    public async Task TheHeadingNamesTheProposalBeingReviewed()
    {
        var (_, _, page) = await OpenWith(Rule("a"));

        Assert.Equal("Sound change", page.ProposalLabel);
        Assert.StartsWith("Sound change · ", page.CountText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePendingChangesKeepTheirHeadingAndCountGrammarEntriesToo()
    {
        var word = WordChange("w1");
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/pending", "revision/one", [word],
            [new ChangeFit("w1", true, [])]));
        fake.Review = Review([Rule("a"), WordEntry(word)]) with { DraftName = "pending-changes", IsPendingChanges = true };
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.LoadReviewAsync();

        Assert.False(page.InProposalMode);
        Assert.Equal("2 changes not applied yet", page.CountText);
        Assert.Equal("0 of 2 looked at", page.LookedAtText);
        Assert.True(page.ShowsUndoAll);
        Assert.True(page.ShowsBackButton);
        Assert.Equal("Back to Analyze texts", page.BackText);
        Assert.True(page.ShowsNumbersText);
        Assert.Empty(page.ApplyChecks);
    }

    [Fact]
    public async Task UndoAllOfThePendingChangesDiscardsTheirDraftAtTheRevisionShown()
    {
        var word = WordChange("w1");
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/pending", "revision/one", [word],
            [new ChangeFit("w1", true, [])]));
        fake.Review = Review([Rule("a"), WordEntry(word)]) with
        {
            DraftName = "pending-changes", IsPendingChanges = true,
        };
        fake.OnDraftDiscarded = _ =>
        {
            fake.PendingChangesIs(new PendingChangesSnapshot(null, "none", [], []));
            fake.Review = Review([]);
        };
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.LoadReviewAsync();

        await page.UndoAllCommand.ExecuteAsync(null);

        var discard = Assert.Single(fake.DiscardDraftRequests);
        Assert.Equal("pending-changes", discard.DraftName);
        Assert.Equal("revision/one", discard.ExpectedRevision);
        Assert.Empty(fake.RemoveOperationsRequests);
        Assert.Empty(context.Changes.Snapshot.Changes);
    }

    [Fact]
    public async Task DeclineIsOfferedOnAnOpenProposalAndLeavesIt()
    {
        var (fake, context, page) = await OpenWith(Rule("a"));
        Assert.True(page.ShowsDecline);

        await page.DeclineCommand.ExecuteAsync(null);

        Assert.Equal(ProposalId, Assert.Single(fake.DeclineProposalRequests).ProposalId);
        Assert.Null(context.Lens);
        Assert.False(page.InProposalMode);
    }

    [Fact]
    public async Task ThePendingChangesAreNeverOfferedDecline()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/pending", "revision/one", [], []));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.LoadReviewAsync();

        Assert.False(page.ShowsDecline);
    }

    [Fact]
    public async Task ThePendingChangesShowNoCurrentEntryUntilThePersonWalksThem()
    {
        var first = WordChange("w1");
        var second = WordChange("w2");
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/pending", "revision/one", [first, second],
            [new ChangeFit("w1", true, []), new ChangeFit("w2", true, [])]));
        fake.Review = Review([WordEntry(first), WordEntry(second)]) with { DraftName = "pending-changes", IsPendingChanges = true };
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.LoadReviewAsync();

        Assert.DoesNotContain(page.Entries, entry => entry.IsCurrent);

        await page.NextCommand.ExecuteAsync(null);

        Assert.Equal(["w2"], page.Entries.Where(entry => entry.IsCurrent).Select(entry => entry.Key));
    }

    [Fact]
    public async Task AnOpenProposalShowsItsFirstEntryAsCurrent()
    {
        var (_, _, page) = await OpenWith(Rule("a"), Rule("b"));

        Assert.Equal(["a"], page.Entries.Where(entry => entry.IsCurrent).Select(entry => entry.Key));
    }

    [Fact]
    public async Task AProposalOffersNoUndoAllNoWayBackAndNoNumbersCheck()
    {
        var word = WordChange("w1");
        var (_, _, page) = await OpenWith([Rule("a"), WordEntry(word)],
            proposalWords: [(word, new ChangeFit("w1", true, []))]);

        Assert.False(page.ShowsUndoAll);
        Assert.False(page.ShowsBackButton);
        Assert.False(page.ShowsNumbersText);
    }

    [Fact]
    public async Task TheApplyCardListsWhatApplyChecksWithEachResult()
    {
        var unfit = WordChange("w1");
        var (fake, _, page) = await OpenWith([Rule("a"), WordEntry(unfit)],
            proposalWords: [(unfit, new ChangeFit("w1", false, ["gone"]))]);
        fake.Review = fake.Review with
        {
            Readiness = new ReadinessProjection(true, false, true, null, [], false),
        };
        await page.LoadReviewAsync();

        Assert.Equal(["✓ Tried as it is now", "✗ A change needs a look", "✓ Ready to apply"],
            page.ApplyChecks.Select(check => check.MarkedText));
    }

    [Fact]
    public async Task AnUntriedProposalIsBlockedWithTheStandingsWordsAndTryIt()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot(null, "none", [], []));
        fake.ReadTrialCompletesWith(null);
        fake.Review = Review([Rule("a")]);
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await context.OpenProposalAsync(ProposalId, CancellationToken.None);
        await page.LoadReviewAsync();

        var blocker = page.ApplyBlockers.Single(blocker => blocker.Kind == ApplyBlockerKind.TrialNeeded);
        Assert.Equal(TrialStandingWords.NotTried.Sentence, blocker.Sentence);
        Assert.Equal("Try it", blocker.ActionText);
        Assert.Equal("Try this Proposal", blocker.ActionAccessibleName);
        Assert.Contains(page.ApplyChecks, check => check is { Text: "Not tried yet", Passed: false });
    }

    [Fact]
    public async Task TryAgainGoesThroughTheWindowsOneTryAgainAndShowsARefusalWithoutThrowing()
    {
        var (fake, _, page) = await OpenWith(Rule("a"));
        fake.EnqueueTrialRefusesWith(new Refusal("trial.queue-busy", FailureReason.Refused,
            "The Trial could not be queued."));

        await page.TryAgainCommand.ExecuteAsync(null);

        Assert.Equal(ProposalId, Assert.Single(fake.EnqueueTrialRequests).ProposalId);
        Assert.Equal("trial.queue-busy", page.MeasurementRefusal?.Code);
    }

    [Fact]
    public async Task UndoOfThreeNamesThemAllAndOffersToUndoTheseThree()
    {
        var (_, _, page) = await OpenWith(Rule("rule"), Rule("retarget", dependsOn: "rule"),
            Rule("again", dependsOn: "retarget"));

        await page.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Undo these 3", page.UndoPrompt!.UndoAllText);
        Assert.Equal("Undoing “Title rule” also undoes “Title retarget”, “Title again”. " +
            "Try the rest again before Apply.", page.UndoPrompt.Sentence);
    }

    [Fact]
    public async Task KeepingAWordEntryShowsLookedAtOnItsRow()
    {
        var word = WordChange("w1");
        var (_, _, page) = await OpenWith([WordEntry(word)], proposalWords: [(word, new ChangeFit("w1", true, []))]);
        var entry = page.Entries.Single();
        Assert.False(entry.Change!.IsLookedAt);

        await entry.KeepCommand.ExecuteAsync(null);

        Assert.True(entry.Change.IsLookedAt);
        Assert.Equal("Keep: w1", entry.Change.Presentation.PendingChange!.KeepAutomationName);
    }

    [Fact]
    public async Task AWordEntrysUndoInAProposalGoesThroughStagingIntoTheProposal()
    {
        var word = WordChange("w1");
        var (fake, _, page) = await OpenWith([Rule("a"), WordEntry(word)],
            proposalWords: [(word, new ChangeFit("w1", true, []))]);
        await page.KeepAllCommand.ExecuteAsync(null);
        fake.ReviewMarkRequests.Clear();
        fake.ReadTrialRequests.Clear();

        await page.Entries.Single(entry => entry.Change is not null).UndoCommand.ExecuteAsync(null);

        var removal = Assert.Single(fake.PendingRemoveRequests);
        Assert.Equal(ProposalId, removal.ProposalId);
        Assert.Empty(fake.RemoveOperationsRequests);
        Assert.Equal(["w1"], fake.ReviewMarkRequests.Where(mark => !mark.LookedAt).Select(mark => mark.EntryKey));
        Assert.NotEmpty(fake.ReadTrialRequests);
    }

    [Fact]
    public async Task TheGrammarChangesHaveTheirOwnBandLikeAWordGroup()
    {
        var (_, _, page) = await OpenWith(Rule("a"), Rule("b"));

        var band = Assert.Single(page.ReviewEntries, entry => entry.GrammarGroup is not null).GrammarGroup!;
        Assert.Equal("Grammar changes", band.Title);
        Assert.Equal("· 2 changes", band.CountText);
        Assert.Equal(2, page.ReviewEntries.Count(entry => entry.IsGrammarRow));
    }

    [Fact]
    public async Task ReadinessReasonsAreTheirOwnBlocker()
    {
        var (fake, _, page) = await OpenWith(Rule("a"));
        fake.Review = fake.Review with
        {
            Readiness = new ReadinessProjection(false, true, false, null, ["A word lost its analysis."], false),
        };

        await page.LoadReviewAsync();

        Assert.Contains(page.ApplyBlockers, blocker => blocker.Kind == ApplyBlockerKind.NotReady);
    }

    [Fact]
    public async Task ARefusedUndoOfAFinalizedProposalDoesNotReopenItAgainOnTheNextUndo()
    {
        var (fake, _, page) = await OpenWith(Rule("a"), Rule("b"));
        fake.RemoveOperationsRefusal = new Refusal("operation.cascading-delete", FailureReason.Refused,
            "Cannot remove: --force cannot help.", new Dictionary<string, string>());

        await page.UndoCommand.ExecuteAsync(null);

        Assert.Equal("This change deletes something that owns other things, so it cannot be undone from here.",
            page.UndoRefusal!.Sentence);
        fake.RemoveOperationsRefusal = null;
        await page.UndoCommand.ExecuteAsync(null);

        Assert.Single(fake.ReopenRequests);
        Assert.Equal(2, fake.RemoveOperationsRequests.Count);
        Assert.Null(page.UndoRefusal);
        Assert.Equal(["b"], page.Entries.Select(entry => entry.Key));
    }

    private static Task<(FakeCommandClient Fake, WorkspaceContext Context, ReviewPageModel Page)> OpenWith(
        params ReviewEntry[] entries) => OpenWith(entries, proposalWords: []);

    // Changes holds the open Proposal's word changes, and the pending Draft's only outside Proposal mode.
    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, ReviewPageModel Page)> OpenWith(
        ReviewEntry[] entries, (PendingChange Change, ChangeFit Fit)[] proposalWords,
        (PendingChange Change, ChangeFit Fit)[]? pendingWords = null)
    {
        var fake = new FakeCommandClient();
        var proposal = Snapshot(ProposalId, proposalWords);
        var pending = Snapshot(pendingWords is { Length: > 0 } ? "draft/pending" : null, pendingWords ?? []);
        fake.PendingLoadHandler = (request, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
            request.ProposalId == ProposalId ? proposal : pending));
        fake.PendingRemoveHandler = (request, _) =>
        {
            proposal = proposal with
            {
                Revision = "revision/after-undo",
                Changes = proposal.Changes.Where(change => change.ChangeId != request.ChangeId).ToArray(),
                FitSummary = proposal.FitSummary.Where(fit => fit.ChangeId != request.ChangeId).ToArray(),
            };
            return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(proposal));
        };
        fake.ReadTrialCompletesWith(Trial(TrialFreshness.Current));
        fake.Review = Review(entries);
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await context.OpenProposalAsync(ProposalId, CancellationToken.None);
        await page.LoadReviewAsync();
        return (fake, context, page);
    }

    private static PendingChangesSnapshot Snapshot(string? draftId, (PendingChange Change, ChangeFit Fit)[] words) =>
        new(draftId, draftId is null ? "none" : "revision/one", words.Select(word => word.Change).ToArray(),
            words.Select(word => word.Fit).ToArray());

    private static ProposalReviewResponse Review(ReviewEntry[] entries) =>
        new(ProposalId, "sha256:intent", "sha256:revision",
            entries.Select((entry, index) => entry switch
            {
                GrammarChangeEntry grammar => grammar with { Ordinal = index },
                WordChangeEntry word => word with { Ordinal = index },
                _ => entry,
            }).ToArray(), null, null, [], 0) { EvidenceDigest = "sha256:evidence", Label = "Sound change" };

    private static PendingChange WordChange(string id) =>
        new(id, "wordform/" + id, id, "approve", "assessment/one", "reading", ["op-" + id]);

    private static WordChangeEntry WordEntry(PendingChange change) =>
        new(change.ChangeId, 0, change.Word + ": approve an analysis", change.OperationIds, [], "digest-" + change.ChangeId,
            false, change, null, new Paired<ParserReading?>(null, null, false, false, false, null));

    private static GrammarChangeEntry Rule(string key, string? dependsOn = null) =>
        new(key, 0, "Title " + key, ["op-" + key], dependsOn is null ? [] : [dependsOn], "digest-" + key, false,
            "Composer", "1 change", [], [], []);

    private static TrialRecord Trial(TrialFreshness freshness) => new("job/trial", ProposalId, "sha256:intent", Token,
        null, new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false,
        null, JobStatus.Completed, DateTimeOffset.UtcNow, freshness);

    private static WorkspaceContext NewContext(FakeCommandClient fake)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
