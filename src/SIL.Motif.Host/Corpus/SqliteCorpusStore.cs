using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Host.Corpus;

/// <summary>Indexes Corpus files in the paired Motif database and exports legacy database-held Documents.</summary>
/// <remarks>Published sidecars and files are authoritative for content and metadata; the database retains their index.</remarks>
public sealed class SqliteCorpusStore : ICorpusStore
{
    private readonly MotifDatabase _database;
    private readonly CorpusFileService _files;

    public SqliteCorpusStore(MotifDatabase database)
        : this(database, new CorpusFileService(Path.GetDirectoryName((database ?? throw new ArgumentNullException(nameof(database))).Project.FullFwDataPath)!)) { }

    internal SqliteCorpusStore(MotifDatabase database, CorpusFileService files)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>Exports legacy Documents and only then replaces their database text with file references.</summary>
    public void ExportExisting(string corpusId)
    {
        var corpus = Load(corpusId) ?? throw new InvalidOperationException($"No corpus '{corpusId}'.");
        if (corpus.PublicationRevision is null) Save(corpus);
    }

    /// <summary>Rebuilds the Corpus index from published sidecars and their Documents.</summary>
    public int RebuildIndex()
    {
        var ids = _files.ListPublished();
        foreach (var id in ids) Save(_files.ReadPublished(id));
        return ids.Count;
    }

    public bool Exists(string corpusId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM Corpora WHERE CorpusId = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", corpusId);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Every corpus id, read from a table with no document text in it — never a scan of the bulk data.</summary>
    public IReadOnlyList<string> List()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CorpusId FROM Corpora ORDER BY CorpusId;";

        var ids = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    public void Save(StoredCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var corpusCommand = connection.CreateCommand())
        {
            corpusCommand.Transaction = transaction;
            corpusCommand.CommandText = """
                INSERT INTO Corpora (CorpusId, ProvenanceJson) VALUES ($id, $provenance)
                ON CONFLICT(CorpusId) DO UPDATE SET ProvenanceJson = excluded.ProvenanceJson;
                """;
            corpusCommand.Parameters.AddWithValue("$id", corpus.CorpusId);
            corpusCommand.Parameters.AddWithValue("$provenance", JsonSerializer.Serialize(corpus.Provenance));
            corpusCommand.ExecuteNonQuery();
        }

        bool alreadyPublished;
        using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = "SELECT FileBacked FROM Corpora WHERE CorpusId = $id;";
            state.Parameters.AddWithValue("$id", corpus.CorpusId);
            alreadyPublished = Convert.ToInt32(state.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
        }
        _files.Export(corpus, requireMatchingSidecar: !alreadyPublished && corpus.PublicationRevision is null);
        using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = "UPDATE Corpora SET FileBacked = 1 WHERE CorpusId = $id;";
            mark.Parameters.AddWithValue("$id", corpus.CorpusId);
            mark.ExecuteNonQuery();
        }

        // Save replaces: a document dropped from the corpus must stop being readable, not merely stop being upserted.
        using (var pruneCommand = connection.CreateCommand())
        {
            var keptParameters = corpus.Documents
                .Select((_, i) => "$keep" + i.ToString(CultureInfo.InvariantCulture))
                .ToList();

            pruneCommand.Transaction = transaction;
            pruneCommand.CommandText = keptParameters.Count == 0
                ? "DELETE FROM CorpusDocuments WHERE CorpusId = $corpusId;"
                : $"DELETE FROM CorpusDocuments WHERE CorpusId = $corpusId AND DocumentId NOT IN ({string.Join(", ", keptParameters)});";
            pruneCommand.Parameters.AddWithValue("$corpusId", corpus.CorpusId);
            for (var i = 0; i < corpus.Documents.Count; i++)
                pruneCommand.Parameters.AddWithValue(keptParameters[i], corpus.Documents[i].DocumentId);

            pruneCommand.ExecuteNonQuery();
        }

