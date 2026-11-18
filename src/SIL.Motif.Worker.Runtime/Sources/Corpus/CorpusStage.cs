using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Worker.Sources.Adapter;

namespace SIL.Motif.Worker.Sources;

internal sealed class CorpusStage : IDisposable
{
    private const int BatchSize = 4096;
    private readonly SqliteConnection _connection;
    private readonly CorpusSourceInput _input;
    private readonly IWordSpanTokeniser _tokeniser;
    private readonly bool _repairPublicForms;
    private readonly Dictionary<string, (long Id, string Nfd, string Key)> _forms = new(StringComparer.Ordinal);
    private readonly SqliteCommand _place;
    private readonly SqliteCommand _form;
    private SqliteTransaction? _transaction;
    private int _pending;
    private int _ordinal;

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public CorpusStage(SqliteConnection connection, string sourceId, CorpusSourceInput input, IWordSpanTokeniser tokeniser,
        bool repairPublicForms)
    {
        _connection = connection;
        _input = input;
        _tokeniser = tokeniser;
        _repairPublicForms = repairPublicForms && input.Sikor is null;
        using var create = CorpusSql.Command(connection, null, """
            INSERT INTO IndexedCorpusGenerations VALUES ($id,$source,$binding,'[]','',0,0,0,0,'',1);
            """, ("$id", Id), ("$source", sourceId), ("$binding", JsonSerializer.Serialize(input.Binding)));
        create.ExecuteNonQuery();
        _place = Prepared("""
            INSERT INTO IndexedCorpusOccurrences VALUES ($shard,$ordinal,$form,$segment,$word,$offset,$length,$verse,$surface);
            """, "$shard", "$ordinal", "$form", "$segment", "$word", "$offset", "$length", "$verse", "$surface");
        _form = Prepared("""
            INSERT INTO IndexedCorpusForms (FormKey,WritingSystem,FormNfd) VALUES ($key,$ws,$nfd)
            ON CONFLICT (FormKey) DO UPDATE SET FormKey=excluded.FormKey
            WHERE WritingSystem=excluded.WritingSystem AND FormNfd IS excluded.FormNfd
            RETURNING Id;
            """, "$key", "$ws", "$nfd");
    }

    private SqliteCommand Prepared(string sql, params string[] parameters) =>
        CorpusSql.Command(_connection, null, sql, parameters.Select(name => (name, (object?)null)).ToArray());

