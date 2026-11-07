using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class DiscardEmptyDraftsTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void EmptyDraftsGoAndADraftWithChangesStays()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        foreach (var name in new[] { "orphan-a", "orphan-b", "work" })
            Assert.True(ProposalCommands.New(new(path, Version, name, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, "work",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag, "gloss")).Succeeded);

        var outcome = ProposalCommands.DiscardEmptyDrafts(new DiscardEmptyDraftsRequest(path, Version));

        Assert.True(outcome.Succeeded);
        Assert.Equal(["orphan-a", "orphan-b"], outcome.Value!.Discarded.Order(StringComparer.Ordinal));
        Assert.Empty(outcome.Value.Kept);
        Assert.True(ProposalCommands.New(new(path, Version, "orphan-a", null)).Succeeded);
        Assert.Equal("draft.name-collision", ProposalCommands.New(new(path, Version, "work", null)).Refusal?.Code);
    }

    [Fact]
    public void APrefixLimitsWhichEmptyDraftsAreConsidered()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        foreach (var name in new[] { "chain-1", "other" })
            Assert.True(ProposalCommands.New(new(path, Version, name, null)).Succeeded);

        var outcome = ProposalCommands.DiscardEmptyDrafts(new DiscardEmptyDraftsRequest(path, Version, "chain-"));

        Assert.Equal(["chain-1"], outcome.Value!.Discarded);
        Assert.Equal("draft.name-collision", ProposalCommands.New(new(path, Version, "other", null)).Refusal?.Code);
    }

    [Fact]
    public void NothingToDiscardSucceedsWithAnEmptyList()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var outcome = ProposalCommands.DiscardEmptyDrafts(new DiscardEmptyDraftsRequest(path, Version));
        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Discarded);
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    [InlineData(true, true, null)]
    [InlineData(false, true, "Review agent")]
    [InlineData(true, true, "Review agent")]
    public void AuthoredRationaleAndCommentsAreKeptAndReported(bool rationale, bool comment, string? authorLabel)
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var created = ProposalCommands.New(new(path, Version, "planned-work", "Plan the merge"));
        Assert.True(created.Succeeded);
        var id = created.Value!.ProposalId;
        if (rationale)
            Assert.True(ProposalTexts.SetRationale(new(path, Version, id, "Keep the plan before composing.")).Succeeded);
        if (comment)
            Assert.True(ProposalComments.Add(new(path, Version, id, "Keep the source citations.", "Alex", authorLabel))
                .Succeeded);

        var outcome = ProposalCommands.DiscardEmptyDrafts(new(path, Version));

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Discarded);
        var kept = Assert.Single(outcome.Value.Kept);
        Assert.Equal("planned-work", kept.DraftName);
        Assert.Equal("draft.authored-content", kept.Code);
        Assert.Contains("Rationale or Comments", kept.Reason, StringComparison.Ordinal);
        using var database = ProjectMotifDatabase.Open(path);
        var record = new ProposalRepository(database).GetDraft("planned-work");
        Assert.Equal(id, record.ProposalId.Value);
        Assert.Equal(rationale ? "Keep the plan before composing." : null, record.Rationale);
        var comments = new ProposalDiscussionRepository(database).ListComments(record.ProposalId);
        if (comment)
        {
            var keptComment = Assert.Single(comments);
            Assert.Equal("Keep the source citations.", keptComment.Text);
            Assert.Equal(new DiscussionAuthor(authorLabel, "Alex"), keptComment.Author);
        }
        else Assert.Empty(comments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PicturesOnTheProposalOrItsCommentsAreKeptAndReported(bool onComment)
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var created = ProposalCommands.New(new(path, Version, "pictured", null));
        Assert.True(created.Succeeded);
        var id = created.Value!.ProposalId;
        long? commentId = null;
        if (onComment)
        {
            var comment = ProposalComments.Add(new(path, Version, id, "Keep this picture.", "Alex"));
            Assert.True(comment.Succeeded);
            commentId = comment.Value!.CommentId;
        }
        var bytes = onComment ? ProposalPicturesTests.Real(20, 10, jpeg: true) : ProposalPicturesTests.Real(20, 10);
        var picture = ProposalPictures.Add(new(path, Version, id, bytes, commentId));
        Assert.True(picture.Succeeded, picture.Refusal?.Message);
        var before = ProposalPictures.Show(new(path, Version, id, picture.Value!.PictureId));
        Assert.True(before.Succeeded, before.Refusal?.Message);
        var storedBytes = Convert.FromBase64String(Assert.IsType<string>(before.Value!.DataBase64));

        var outcome = ProposalCommands.DiscardEmptyDrafts(new(path, Version));

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Discarded);
        var kept = Assert.Single(outcome.Value.Kept);
        Assert.Equal("pictured", kept.DraftName);
        Assert.Equal("draft.authored-content", kept.Code);
        Assert.Contains("pictures", kept.Reason, StringComparison.Ordinal);
        using var database = ProjectMotifDatabase.Open(path);
        Assert.Equal(id, new ProposalRepository(database).GetDraft("pictured").ProposalId.Value);
        var pictures = new ProposalPictureRepository(database);
        var stored = Assert.Single(pictures.List(CanonicalId.Parse(id)));
        Assert.Equal(picture.Value!.PictureId, stored.PictureId);
        Assert.Equal(commentId, stored.CommentId);
        Assert.Equal(storedBytes, pictures.ReadBytes(CanonicalId.Parse(id), stored.PictureId));
    }

    [Fact]
    public void APictureAttachedAfterListingIsProtectedInsideTheDiscardTransaction()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        Assert.True(ProposalCommands.New(new(path, Version, "picture-after-listing", null)).Succeeded);
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var listed = repository.GetDraft("picture-after-listing");
        var bytes = ProposalPicturesTests.Real(20, 10);
        var picture = ProposalPictures.Add(new(path, Version, listed.ProposalId.Value, bytes));
        Assert.True(picture.Succeeded, picture.Refusal?.Message);
        var pictures = new ProposalPictureRepository(database);
        var storedBytes = Assert.IsType<byte[]>(pictures.ReadBytes(listed.ProposalId, picture.Value!.PictureId));

        Assert.Throws<DraftAuthoredContentException>(() => repository.DiscardDraft("picture-after-listing",
            listed.ProposalJson, beforeEvidenceRemoved: null, emptyDraftId: listed.ProposalId));

        Assert.Equal(listed, repository.GetDraft("picture-after-listing"));
        Assert.Equal(storedBytes, pictures.ReadBytes(listed.ProposalId, picture.Value.PictureId));
    }

    [Fact]
    public void AnEmptyReopenedDraftIsUntouched()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        var created = ProposalCommands.New(new(path, Version, "reopened", "Revise the entry"));
        Assert.True(created.Succeeded);
        var id = created.Value!.ProposalId;
        Assert.True(ProposalTexts.SetRationale(new(path, Version, id, "Revise the entry.")).Succeeded);
        var added = ProposalCommands.AddSetGloss(new(path, Version, "reopened", CanonicalId.Mint().Value, "en", "gloss"));
        Assert.True(added.Succeeded);
        Assert.True(ProposalCommands.Finalize(new(path, Version, "reopened")).Succeeded);
        Assert.True(ProposalCommands.Withdraw(new(path, Version, id)).Succeeded);
        Assert.True(ProposalCommands.Reopen(new(path, Version, "reopened", id)).Succeeded);
        Assert.True(ProposalCommands.RemoveOperations(new(path, Version, "reopened", [added.Value!.OperationId], false)).Succeeded);
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var before = repository.GetDraft("reopened");

        var outcome = ProposalCommands.DiscardEmptyDrafts(new(path, Version));

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Discarded);
        Assert.Empty(outcome.Value.Kept);
        Assert.NotNull(before.IntentDigest);
        Assert.Equal(before, repository.GetDraft("reopened"));
        Assert.NotNull(Assert.Single(repository.ListDrafts()).IntentDigest);
        Assert.Throws<DraftChangedException>(() => repository.DiscardDraft("reopened", before.ProposalJson,
            beforeEvidenceRemoved: null, emptyDraftId: before.ProposalId));
        Assert.Equal(before, repository.GetDraft("reopened"));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(false, "Review agent")]
    public void DiscussionWrittenAfterListingIsProtectedInsideTheDiscardTransaction(bool rationale, string? authorLabel)
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        Assert.True(ProposalCommands.New(new(path, Version, "writing", null)).Succeeded);
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var listed = repository.GetDraft("writing");
        if (rationale)
            Assert.True(ProposalTexts.SetRationale(new(path, Version, listed.ProposalId.Value, "New rationale.")).Succeeded);
        else
            Assert.True(ProposalComments.Add(new(path, Version, listed.ProposalId.Value, "New comment.", "Alex", authorLabel))
                .Succeeded);

        Assert.Throws<DraftAuthoredContentException>(() => repository.DiscardDraft("writing", listed.ProposalJson,
            beforeEvidenceRemoved: null, emptyDraftId: listed.ProposalId));

        Assert.True(repository.DraftNameExists("writing"));
        Assert.Equal(listed.ProposalJson, repository.GetDraft("writing").ProposalJson);
        if (rationale) Assert.Equal("New rationale.", repository.GetDraft("writing").Rationale);
        else
        {
            var keptComment = Assert.Single(new ProposalDiscussionRepository(database).ListComments(listed.ProposalId));
            Assert.Equal("New comment.", keptComment.Text);
            Assert.Equal(new DiscussionAuthor(authorLabel, "Alex"), keptComment.Author);
        }
    }

    [Fact]
    public void AnOperationSavedAfterListingPreventsDiscard()
    {
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        Assert.True(ProposalCommands.New(new(path, Version, "composing", null)).Succeeded);
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var listed = repository.GetDraft("composing");
        Assert.True(ProposalCommands.AddSetGloss(new(path, Version, "composing", CanonicalId.Mint().Value, "en", "gloss"))
            .Succeeded);

        Assert.Throws<DraftChangedException>(() => repository.DiscardDraft("composing", listed.ProposalJson,
            beforeEvidenceRemoved: null, emptyDraftId: listed.ProposalId));

        Assert.Single(ProposalCommands.DeserializeDraft(repository.GetDraft("composing").ProposalJson!).Operations);
    }
}
