using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class PhonologyExampleSetRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-phonology-sets-" + Guid.NewGuid().ToString("N"));

    public PhonologyExampleSetRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void CitationEditCreatesRevisionWithoutChangingExecutableDigest()
    {
        using var database = OpenDatabase("citation-edit");
        var repository = new PhonologyExampleSetRepository(database);
        var first = repository.Save("Sound changes", [Example("e1", citation: new CitationView("Source A", "p. 1"))], null, null);

        var second = repository.Save("Sound changes", [Example("e1", citation: new CitationView("Source B", "p. 2"))],
            first.Revision.RevisionId, null);

        Assert.NotEqual(first.Revision.RevisionId, second.Revision.RevisionId);
        Assert.Equal(first.Revision.ExecutableSha256, second.Revision.ExecutableSha256);
        Assert.NotEqual(first.Revision.CitedRevisionSha256, second.Revision.CitedRevisionSha256);
        var oldRevision = repository.Read(first.Revision.RevisionId, 10, null);
        Assert.Equal(new CitationView("Source A", "p. 1"), Assert.Single(oldRevision.Items).Citation);
    }

    [Fact]
    public async Task ConcurrentStaleSavesCommitOneRevisionAndLeaveNoPartialRows()
    {
        using var database = OpenDatabase("concurrent-save");
        var repository = new PhonologyExampleSetRepository(database);
        var first = repository.Save("Set", [Example("before")], null, null);
        using var start = new Barrier(2);

        Task<object> Save(string id) => Task.Run<object>(() =>
        {
            start.SignalAndWait();
            try
            {
                return repository.Save("Set", [Example(id)], first.Revision.RevisionId, null);
            }
            catch (Exception exception)
            {
                return exception;
            }
        });

        var results = await Task.WhenAll(Save("left"), Save("right"));

        Assert.Single(results.OfType<PhonologyExampleSetHeader>());
        Assert.Single(results.OfType<PhonologyExampleSetRevisionConflictException>());
        using var connection = database.OpenConnection();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM PhonologyExampleSetRevisions WHERE SetId = $setId;";
        count.Parameters.AddWithValue("$setId", first.Revision.SetId);
        Assert.Equal(2L, (long)count.ExecuteScalar()!);
        count.CommandText = "SELECT COUNT(*) FROM PhonologyExamples;";
        Assert.Equal(2L, (long)count.ExecuteScalar()!);
    }

    [Fact]
    public void BoundedPagesPreserveAllFiveHundredCitedExamplesAndRejectForeignCursors()
    {
        using var database = OpenDatabase("pages");
        var repository = new PhonologyExampleSetRepository(database);
        var examples = Enumerable.Range(0, PhonologyExampleValidation.MaxExamples)
            .Select(index => Example($"example-{index:D3}", $"underlying-{index:D3}",
                [$"surface-{index:D3}", $"variant-{index:D3}"],
                new CitationView($"Source {index:D3}", $"section {index}")))
            .ToArray();
        var saved = repository.Save("Ordered set", examples, null, null);
        var recovered = new List<PhonologyExample>();
        PhonologyPageCursor? cursor = null;
        do
        {
            var page = repository.Read(saved.Revision.RevisionId, 73, cursor);
            recovered.AddRange(page.Items);
            cursor = page.Continuation;
        } while (cursor is not null);

        Assert.Equal(JsonSerializer.Serialize(examples, PhonologyJson.CreateOptions()),
            JsonSerializer.Serialize(recovered, PhonologyJson.CreateOptions()));
        Assert.Throws<ArgumentException>(() => repository.Read(saved.Revision.RevisionId, 10,
            new PhonologyPageCursor(CanonicalId.Mint("example-revision/").Value, 0)));
    }

    [Fact]
    public void ListCursorUsesProjectIdentityAndListsCurrentHeadersBySetId()
    {
        using var database = OpenDatabase("list-cursor");
        var repository = new PhonologyExampleSetRepository(database);
        var headers = Enumerable.Range(0, 5).Select(index =>
            repository.Save($"Set {index}", [Example($"e{index}")], null, null)).ToArray();
        var expected = headers.OrderBy(header => header.Revision.SetId, StringComparer.Ordinal)
            .Select(header => header.Revision.SetId).ToArray();
        var actual = new List<string>();
        PhonologySetListCursor? cursor = null;
        do
        {
            var page = repository.List(2, cursor);
            actual.AddRange(page.Items.Select(header => header.Revision.SetId));
            cursor = page.Continuation;
        } while (cursor is not null);

        Assert.Equal(expected, actual);
        var otherProject = new ProjectLocator(Path.Combine(_root, "other.fwdata"), "other");
        Assert.Throws<ArgumentException>(() => repository.List(2,
            new PhonologySetListCursor(otherProject.FieldWorksProjectIdentity, expected[0])));
    }

    [Fact]
    public void CurrentRevisionForeignKeyCannotPointAtAnotherSetsRevision()
    {
        using var database = OpenDatabase("composite-pointer");
        var repository = new PhonologyExampleSetRepository(database);
        var first = repository.Save("First", [Example("first")], null, null);
        var second = repository.Save("Second", [Example("second")], null, null);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PhonologyExampleSets SET CurrentRevisionId = $revisionId WHERE SetId = $setId;";
        command.Parameters.AddWithValue("$revisionId", second.Revision.RevisionId);
        command.Parameters.AddWithValue("$setId", first.Revision.SetId);

        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void SavedRevisionsAndExamplesCannotBeMutated()
    {
        using var database = OpenDatabase("immutable-rows");
        var saved = new PhonologyExampleSetRepository(database).Save("Set", [Example("e")], null, null);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PhonologyExampleSetRevisions SET ExampleCount = 2 WHERE RevisionId = $revisionId;";
        command.Parameters.AddWithValue("$revisionId", saved.Revision.RevisionId);
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        command.CommandText = "UPDATE PhonologyExamples SET Underlying = 'changed' WHERE RevisionId = $revisionId;";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        command.CommandText = "UPDATE PhonologyExampleSets SET Name = 'Renamed' WHERE SetId = $setId;";
        command.Parameters.AddWithValue("$setId", saved.Revision.SetId);
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        command.CommandText = """
            INSERT INTO PhonologyExamples
                (RevisionId, Ordinal, ExampleId, Underlying, ExpectedJson, ContextJson, CitationJson)
            VALUES ($revisionId, 1, 'late', 'underlying', '["surface"]',
                '{"kind":"hcXmlEntry","entryId":"entry-1"}', NULL);
            """;
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void ProposalAssociationMustReferenceThisProjectsStore()
    {
        using var database = OpenDatabase("proposal-association");
        var repository = new PhonologyExampleSetRepository(database);
        var proposalId = CanonicalId.Mint("proposal/").Value;
        Assert.Throws<KeyNotFoundException>(() => repository.Save("Missing", [Example("e")], null, proposalId));

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Proposals (ProposalId, Status) VALUES ($proposalId, 'proposed');";
            command.Parameters.AddWithValue("$proposalId", proposalId);
            command.ExecuteNonQuery();
        }

        var saved = repository.Save("Associated", [Example("e")], null, proposalId);
        Assert.Equal(proposalId, saved.ProposalId);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private MotifDatabase OpenDatabase(string name) => MotifDatabase.OpenOwned(
        Path.Combine(_root, name + ".motif.db"),
        new ProjectLocator(Path.Combine(_root, name + ".fwdata"), name), MotifSchema.CurrentSchema, new Version(1, 0));

    private static PhonologyExample Example(string id, string underlying = "underlying",
        IReadOnlyList<string>? expected = null, CitationView? citation = null) =>
        new(id, underlying, expected ?? ["surface"], new PhonologyGenerationContext.HcXmlEntry("entry-1"), citation);
}
