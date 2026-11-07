using System;
using System.IO;
using System.Linq;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Projects;
using Xunit;

namespace SIL.Motif.Tests.Store;

/// <summary>
/// <see cref="SqliteCorpusStore"/> round-trips a Corpus through the embedded database ADR 0036 decision 6
/// assigns it to, and its aggregate reads (<see cref="ICorpusStore.List"/>, <see cref="ICorpusStore.Exists"/>)
/// touch a table that carries no document text at all.
/// </summary>
public sealed class SqliteCorpusStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-sqlite-corpus-tests", Guid.NewGuid().ToString("N"));

    private MotifDatabase? _database;

    // The Corpus tables only ever exist inside a project database, so a store needs one to have been opened.
    private MotifDatabase Database => _database ??= MotifDatabase.OpenOwned(
        Path.Combine(_root, "motif.db"),
        new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project"),
        MotifSchema.CurrentSchema,
        new Version(1, 0));

    public void Dispose()
    {
        _database?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private SqliteCorpusStore Store() => new(Database);

    private static CorpusProvenance Provenance() => new(
        new CorpusOrigin(
            Description: "eBible, Testlang",
            Uri: "https://github.com/BibleNLP/ebible",
            RetrievedUtc: new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            Licence: "CC-BY-SA-4.0",
            Capabilities: new LicenceCapabilities(true, true, false, true, "eBible licences.tsv")),
        new TokenisationRecord("whitespace-and-punctuation", "1", "Splits on whitespace."),
        new CorpusQualification(true, true, "A. Linguist", new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero), "Hand-checked."));

    private static CorpusDocument Document(string id, string text) => new(
        DocumentId: id,
        Title: "Testlang " + id,
        Source: new DocumentSource.File(@"C:\incoming\" + id + ".txt"),
        Text: text,
        ContentSha256: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
        IngestedUtc: new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero),
        Licence: "CC-BY-SA-4.0",
        Capabilities: new LicenceCapabilities(true, false, false, true, "eBible licences.tsv"),
        Attributes: new System.Collections.Generic.Dictionary<string, string> { ["isoCode"] = "tst" });

    [Fact]
    public void ReindexRepairsDatabaseAfterPublicationBeforeTransactionCommit()
    {
        var files = new CorpusFileService(_root);
        var store = new SqliteCorpusStore(Database, files);
        store.Save(StoredCorpus.Create("c", Provenance()).With(Document("d", "text")));
        var snapshot = store.Load("c")!;
        files.AfterSidecarPublication = (_, _) => throw new IOException("Simulated termination before database commit");
        Assert.Throws<IOException>(() => store.Save(snapshot with
        {
            Documents = new[] { snapshot.Documents.Single() with { Title = "New Title" } }
        }));
        files.AfterSidecarPublication = null;
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Title FROM CorpusDocuments WHERE CorpusId='c';";
            Assert.Equal("Testlang d", command.ExecuteScalar());
        }
        Assert.Equal("New Title", store.Load("c")!.Documents.Single().Title);
        Assert.Equal(1, store.RebuildIndex());
        using var verified = Database.OpenConnection();
        using var query = verified.CreateCommand();
        query.CommandText = "SELECT Title FROM CorpusDocuments WHERE CorpusId='c';";
        Assert.Equal("New Title", query.ExecuteScalar());
    }

    [Fact]
    public void StaleCorpusCannotReplaceDocumentsAddedByAnotherWriter()
    {
        var store = Store();
        store.Save(StoredCorpus.Create("c", Provenance()));
        var first = store.Load("c")!;
        var stale = store.Load("c")!;
        store.Save(first.With(Document("first", "one")));
        Assert.Throws<IOException>(() => store.Save(stale.With(Document("second", "two"))));
        Assert.Equal(new[] { "first" }, store.Load("c")!.Documents.Select(d => d.DocumentId));
    }

    [Fact]
    public void MissingSidecarIsVisibleEvenForAnEmptyCorpus()
    {
        var store = Store();
        store.Save(StoredCorpus.Create("empty", Provenance()));
        File.Delete(Directory.GetFiles(Path.Combine(_root, "corpora"), "corpus.json", SearchOption.AllDirectories).Single());
        Assert.Throws<FileNotFoundException>(() => store.Load("empty"));
    }

    [Fact]
    public void PublishedSidecarsRebuildAnEmptyIndexWithoutDatabaseText()
    {
        var store = Store();
        store.Save(StoredCorpus.Create("source", Provenance()).With(Document("one", "source text")));
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM CorpusDocuments; DELETE FROM Corpora;";
            command.ExecuteNonQuery();
        }
        Assert.Equal(1, store.RebuildIndex());
        Assert.Equal("source text", store.Load("source")!.Documents.Single().Text);
        Assert.Equal("isoCode", store.Load("source")!.Documents.Single().Attributes!.Keys.Single());
    }

    [Fact]
    public void OpeningAnAlreadyPublishedCorpusDoesNotRewriteItsSidecar()
    {
        var store = Store();
        store.Save(StoredCorpus.Create("published", Provenance()).With(Document("d", "text")));
        var path = Directory.GetFiles(Path.Combine(_root, "corpora"), "corpus.json", SearchOption.AllDirectories).Single();
        var timestamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        store.ExportExisting("published");
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup"));
    }

    [Fact]
    public void DamagedSidecarDoesNotRetireLegacyDatabaseText()
    {
        var store = Store();
        store.Save(StoredCorpus.Create("legacy", Provenance()).With(Document("d", "old text")));
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE CorpusDocuments SET Text='old text', FileBacked=0 WHERE CorpusId='legacy'; " +
                "UPDATE Corpora SET FileBacked=0 WHERE CorpusId='legacy';";
            command.ExecuteNonQuery();
        }
        var path = Directory.GetFiles(Path.Combine(_root, "corpora"), "corpus.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(path, "{");
        Assert.Throws<InvalidDataException>(() => store.ExportExisting("legacy"));
        Assert.Equal("old text", store.Load("legacy")!.Documents.Single().Text);
        Assert.Equal("{", File.ReadAllText(path));
    }

    [Fact]
    public void ConflictingSidecarCannotReplaceLegacyDatabaseText()
    {
        var store = Store();
        store.Save(StoredCorpus.Create("legacy", Provenance()).With(Document("d", "old text")));
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE CorpusDocuments SET Text='old text', FileBacked=0 WHERE CorpusId='legacy'; " +
                "UPDATE Corpora SET FileBacked=0 WHERE CorpusId='legacy';";
            command.ExecuteNonQuery();
        }
        var path = Directory.GetFiles(Path.Combine(_root, "corpora"), "corpus.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(path, File.ReadAllText(path).Replace("Testlang d", "Changed Title", StringComparison.Ordinal));
        Assert.Throws<IOException>(() => store.ExportExisting("legacy"));
        Assert.Equal("old text", store.Load("legacy")!.Documents.Single().Text);
        Assert.Equal("Testlang d", store.Load("legacy")!.Documents.Single().Title);
    }

    [Fact]
    public void ExportExistingPreservesLegacyTextAndReadsLaterFileEdits()
    {
        var store = Store();
        store.Save(StoredCorpus.Create("legacy", Provenance()).With(Document("d", "old text")));
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE CorpusDocuments SET Text='old text', FileBacked=0 WHERE CorpusId='legacy'; " +
                "UPDATE Corpora SET FileBacked=0 WHERE CorpusId='legacy';";
            command.ExecuteNonQuery();
        }
        Directory.Delete(Path.Combine(_root, "corpora"), recursive: true);

        store.ExportExisting("legacy");
        store.ExportExisting("legacy");
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Text FROM CorpusDocuments WHERE CorpusId='legacy';";
            Assert.Equal("", command.ExecuteScalar());
        }
        var path = Directory.GetFiles(Path.Combine(_root, "corpora"), "*.txt", SearchOption.AllDirectories).Single();
        Assert.Equal("old text", File.ReadAllText(path));
        File.WriteAllText(path, "edited text");
        Assert.Equal("edited text", store.Load("legacy")!.Documents.Single().Text);
        File.Delete(path);
        Assert.Throws<FileNotFoundException>(() => store.Load("legacy"));
    }

    [Fact]
    public void ACorpusRoundTripsThroughTheDatabase_EveryFieldIntact()
    {
        var store = Store();
        var corpus = StoredCorpus.Create("ebible-tst", Provenance())
            .With(Document("d1", "mbali mbali nyumba\nmbali\n"))
            .With(Document("d2", "nyumba kubwa\n"));

        store.Save(corpus);
        var loaded = store.Load("ebible-tst");

        Assert.NotNull(loaded);
        Assert.Equal(corpus.CorpusId, loaded!.CorpusId);
        Assert.Equal(corpus.Provenance.Origin.Description, loaded.Provenance.Origin.Description);
        Assert.Equal(corpus.Provenance.Origin.Capabilities!.Basis, loaded.Provenance.Origin.Capabilities!.Basis);
        Assert.Equal(corpus.Provenance.Qualification!.Attestor, loaded.Provenance.Qualification!.Attestor);
        Assert.True(loaded.Provenance.SupportsAccuracyClaims);

        Assert.Equal(2, loaded.Documents.Count);
        Assert.Equal(new[] { "d1", "d2" }, loaded.Documents.Select(d => d.DocumentId));

        // Order and repetition of the running text must survive storage: deduplicating would destroy both.
        Assert.Equal("mbali mbali nyumba\nmbali\n", loaded.Documents[0].Text);
        Assert.Equal(3, loaded.Documents[0].Text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Count(w => w == "mbali"));

        Assert.Equal(corpus.Documents[0].ContentSha256, loaded.Documents[0].ContentSha256);
        Assert.Equal(corpus.Documents[0].IngestedUtc, loaded.Documents[0].IngestedUtc);
        Assert.Equal("tst", loaded.Documents[0].Attributes!["isoCode"]);
        Assert.False(loaded.Documents[0].EffectiveCapabilities(loaded.Provenance.Origin).MayDerive);
    }

    [Fact]
    public void ListingAndExistenceReadOnlyTheCorporaTable_NeverTheDocumentText()
    {
        var store = Store();
        // A half-megabyte document: List()/Exists() must stay cheap without reading CorpusDocuments at all.
        var corpus = StoredCorpus.Create("c", Provenance()).With(Document("d", new string('x', 500_000)));
        store.Save(corpus);

        Assert.Equal(new[] { "c" }, store.List());
        Assert.True(store.Exists("c"));
        Assert.False(store.Exists("no-such-corpus"));
    }

    [Fact]
    public void LoadingAMissingCorpusReturnsNull()
    {
        Assert.Null(Store().Load("nope"));
    }

    [Fact]
    public void SavingAgainReplacesRatherThanDuplicates()
    {
        var store = Store();
        var corpus = StoredCorpus.Create("c", Provenance()).With(Document("d1", "one\n"));
        store.Save(corpus);

        var extended = corpus.With(Document("d2", "two\n"));
        store.Save(extended);

        var loaded = store.Load("c")!;
        Assert.Equal(new[] { "d1", "d2" }, loaded.Documents.Select(d => d.DocumentId));
        Assert.Equal(new[] { "c" }, store.List());
    }

    /// <summary>
    /// A document is removed from a corpus when its attribution or licence turns out to be wrong, so
    /// "replacing what is there" has to mean its text stops being readable — not merely that it stops
    /// being written. Adding a document proves nothing about that, which is why this is separate.
    /// </summary>
    [Fact]
    public void SavingWithoutADocumentRemovesIt_RatherThanLeavingItReadable()
    {
        var store = Store();
        var withBoth = StoredCorpus.Create("c", Provenance())
            .With(Document("keep", "kept text\n"))
            .With(Document("withdrawn", "text that must stop being readable\n"));
        store.Save(withBoth);

        var withoutWithdrawn = StoredCorpus.Create("c", Provenance()).With(Document("keep", "kept text\n"));
        store.Save(withoutWithdrawn);

        var loaded = store.Load("c")!;
        Assert.Equal(new[] { "keep" }, loaded.Documents.Select(d => d.DocumentId));
        Assert.DoesNotContain(loaded.Documents, d => d.Text.Contains("must stop being readable"));
    }
}
