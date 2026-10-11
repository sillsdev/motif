using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ReviewMarksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ReviewMarksTests",
        Guid.NewGuid().ToString("N"));

    public ReviewMarksTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AMarkSurvivesWhileContentAndEvidenceMatch()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var (id, entry) = OneEntry(fixture);
        ReviewMarks.Put(fixture.Database, id, entry.EntryKey, entry.ContentDigest, ReviewMarks.NoEvidence);

        var review = ProposalReview.Read(fixture.Database, fixture.Project, id).Value!;

        Assert.True(review.Entries.Single().LookedAt);
        Assert.Equal(1, review.LookedAtCount);
    }

    [Fact]
    public void AMarkClearsWhenTheEntryContentChanges()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var (id, entry) = OneEntry(fixture);
        ReviewMarks.Put(fixture.Database, id, entry.EntryKey, "sha256:other", ReviewMarks.NoEvidence);

        Assert.False(ProposalReview.Read(fixture.Database, fixture.Project, id).Value!.Entries.Single().LookedAt);
    }

    [Fact]
    public void AMarkClearsWhenTheTrialChanges()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var (id, entry) = OneEntry(fixture);
        ReviewMarks.Put(fixture.Database, id, entry.EntryKey, entry.ContentDigest, "sha256:an-earlier-trial");

        Assert.False(ProposalReview.Read(fixture.Database, fixture.Project, id).Value!.Entries.Single().LookedAt);
    }

    [Fact]
    public void AMarkNamesNoPerson()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('ReviewMarks') ORDER BY cid;";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read()) columns.Add(reader.GetString(0));

        Assert.Equal(["ProposalId", "EntryKey", "ContentDigest", "EvidenceDigest", "LookedAtUtc"], columns);
    }

    [Fact]
    public void ClearingRemovesTheMark()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var (id, entry) = OneEntry(fixture);
        ReviewMarks.Put(fixture.Database, id, entry.EntryKey, entry.ContentDigest, ReviewMarks.NoEvidence);

        ReviewMarks.Clear(fixture.Database, id, [entry.EntryKey]);

        Assert.Empty(ReviewMarks.Read(fixture.Database, id));
    }

    private static (string Id, ReviewEntry Entry) OneEntry(TrialsTests.ProjectFixture fixture)
    {
        var operation = ProposalReviewTests.Op("lexical/lexSense/setGloss");
        var id = ProposalReviewTests.Save(fixture,
            ProposalReviewTests.Draft([operation], ProposalReviewTests.Provenance("AddPhoneme", operation)));
        return (id, ProposalReview.Read(fixture.Database, fixture.Project, id).Value!.Entries.Single());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