    public CorpusDocumentVersion Add(CorpusInputDocument document, CancellationToken cancellationToken)
    {
        using var create = CorpusSql.Command(_connection, Transaction(), """
            INSERT INTO IndexedCorpusShards (StageGeneration,Digest) VALUES ($generation,'') RETURNING Id;
            """, ("$generation", Id));
        var shard = (long)create.ExecuteScalar()!;
        using var digest = new CorpusShardDigest();
        long ordinal = 0;
        var verseOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var segment in document.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var word = 0;
            foreach (var span in _tokeniser.TokeniseSpans(segment.Text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segment.Verse is { } verse)
                {
                    word = verseOrdinals.GetValueOrDefault(verse) + 1;
                    verseOrdinals[verse] = word;
                }
                else word++;
                var form = Form(span.Surface);
                var surface = _input.Sikor is null && span.Surface != form.Nfd ? span.Surface : null;
                var hasBytes = _input.Sikor is null && segment.Verse is null;
                long? offset = hasBytes ? segment.ByteOffset + span.Utf8Start : null;
                int? length = hasBytes ? span.Utf8Length : null;
                Run(_place, shard, ++ordinal, form.Id, segment.Number, word, offset, length, segment.Verse, surface);
                digest.Append(ordinal, form.Id, form.Key, segment.Number, word, offset, length, segment.Verse, surface);
                Advance();
            }
        }
        using var seal = CorpusSql.Command(_connection, Transaction(), """
            UPDATE IndexedCorpusShards SET Digest=$digest WHERE Id=$shard;
            """, ("$digest", digest.Complete()), ("$shard", shard));
        seal.ExecuteNonQuery();
        var version = document.Version with { ShardId = shard, Tokens = ordinal };
        AddDocument(version);
        return version;
    }

    public void AddDocument(CorpusDocumentVersion version)
    {
        using var command = CorpusSql.Command(_connection, Transaction(), """
            INSERT INTO IndexedCorpusDocuments
            VALUES ($generation,$ordinal,$id,$container,$position,$raw,$text,$metadata,$shard,$tokens);
            """, ("$generation", Id), ("$ordinal", ++_ordinal), ("$id", version.Id), ("$container", version.Container),
            ("$position", version.Position), ("$raw", version.RawHash), ("$text", version.TextHash),
            ("$metadata", version.Metadata), ("$shard", version.ShardId), ("$tokens", version.Tokens));
        command.ExecuteNonQuery();
        Advance();
    }

    private (long Id, string Nfd, string Key) Form(string surface)
    {
        if (_forms.TryGetValue(surface, out var form)) return form;
        var nfd = surface.IsNormalized(NormalizationForm.FormD) ? surface : surface.Normalize(NormalizationForm.FormD);
        var writingSystem = _input.Binding.WritingSystem;
        var key = _input.Sikor is { } privateInput
            ? privateInput.Protect("form", CorpusContent.Digest(new[] { writingSystem, nfd }))
            : CorpusContent.Digest(new WordformKey(writingSystem, nfd));
        Assign(_form, key, writingSystem, _input.Sikor is null ? nfd : null);
        var id = _form.ExecuteScalar() as long?;
        if (id is null && _repairPublicForms) id = RepairPublicForm(key, writingSystem, nfd);
        if (id is null) throw new InvalidDataException("A Corpus Wordform key collision was found.");
        if (_forms.Count >= 100_000) _forms.Clear();
        form = (id.Value, nfd, key);
        _forms.Add(surface, form);
        return form;
    }

    private long? RepairPublicForm(string key, string writingSystem, string nfd)
    {
        var transaction = Transaction();
        using var lookup = CorpusSql.Command(_connection, transaction, """
            SELECT Id,WritingSystem,FormNfd FROM IndexedCorpusForms WHERE FormKey=$key;
            """, ("$key", key));
        long id;
        using (var row = lookup.ExecuteReader())
        {
            if (!row.Read() || row.IsDBNull(2) || CorpusContent.Digest(new WordformKey(row.GetString(1), row.GetString(2))) == key)
                return null;
            id = row.GetInt64(0);
        }

        CorpusProjectionVerification.Invalidate(_connection, transaction, null, [], [id]);
        using var repair = CorpusSql.Command(_connection, transaction, """
            UPDATE IndexedCorpusForms SET WritingSystem=$ws,FormNfd=$nfd WHERE Id=$id;
            """, ("$ws", writingSystem), ("$nfd", nfd), ("$id", id));
        repair.ExecuteNonQuery();
        return id;
    }

    private SqliteTransaction Transaction() => _transaction ??= _connection.BeginTransaction();

    private void Run(SqliteCommand command, params object?[] values)
    {
        Assign(command, values);
        command.ExecuteNonQuery();
    }

    private void Assign(SqliteCommand command, params object?[] values)
    {
        command.Transaction = Transaction();
        for (var i = 0; i < values.Length; i++)
            command.Parameters[i].Value = values[i] ?? DBNull.Value;
    }

    private void Advance()
    {
        if (++_pending >= BatchSize) Commit();
    }

    private void Commit()
    {
        if (_transaction is null) return;
        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;
        _pending = 0;
    }

    public void Seal(CorpusFileVersion[] inventory, string digest, string? previousGeneration,
        CancellationToken cancellationToken)
    {
        Commit();
        cancellationToken.ThrowIfCancellationRequested();
        using var transaction = _connection.BeginTransaction();
        using (var shards = CorpusSql.Command(_connection, transaction, """
            INSERT INTO IndexedCorpusShardWeights
            SELECT s.Id,o.FormId,COUNT(*)
            FROM IndexedCorpusShards s JOIN IndexedCorpusOccurrences o ON o.ShardId=s.Id
            WHERE s.StageGeneration=$id GROUP BY s.Id,o.FormId;
            """, ("$id", Id)))
            shards.ExecuteNonQuery();
        using (var weights = CorpusSql.Command(_connection, transaction,
            previousGeneration is null ? AllWeights : ChangedWeights, ("$id", Id), ("$old", previousGeneration)))
            weights.ExecuteNonQuery();
        using var command = CorpusSql.Command(_connection, transaction, """
            UPDATE IndexedCorpusGenerations SET InventoryJson=$inventory, DocumentDigest=$digest,DocumentCount=$documents,
                TypeCount=(SELECT COUNT(*) FROM IndexedCorpusWeights WHERE Generation=$id),
                TokenCount=(SELECT COALESCE(SUM(Frequency),0) FROM IndexedCorpusWeights WHERE Generation=$id)
            WHERE Id=$id;
            """, ("$id", Id), ("$inventory", JsonSerializer.Serialize(inventory)), ("$digest", digest), ("$documents", _ordinal));
        command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private const string AllWeights = """
        INSERT INTO IndexedCorpusWeights
        SELECT $id,w.FormId,SUM(w.Frequency)
        FROM IndexedCorpusDocuments d JOIN IndexedCorpusShardWeights w ON w.ShardId=d.ShardId
        WHERE d.Generation=$id GROUP BY w.FormId;
        """;

    private const string ChangedWeights = """
        WITH differences AS (
            SELECT ShardId,1 AS Sign FROM IndexedCorpusDocuments d WHERE Generation=$id AND NOT EXISTS
                (SELECT 1 FROM IndexedCorpusDocuments p WHERE p.Generation=$old AND p.ShardId=d.ShardId)
            UNION ALL
            SELECT ShardId,-1 FROM IndexedCorpusDocuments d WHERE Generation=$old AND NOT EXISTS
                (SELECT 1 FROM IndexedCorpusDocuments p WHERE p.Generation=$id AND p.ShardId=d.ShardId)
        ), weights AS (
            SELECT FormId,Frequency FROM IndexedCorpusWeights WHERE Generation=$old
            UNION ALL
            SELECT w.FormId,d.Sign*w.Frequency
            FROM differences d JOIN IndexedCorpusShardWeights w ON w.ShardId=d.ShardId
        )
        INSERT INTO IndexedCorpusWeights
        SELECT $id,FormId,SUM(Frequency) FROM weights GROUP BY FormId HAVING SUM(Frequency) != 0;
        """;

    public void Dispose()
    {
        _place.Dispose();
        _form.Dispose();
        _transaction?.Dispose();
    }
}

internal static class CorpusSql
{
    public static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static string? OptionalString(this SqliteDataReader row, int index) =>
        row.IsDBNull(index) ? null : row.GetString(index);
}