        for (var i = 0; i < corpus.Documents.Count; i++)
        {
            var document = corpus.Documents[i];
            using var documentCommand = connection.CreateCommand();
            documentCommand.Transaction = transaction;
            documentCommand.CommandText = """
                INSERT INTO CorpusDocuments
                    (CorpusId, DocumentId, OrdinalIndex, Title, Source, Text, ContentSha256, IngestedUtc,
                     Licence, CapabilitiesJson, AttributesJson, FileBacked)
                VALUES
                    ($corpusId, $documentId, $ordinal, $title, $source, $text, $sha256, $ingestedUtc,
                     $licence, $capabilities, $attributes, 1)
                ON CONFLICT(CorpusId, DocumentId) DO UPDATE SET
                    OrdinalIndex = excluded.OrdinalIndex, Title = excluded.Title, Source = excluded.Source,
                    Text = excluded.Text, ContentSha256 = excluded.ContentSha256,
                    IngestedUtc = excluded.IngestedUtc, Licence = excluded.Licence,
                    CapabilitiesJson = excluded.CapabilitiesJson, AttributesJson = excluded.AttributesJson,
                    FileBacked = excluded.FileBacked;
                """;
            documentCommand.Parameters.AddWithValue("$corpusId", corpus.CorpusId);
            documentCommand.Parameters.AddWithValue("$documentId", document.DocumentId);
            documentCommand.Parameters.AddWithValue("$ordinal", i);
            documentCommand.Parameters.AddWithValue("$title", document.Title);
            documentCommand.Parameters.AddWithValue("$source", document.Source.Describe());
            documentCommand.Parameters.AddWithValue("$text", "");
            documentCommand.Parameters.AddWithValue("$sha256", document.ContentSha256);
            documentCommand.Parameters.AddWithValue("$ingestedUtc", document.IngestedUtc.ToString("O", CultureInfo.InvariantCulture));
            documentCommand.Parameters.AddWithValue("$licence", (object?)document.Licence ?? DBNull.Value);
            documentCommand.Parameters.AddWithValue(
                "$capabilities", document.Capabilities is null ? DBNull.Value : JsonSerializer.Serialize(document.Capabilities));
            documentCommand.Parameters.AddWithValue(
                "$attributes", document.Attributes is null ? DBNull.Value : JsonSerializer.Serialize(document.Attributes));
            documentCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public StoredCorpus? Load(string corpusId)
    {
        using var connection = _database.OpenConnection();

        string provenanceJson;
        bool fileBacked;
        using (var corpusCommand = connection.CreateCommand())
        {
            corpusCommand.CommandText = "SELECT ProvenanceJson, FileBacked FROM Corpora WHERE CorpusId = $id;";
            corpusCommand.Parameters.AddWithValue("$id", corpusId);
            using var reader = corpusCommand.ExecuteReader();
            if (!reader.Read()) return null;
            provenanceJson = reader.GetString(0);
            fileBacked = reader.GetInt32(1) != 0;
        }

        var provenance = JsonSerializer.Deserialize<CorpusProvenance>(provenanceJson)!;
        if (fileBacked)
        {
            if (!_files.Exists(corpusId)) throw new FileNotFoundException($"The sidecar for Corpus '{corpusId}' is missing.");
            return _files.ReadPublished(corpusId);
        }
        var documents = new List<CorpusDocument>();

        using (var documentsCommand = connection.CreateCommand())
        {
            documentsCommand.CommandText = """
                SELECT DocumentId, Title, Source, Text, ContentSha256, IngestedUtc, Licence, CapabilitiesJson, AttributesJson, FileBacked
                FROM CorpusDocuments WHERE CorpusId = $id ORDER BY OrdinalIndex;
                """;
            documentsCommand.Parameters.AddWithValue("$id", corpusId);
            using var reader = documentsCommand.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt32(9) != 0)
                    throw new FileNotFoundException($"The sidecar for Corpus '{corpusId}' is missing.");
                documents.Add(new CorpusDocument(
                    DocumentId: reader.GetString(0),
                    Title: reader.GetString(1),
                    Source: DocumentSource.Parse(reader.GetString(2)),
                    Text: reader.GetString(3),
                    ContentSha256: reader.GetString(4),
                    IngestedUtc: DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    Licence: reader.IsDBNull(6) ? null : reader.GetString(6),
                    Capabilities: reader.IsDBNull(7) ? null : JsonSerializer.Deserialize<LicenceCapabilities>(reader.GetString(7)),
                    Attributes: reader.IsDBNull(8)
                        ? null
                        : JsonSerializer.Deserialize<IReadOnlyDictionary<string, string>>(reader.GetString(8))));
            }
        }

        return new StoredCorpus(corpusId, provenance, documents);
    }
}
