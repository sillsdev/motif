using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Receipts;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class ProposalDiscussionRepositoryTests : IDisposable
{
    private static readonly string Digest = "sha256:" + new string('a', 64);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-discussion-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly ProposalRepository _proposals;
    private readonly ProposalDiscussionRepository _discussion;

    public ProposalDiscussionRepositoryTests()
    {
        Directory.CreateDirectory(_root);
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "discussion.motif.db"),
            new ProjectLocator(Path.Combine(_root, "discussion.fwdata"), "discussion"),
            MotifSchema.CurrentSchema, new Version(1, 0));
        _proposals = new ProposalRepository(_database);
        _discussion = new ProposalDiscussionRepository(_database);
    }

    [Fact]
    public void RevisionsGrowWhileOpenEachCarryingItsAuthorAndTheLatestTextIsTheRationale()
    {
        var id = Open();

        Assert.True(_proposals.SetRationale(id, "first"));
        Assert.True(_proposals.SetRationale(id, "second", new DiscussionAuthor("Claude (Opus 5.5)", "John")));
        Assert.True(_proposals.SetRationale(id, "third", new DiscussionAuthor(null, "John")));

        var revisions = _discussion.ListRationaleRevisions(id);
        Assert.Equal(["first", "second", "third"], revisions.Select(revision => revision.Text));
        Assert.Equal(new DiscussionAuthor(null, ""), revisions[0].Author);
        Assert.Equal(new DiscussionAuthor("Claude (Opus 5.5)", "John"), revisions[1].Author);
        Assert.Equal(new DiscussionAuthor(null, "John"), revisions[2].Author);
        Assert.All(revisions, revision => Assert.Equal(Digest, revision.IntentDigest));
        Assert.Equal("third", _proposals.Get(id).Rationale);
    }

    [Fact]
    public void AnOpenProposalKeepsOnlyTheNewestHundredRevisions()
    {
        var id = Open();

        for (var number = 1; number <= 105; number++) Assert.True(_proposals.SetRationale(id, "text " + number));

        var revisions = _discussion.ListRationaleRevisions(id);
        Assert.Equal(100, revisions.Count);
        Assert.Equal("text 6", revisions[0].Text);
        Assert.Equal("text 105", revisions[^1].Text);
        Assert.Equal("text 105", _proposals.Get(id).Rationale);
    }

    [Fact]
    public void ClearingTheRationaleKeepsNoRevisionForNoText()
    {
        var id = Open();
        _proposals.SetRationale(id, "kept");

        _proposals.SetRationale(id, null);

        Assert.Single(_discussion.ListRationaleRevisions(id));
        Assert.Null(_proposals.Get(id).Rationale);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("superseded")]
    public void TransitionToADecisionDropsTheHistoryAndKeepsTheLatestText(string decision)
    {
        var id = Open();
        _proposals.SetRationale(id, "one");
        _proposals.SetRationale(id, "two");

        Assert.True(_proposals.Transition(id, ["proposed"], decision).Moved);

        Assert.Empty(_discussion.ListRationaleRevisions(id));
        Assert.Equal("two", _proposals.Get(id).Rationale);
    }

    [Fact]
    public void ApplyingDropsTheHistoryAndKeepsTheLatestText()
    {
        var id = Open();
        _proposals.SetRationale(id, "one");
        _proposals.SetRationale(id, "two");

        _proposals.RecordAppliedReceipt(new Receipt(id, Digest, false, "baseline", "applied", Array.Empty<ExpectedEffect>(),
            "sha256:effect", new AppliedLogEntry(id.ToGuid(), 1, "2026-09-25T00:00:00Z", "tester", Digest[7..], "test")));

        Assert.Empty(_discussion.ListRationaleRevisions(id));
        Assert.Equal("two", _proposals.Get(id).Rationale);
    }

    [Fact]
    public void ASetStatusDecisionDropsTheHistoryButADeferralDoesNot()
    {
        var id = Open();
        _proposals.SetRationale(id, "one");

        _proposals.SetStatus(id, "deferred", null);
        Assert.Single(_discussion.ListRationaleRevisions(id));

        _proposals.SetStatus(id, "rejected", null);
        Assert.Empty(_discussion.ListRationaleRevisions(id));
    }

    [Fact]
    public void AReopenedProposalThawsStartsNewHistoryAndDropsItAgainWhenTheReopeningIsDiscarded()
    {
        var id = Open();
        _proposals.SetRationale(id, "before");
        _proposals.Transition(id, ["proposed"], "rejected");

        _proposals.ReopenAsDraft(id, "again", "{}");
        _proposals.SetRationale(id, "after");
        Assert.Equal(["after"], _discussion.ListRationaleRevisions(id).Select(revision => revision.Text));
        Assert.Equal(CommentWriteOutcome.Written, _discussion.AddComment(id, "thawed", new DiscussionAuthor(null, "John")).Outcome);

        Assert.True(_proposals.DiscardDraft("again"));
        Assert.Empty(_discussion.ListRationaleRevisions(id));
        Assert.Equal("after", _proposals.Get(id).Rationale);
        Assert.Single(_discussion.ListComments(id));
    }

    [Fact]
    public void ADraftsRevisionsAndCommentsTakeTheDigestItIsFinalizedWith()
    {
        var id = CanonicalId.Mint("proposal/");
        _proposals.CreateDraft("working", id, "{}");
        _proposals.SetRationale(id, "why");
        _discussion.AddComment(id, "note", new DiscussionAuthor(null, "John"));
        Assert.Null(Assert.Single(_discussion.ListComments(id)).IntentDigest);

        _proposals.Finalize("working", Digest, "{\"proposalId\":\"" + id.Value + "\"}", "label");

        Assert.Equal(Digest, Assert.Single(_discussion.ListComments(id)).IntentDigest);
        Assert.Equal(Digest, Assert.Single(_discussion.ListRationaleRevisions(id)).IntentDigest);
    }

    [Fact]
    public void ACommentIsStoredAsTypedAndAnEditMarksItEditedKeepingNoHistory()
    {
        var id = Open();
        var added = _discussion.AddComment(id, "Original", new DiscussionAuthor("  Claude (Opus 5.5)  ", "John"));
        Assert.Equal(CommentWriteOutcome.Written, added.Outcome);

        var before = Assert.Single(_discussion.ListComments(id));
        Assert.Equal("  Claude (Opus 5.5)  ", before.Author.Label);
        Assert.Null(before.EditedUtc);

        Assert.Equal(CommentWriteOutcome.Written, _discussion.EditComment(id, added.CommentId, "Changed").Outcome);

        var after = Assert.Single(_discussion.ListComments(id));
        Assert.Equal("Changed", after.Text);
        Assert.NotNull(after.EditedUtc);
        Assert.Equal(before.CreatedUtc, after.CreatedUtc);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM ProposalComments;"));
    }

    [Fact]
    public void ADecidedProposalRefusesToAddEditOrDeleteAComment()
    {
        var id = Open();
        var comment = _discussion.AddComment(id, "Before", new DiscussionAuthor(null, "John")).CommentId;
        _proposals.Transition(id, ["proposed"], "withdrawn");

        Assert.Equal(CommentWriteOutcome.Closed, _discussion.AddComment(id, "After", new DiscussionAuthor(null, "John")).Outcome);
        Assert.Equal(CommentWriteOutcome.Closed, _discussion.EditComment(id, comment, "After").Outcome);
        Assert.Equal(CommentWriteOutcome.Closed, _discussion.DeleteComment(id, comment).Outcome);
        Assert.Equal("Before", Assert.Single(_discussion.ListComments(id)).Text);
    }

    [Fact]
    public void ARationaleWrittenAfterTheDecisionLeavesNoRevision()
    {
        var id = Open();
        _proposals.Transition(id, ["proposed"], "rejected");

        _proposals.SetRationale(id, "late");

        Assert.Empty(_discussion.ListRationaleRevisions(id));
    }

    [Fact]
    public void AMissingProposalOrCommentIsReportedAsSuch()
    {
        var id = Open();

        Assert.Equal(CommentWriteOutcome.ProposalNotFound,
            _discussion.AddComment(CanonicalId.Mint("proposal/"), "x", new DiscussionAuthor(null, "")).Outcome);
        Assert.Equal(CommentWriteOutcome.CommentNotFound, _discussion.EditComment(id, 99, "x").Outcome);
        Assert.Equal(CommentWriteOutcome.CommentNotFound, _discussion.DeleteComment(id, 99).Outcome);
    }

    [Fact]
    public void DeletingACommentTakesItsPicturesAndLeavesTheOthers()
    {
        var id = Open();
        var first = _discussion.AddComment(id, "first", new DiscussionAuthor(null, "John")).CommentId;
        var second = _discussion.AddComment(id, "second", new DiscussionAuthor(null, "John")).CommentId;
        Exec($"INSERT INTO ProposalPictures (ProposalId, CommentId, MediaType, Width, Height, Bytes, CreatedUtc) VALUES " +
            $"('{id.Value}', {first}, 'image/png', 1, 1, X'00', '2020'), ('{id.Value}', {second}, 'image/png', 1, 1, X'00', '2020'), " +
            $"('{id.Value}', NULL, 'image/png', 1, 1, X'00', '2020');");

        Assert.Equal(CommentWriteOutcome.Written, _discussion.DeleteComment(id, first).Outcome);

        Assert.Equal(["second"], _discussion.ListComments(id).Select(comment => comment.Text));
        Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM ProposalPictures;"));
    }

    [Fact]
    public void DeletingAnUnappliedProposalRemovesEveryRevisionCommentAndPictureRow()
    {
        var id = Open();
        var other = Open();
        foreach (var target in new[] { id, other })
        {
            _proposals.SetRationale(target, "one");
            var comment = _discussion.AddComment(target, "a comment", new DiscussionAuthor(null, "John")).CommentId;
            Exec($"INSERT INTO ProposalPictures (ProposalId, CommentId, MediaType, Width, Height, Bytes, CreatedUtc) VALUES " +
                $"('{target.Value}', {comment}, 'image/png', 1, 1, X'00', '2020'), ('{target.Value}', NULL, 'image/png', 1, 1, X'00', '2020');");
        }
        _proposals.Transition(id, ["proposed"], "rejected");

        _proposals.DeleteArchived(id);

        foreach (var table in new[] { "ProposalRationaleRevisions", "ProposalComments", "ProposalPictures" })
        {
            Assert.Equal(0L, Scalar($"SELECT COUNT(*) FROM {table} WHERE ProposalId = '{id.Value}';"));
            Assert.Equal(table == "ProposalPictures" ? 2L : 1L, Scalar($"SELECT COUNT(*) FROM {table} WHERE ProposalId = '{other.Value}';"));
        }
    }

    [Fact]
    public void CommentCountsListOnlyProposalsThatHaveComments()
    {
        var id = Open();
        var empty = Open();
        _discussion.AddComment(id, "one", new DiscussionAuthor(null, ""));
        _discussion.AddComment(id, "two", new DiscussionAuthor("Agent", ""));

        var counts = _discussion.CommentCounts();

        Assert.Equal(2, counts[id.Value]);
        Assert.False(counts.ContainsKey(empty.Value));
    }

    [Fact]
    public void DeletingThePersonsCommentsLeavesAnAgentsCommentsAlone()
    {
        var id = Open();
        _discussion.AddComment(id, "mine", new DiscussionAuthor(null, "John"));
        _discussion.AddComment(id, "agent", new DiscussionAuthor("Claude", "John"));

        _discussion.DeletePersonComments(id);

        Assert.Equal(["agent"], _discussion.ListComments(id).Select(comment => comment.Text));
    }

    public void Dispose()
    {
        _database.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private CanonicalId Open()
    {
        var id = CanonicalId.Mint("proposal/");
        _proposals.SaveRevision(new ProposalRevisionRecord(id, Digest, "{}", "proposed", null, null, "{}"));
        return id;
    }

    private long Scalar(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Exec(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
