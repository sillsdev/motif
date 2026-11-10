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
using SIL.Motif.Tests.TestFixtures;
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
    public void ACleanTextIsStoredInItsCanonicalSpellingWithTheLinkItsGuid()
    {
        var path = ProjectWithBaseline();
        var (_, id) = Draft(path, "canonical");

        var written = ProposalTexts.SetRationale(new SetRationaleRequest(path, Version, id, Clean));
        var comment = ProposalComments.Add(new AddCommentRequest(path, Version, id, "First line.\r\n\r\nSecond line.\r\n", "person-1"));

        Assert.True(written.Succeeded, written.Refusal?.Message);
        Assert.Empty(written.Value!.Findings);
        var stored = Show(path, id).Rationale!.Markdown;
        Assert.StartsWith("Glosses [entry-1](/lexicon/entry-1){", stored, StringComparison.Ordinal);
        Assert.Contains("guid=", stored, StringComparison.Ordinal);
        Assert.EndsWith(" as the corpus does.", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("fwmd:", stored, StringComparison.Ordinal);
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
        Assert.Contains("guid=", Show(path, id).Rationale!.Markdown, StringComparison.Ordinal);
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
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        return path;
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
