using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.RichText;
using SIL.Motif.Projection.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Rationales and Comments are checked as FieldWorks Markdown against the project's Baseline (ADR 0099).</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ProposalTextFindingsTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Clean = "Glosses [entry-1](/lexicon/entry-1){.name} as the corpus does.";
    private const string Table = "Glosses as the corpus does:\n\n| Form | Gloss |\n| --- | --- |\n| form-1 | gloss-1 |\n| form-2 | gloss-2 |\n";
    private const string MissingLink = "Glosses [entry-9](/lexicon/entry-9){.name} as the corpus does.";
    private static string Version => MotifProductVersion.CurrentText;
    private Guid _entry1;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.ProposalTextFindings", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ADraftRationaleWithFindingsSavesAsWrittenAndReturnsThem()
    {
        var path = ProjectWithBaseline();
        var (_, id) = Draft(path, "draft-findings");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink));

        Assert.True(written.Succeeded, written.Refusal?.Message);
        var finding = Assert.Single(written.Value!.Findings);
        Assert.Equal("fwmd.link.not-found", finding.Id);
        Assert.Equal(1, finding.Line);
        Assert.Equal(MissingLink, Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void FinalizeIsRefusedWhileTheRationaleHasFindingsAndListsThemThenSucceedsOnceFixed()
    {
        var path = ProjectWithBaseline();
        var (name, id) = Draft(path, "finalize-findings");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink)).Succeeded);

        var refused = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));

        Assert.Equal(RefusalCodes.ProposalRationaleFindings, refused.Refusal?.Code);
        Assert.Equal("1", refused.Refusal!.Facts["findingCount"]);
        Assert.Contains("fwmd.link.not-found", refused.Refusal.Facts["findings"]);
        Assert.Contains("fwmd.link.not-found", refused.Refusal.Message);
        Assert.Equal(id, refused.Refusal.Facts["proposalId"]);

        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean)).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
    }

    [Fact]
    public void AnEditOfARationaleChangedMeanwhileIsRefusedAndTheOtherWriteStays()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var (_, id) = Draft(path, "changed-meanwhile");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Theirs.", "agent-1")).Succeeded);

        var refused = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Mine.", ExpectedRationale: "Before."));
        var saved = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Mine.", ExpectedRationale: "Theirs."));

        Assert.Equal(RefusalCodes.ProposalRationaleStale, refused.Refusal?.Code);
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        Assert.Equal("Mine.", Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void ARationaleWrittenWhileAnEditIsCheckedIsNotOverwritten()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var (_, id) = Draft(path, "written-meanwhile");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Theirs.")).Succeeded);

        var refused = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Mine.", ExpectedRationale: "Theirs."),
            (database, project, text) =>
            {
                var checkedText = ProposalTexts.Check(database, project, text);
                Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Another.")).Succeeded);
                return checkedText;
            });

        Assert.Equal(RefusalCodes.ProposalRationaleStale, refused.Refusal?.Code);
        Assert.Equal("Another.", Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void AReadyRevisionWithFindingsIsRefusedAndTheLastCleanRationaleStays()
    {
        var path = ProjectWithBaseline();
        var id = Finalized(path, "ready-findings");
        var before = Show(path, id).Rationale!.Markdown;
        var revisions = Revisions(path, id).Count;

        var refused = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink, "Claude"));

        Assert.Equal(RefusalCodes.ProposalRationaleFindings, refused.Refusal?.Code);
        Assert.Contains("fwmd.link.not-found", refused.Refusal!.Message);
        Assert.Equal(before, Show(path, id).Rationale!.Markdown);
        Assert.Equal(revisions, Revisions(path, id).Count);
        var clean = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean, "Claude"));
        Assert.True(clean.Succeeded, clean.Refusal?.Message);
        Assert.Empty(clean.Value!.Findings);
    }

    [Fact]
    public void ACodeSpanInAReadyRationaleOrACommentIsPlainTextAndNoFinding()
    {
        const string withCode = "Run `motif proposals` and read [form-1]{.v} beside it.";
        var path = ProjectWithBaseline();
        var id = Finalized(path, "ready-code-span");

        var revised = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, withCode, "Claude"));
        var commented = ProposalComments.Add(new AddCommentRequest(path, Version, id, withCode, "person-1", "agent-1"));

        Assert.True(revised.Succeeded, revised.Refusal?.Message);
        Assert.Empty(revised.Value!.Findings);
        Assert.Equal(withCode, Show(path, id).Rationale!.Markdown);
        Assert.True(commented.Succeeded, commented.Refusal?.Message);
        Assert.Empty(commented.Value!.Findings);
    }

    [Fact]
    public void AReopenedReadyProposalStillRefusesARevisionWithFindings()
    {
        var path = ProjectWithBaseline();
        var id = Finalized(path, "reopened-ready");
        var before = Show(path, id).Rationale!.Markdown;
        Assert.True(ProposalCommands.Reopen(new ReopenRequest(path, Version, "reopened-ready-2", id)).Succeeded);

        var refused = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink));

        Assert.Equal(RefusalCodes.ProposalRationaleFindings, refused.Refusal?.Code);
        Assert.Equal(before, Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void CommentsWithFindingsSaveAsWrittenAndNeverBlockFinalize()
    {
        var path = ProjectWithBaseline();
        var (name, id) = Draft(path, "comment-findings");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean)).Succeeded);

        var added = ProposalComments.Add(new AddCommentRequest(path, Version, id, MissingLink, "person-1", "agent-1"));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Equal("fwmd.link.not-found", Assert.Single(added.Value!.Findings).Id);
        var edited = ProposalComments.Edit(new EditCommentRequest(path, Version, id, added.Value.CommentId, "An <b>unread</b> tag."));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        Assert.NotEmpty(edited.Value!.Findings);
        Assert.All(edited.Value.Findings, finding => Assert.Equal("fwmd.syntax.unsupported-markup", finding.Id));

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));

        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        Assert.Equal("An <b>unread</b> tag.", Assert.Single(Show(path, id).Comments!).Markdown);
        var onReady = ProposalComments.Add(new AddCommentRequest(path, Version, id, MissingLink, "person-1"));
        Assert.True(onReady.Succeeded, onReady.Refusal?.Message);
        Assert.NotEmpty(onReady.Value!.Findings);
    }

    [Fact]
    public void ACleanTextIsStoredAsWrittenWithItsLineEndingsTidiedAndThePathLinkGivenItsGuid()
    {
        var path = ProjectWithBaseline();
        var (_, id) = Draft(path, "tidied");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean));
        var comment = ProposalComments.Add(new AddCommentRequest(path, Version, id, "First line.\r\n\r\nSecond line.\r\n", "person-1"));

        Assert.True(written.Succeeded, written.Refusal?.Message);
        Assert.Empty(written.Value!.Findings);
        Assert.Equal($"Glosses [entry-1](/lexicon/entry-1){{.name guid={_entry1}}} as the corpus does.",
            Show(path, id).Rationale!.Markdown);
        Assert.Empty(comment.Value!.Findings);
        Assert.Equal("First line.\n\nSecond line.", Assert.Single(Show(path, id).Comments!).Markdown);
    }

    [Fact]
    public void ALinkWrittenAsAPathAloneIsFoundInTheBaselineAndGetsItsGuid()
    {
        var path = ProjectWithBaseline();
        var (_, id) = Draft(path, "bare-link");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id,
            "Glosses [entry-1](/lexicon/entry-1) as the corpus does."));

        Assert.True(written.Succeeded, written.Refusal?.Message);
        Assert.Empty(written.Value!.Findings);
        Assert.Equal($"Glosses [entry-1](/lexicon/entry-1){{guid={_entry1}}} as the corpus does.",
            Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void ADraftWithRationaleFindingsCanStillBeWithdrawnOrDeclined()
    {
        var path = ProjectWithBaseline();
        var (_, withdrawn) = Draft(path, "withdraw-findings");
        var (_, declined) = Draft(path, "decline-findings");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, withdrawn, MissingLink)).Succeeded);
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, declined, MissingLink)).Succeeded);

        var withdraw = ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, withdrawn));
        var decline = ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, declined));

        Assert.True(withdraw.Succeeded, withdraw.Refusal?.Message);
        Assert.True(decline.Succeeded, decline.Refusal?.Message);
        Assert.Equal(MissingLink, Show(path, declined).Rationale!.Markdown);
    }

    [Fact]
    public void AnUnknownWritingSystemIsAFinding()
    {
        var path = ProjectWithBaseline();
        var (_, id) = Draft(path, "unknown-ws");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id,
            "The form [text-1]{ws=qaa-x-unknown} is new."));

        Assert.True(written.Succeeded, written.Refusal?.Message);
        Assert.Equal("fwmd.ws.unknown", Assert.Single(written.Value!.Findings).Id);
    }

    [Fact]
    public void AnExistingReadyRationaleWithFindingsIsLeftAsWritten()
    {
        var path = ProjectWithBaseline();
        var id = Finalized(path, "existing-ready");
        const string legacy = "Old text with [entry-9](/lexicon/entry-9) and <b>raw</b> HTML.";
        Assert.True(ProjectStoreCommand.Run(path, Version, (database, _) => CommandOutcome<string>.Success(
            new ProposalRepository(database).SetRationale(CanonicalId.Parse(id), legacy) ? "" : null!)).Succeeded);

        Assert.Equal(legacy, Show(path, id).Rationale!.Markdown);
        Assert.True(ProposalComments.Add(new AddCommentRequest(path, Version, id, "Seen.", "person-1")).Succeeded);
        Assert.Equal(legacy, Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void WithoutABaselineOnlyTheMarkupIsCheckedAndAPathNeedsItsGuid()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var (name, id) = Draft(path, "no-baseline");

        var markup = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Some <b>raw</b> HTML."));
        var link = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink));
        var plain = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Why it is right."));

        Assert.NotEmpty(markup.Value!.Findings);
        Assert.All(markup.Value.Findings, finding => Assert.Equal("fwmd.syntax.unsupported-markup", finding.Id));
        Assert.Equal("fwmd.link.not-found", Assert.Single(link.Value!.Findings).Id);
        Assert.Empty(plain.Value!.Findings);
        Assert.Equal("Why it is right.", Show(path, id).Rationale!.Markdown);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, Version, name)).Succeeded);
    }

    [Fact]
    public void ARationaleWithAPipeTableHasNoFindingsIsStoredAsATableAndFinalizes()
    {
        var path = ProjectWithBaseline();
        var (name, id) = Draft(path, "table");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Table));

        Assert.True(written.Succeeded, written.Refusal?.Message);
        Assert.Empty(written.Value!.Findings);
        var stored = Show(path, id).Rationale!.Markdown;
        Assert.Contains("| form-1 | gloss-1 |", stored);
        Assert.Contains("| form-2 | gloss-2 |", stored);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
    }

    [Fact]
    public void ATableWithARaggedRowIsAFinding()
    {
        var path = ProjectWithBaseline();
        var (_, id) = Draft(path, "ragged");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id,
            "| Form | Gloss |\n| --- | --- |\n| form-1 |\n"));

        Assert.Equal("fwmd.table.ragged", Assert.Single(written.Value!.Findings).Id);
    }

    [Fact]
    public void WithoutABaselineAPipeTableIsNoFindingEither()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var (name, id) = Draft(path, "table-no-baseline");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Table));

        Assert.Empty(written.Value!.Findings);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, Version, name)).Succeeded);
    }

    [Theory]
    [InlineData("**Evidence:**\n- a\n- b", ProposalTextChecker.ListNeedsBlankLine, 2)]
    [InlineData("Intro\n1. a\n2. b", ProposalTextChecker.ListNeedsBlankLine, 2)]
    [InlineData("1. x\n\ny\n\n2. z", ProposalTextChecker.ListRenumbered, 5)]
    [InlineData("3. a\n4. b", ProposalTextChecker.ListRenumbered, 1)]
    public void AListACommonMarkReaderWouldShowDifferentlyIsAFindingAndTheTextIsKeptAsWritten(string text, string id, int line)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var (_, proposal) = Draft(path, "structure");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, proposal, text));

        var finding = Assert.Single(written.Value!.Findings);
        Assert.Equal((id, line), (finding.Id, finding.Line));
        Assert.Equal(text, Show(path, proposal).Rationale!.Markdown);
    }

    [Theory]
    [InlineData("1. a\n\n2. b")]
    [InlineData("Intro\n2. a")]
    [InlineData("- a\n- b")]
    [InlineData("Intro\n\n- a\n- b")]
    [InlineData("[x]{ws=en} and **bold**")]
    public void ATextFieldWorksAndCommonMarkReadAlikeIsCleanAndStoredExactlyAsWritten(string text)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var (_, proposal) = Draft(path, "alike");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, proposal, text));

        Assert.Empty(written.Value!.Findings);
        Assert.Equal(text, Show(path, proposal).Rationale!.Markdown);
    }

    [Fact]
    public void ADraftFinalizedWhileItsRationaleIsCheckedDoesNotTakeTheRationale()
    {
        var path = ProjectWithBaseline();
        var (name, id) = Draft(path, "race-write");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean)).Succeeded);
        var before = Show(path, id).Rationale!.Markdown;

        var refused = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink), (database, project, text) =>
        {
            var checkedText = ProposalTexts.Check(database, project, text);
            var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
            return checkedText;
        });

        Assert.Equal(RefusalCodes.ProposalRationaleStale, refused.Refusal?.Code);
        var shown = Show(path, id);
        Assert.Null(shown.DraftName);
        Assert.Equal(before, shown.Rationale!.Markdown);
    }

    [Fact]
    public void ARationaleWrittenWhileFinalizeChecksItLeavesTheDraftADraft()
    {
        var path = ProjectWithBaseline();
        var (name, id) = Draft(path, "race-finalize");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean)).Succeeded);

        var refused = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name), (database, project, text) =>
        {
            var checkedText = ProposalTexts.Check(database, project, text);
            Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink)).Succeeded);
            return checkedText;
        });

        Assert.Equal(RefusalCodes.ProposalRationaleStale, refused.Refusal?.Code);
        Assert.Equal(name, Show(path, id).DraftName);
        Assert.Equal(RefusalCodes.ProposalRationaleFindings,
            ProposalCommands.Finalize(new FinalizeRequest(path, Version, name)).Refusal?.Code);
    }

    [Fact]
    public void FinalizeChecksAgainstTheBaselineAsItIsNowNotAsTheRationaleWasWritten()
    {
        var path = ProjectWithBaseline();
        var (name, id) = Draft(path, "baseline-moved");
        Assert.Empty(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean)).Value!.Findings);
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_entry1);
            SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.DomainDataByFlid.DeleteObj(entry.Hvo));
            new FwDataProjectLoader().Save(cache);
        }
        Captured(path);

        var refused = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));

        Assert.Equal(RefusalCodes.ProposalRationaleFindings, refused.Refusal?.Code);
        Assert.Contains("fwmd.link.broken", refused.Refusal!.Facts["findings"]);
    }

    [Fact]
    public void ADeferredProposalRefusesARevisionWithFindings()
    {
        var path = ProjectWithBaseline();
        var id = Finalized(path, "deferred");
        Assert.True(ProposalCommands.Defer(new DeferRequest(path, Version, id)).Succeeded);
        var before = Show(path, id).Rationale!.Markdown;

        var refused = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink));

        Assert.Equal(RefusalCodes.ProposalRationaleFindings, refused.Refusal?.Code);
        Assert.Equal(before, Show(path, id).Rationale!.Markdown);
    }

    [Fact]
    public void ADuplicateOrSplitCopyOfARationaleWithFindingsCannotBeFinalizedUntilItIsFixed()
    {
        var path = ProjectWithBaseline();
        var id = Finalized(path, "copied");
        Assert.True(ProjectStoreCommand.Run(path, Version, (database, _) => CommandOutcome<string>.Success(
            new ProposalRepository(database).SetRationale(CanonicalId.Parse(id), MissingLink) ? "" : null!)).Succeeded);
        var operation = Assert.Single(Show(path, id).Operations).OperationId;

        Assert.True(ProposalCommands.Duplicate(new DuplicateRequest(path, Version, id, "copied-duplicate")).Succeeded);
        Assert.True(ProposalCommands.Split(new SplitRequest(path, Version, id,
            [new SplitGroup("copied-split", [operation])], Force: false)).Succeeded);

        foreach (var copy in new[] { "copied-duplicate", "copied-split" })
            Assert.Equal(RefusalCodes.ProposalRationaleFindings,
                ProposalCommands.Finalize(new FinalizeRequest(path, Version, copy)).Refusal?.Code);
    }

    [Fact]
    public void WhenTheBaselineCannotBeReadAReadyRevisionAndFinalizeAreRefusedAndACommentStillSaves()
    {
        var path = ProjectWithBaseline();
        var ready = Finalized(path, "unreadable-ready");
        var (name, draft) = Draft(path, "unreadable-draft");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, draft, Clean)).Succeeded);
        File.Delete(ProjectStoreCommand.Run(path, Version, (database, project) => CommandOutcome<string>.Success(
            new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!.FwDataPath)).Value!);

        var revision = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, ready, Clean));
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));
        var comment = ProposalComments.Add(new AddCommentRequest(path, Version, ready, Clean, "person-1"));

        Assert.Equal(RefusalCodes.ProposalRationaleUnchecked, revision.Refusal?.Code);
        Assert.Equal(RefusalCodes.ProposalRationaleUnchecked, finalized.Refusal?.Code);
        Assert.True(comment.Succeeded, comment.Refusal?.Message);
    }

    [Fact]
    public void OnlyADeclineOrWithdrawAtFinalizeSkipsTheRationaleCheck()
    {
        var path = ProjectWithBaseline();
        var (name, id) = Draft(path, "finalize-then-defer");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, MissingLink)).Succeeded);

        var refused = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name, null,
            new FinalizeMove([ManifestStatus.Proposed], ManifestStatus.Deferred)));

        Assert.Equal(RefusalCodes.ProposalRationaleFindings, refused.Refusal?.Code);
        Assert.Equal(name, Show(path, id).DraftName);
    }

    [Fact]
    public void ADeclinesReasonIsAComment()
    {
        var path = ProjectWithBaseline();
        var ready = Finalized(path, "decline-reason");
        var (_, draft) = Draft(path, "decline-draft-reason");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, draft, Clean)).Succeeded);

        Assert.True(ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, ready, "Not yet.\r\nLater.\r\n")).Succeeded);
        Assert.True(ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, draft, "Some <b>raw</b> reason.")).Succeeded);

        Assert.Equal("Not yet.\nLater.", Assert.Single(Show(path, ready).Comments!).Markdown);
        Assert.Equal("Some <b>raw</b> reason.", Assert.Single(Show(path, draft).Comments!).Markdown);
    }

    [Fact]
    public void TheKeptVersionsOfARationaleAreReadNewestFirstWithTheirAuthors()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var (_, id) = Draft(path, "versions");
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "First.", "agent-1")).Succeeded);
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, "Second.", null, "person-1")).Succeeded);

        var read = ProposalTexts.RationaleRevisions(new RationaleRevisionsRequest(path, Version, id));

        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Equal([("Second.", null, "person-1"), ("First.", "agent-1", "")],
            read.Value!.Select(revision => (revision.Markdown, revision.AuthorLabel, revision.PersonName)));
    }

    private (string Name, string Id) Draft(string path, string name)
    {
        var created = ProposalCommands.New(new NewDraftRequest(path, Version, name, name));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, name,
            CanonicalId.Mint().Value, "en", name)).Succeeded);
        return (name, created.Value!.ProposalId);
    }

    private string Finalized(string path, string name)
    {
        var (_, id) = Draft(path, name);
        Assert.True(ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean)).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return id;
    }

    private string ProjectWithBaseline()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        SeededGdrs.SeedLinks(cache);
        _entry1 = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Single(entry => entry.LexemeFormOA?.Form.VernacularDefaultWritingSystem.Text == "entry-1").Guid;
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Captured(path);
        return path;
    }

    private void Captured(string path)
    {
        var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
    }

    private static ProposalDetailProjection Show(string path, string id)
    {
        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private static IReadOnlyList<RationaleRevisionRecord> Revisions(string path, string id) =>
        ProjectStoreCommand.Run(path, Version, (database, _) => CommandOutcome<IReadOnlyList<RationaleRevisionRecord>>.Success(
            new ProposalDiscussionRepository(database).ListRationaleRevisions(CanonicalId.Parse(id)))).Value!;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
