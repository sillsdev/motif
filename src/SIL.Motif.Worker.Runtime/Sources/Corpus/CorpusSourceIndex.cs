using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Machine.Corpora;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Sources.Adapter;

namespace SIL.Motif.Worker.Sources;

internal enum CorpusFreshness
{
    Verified,
    Changed,
    InvalidProjection,
    Historical,
    Unavailable,
}

internal sealed record CorpusRefreshResult(CorpusStamp Stamp, int Files, int Documents, long Types, long Tokens,
    int ShardsBuilt, int ShardsReused, TimeSpan Scan, TimeSpan Stage, TimeSpan Publish);

internal sealed record CorpusVerification(CorpusFreshness Freshness, TimeSpan Scan, TimeSpan Projection, string? Reason = null);

internal sealed class CorpusSourceIndex(MotifDatabase database)
{
    public CorpusSourceSnapshot? Pin(string sourceId, string? exactGeneration = null)
    {
        var source = Key(sourceId);
        using var connection = database.OpenConnection();
        using var command = CorpusSql.Command(connection, null, """
            SELECT g.Id,g.BindingJson,g.InventoryJson,g.DocumentDigest,g.DocumentCount,g.TypeCount,g.TokenCount,COALESCE(h.CheckedUtc,g.PublishedUtc),h.Freshness,g.ProjectionValid
            FROM IndexedCorpusGenerations g
            LEFT JOIN IndexedCorpusHeads h ON h.SourceId=g.SourceId AND h.Generation=g.Id
            WHERE g.Id=COALESCE($exact,(SELECT Generation FROM IndexedCorpusHeads WHERE SourceId=$source))
                AND g.SourceId=$source AND g.Published=1;
            """, ("$source", source.SourceId), ("$exact", exactGeneration));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var binding = JsonSerializer.Deserialize<CorpusSourceBinding>(reader.GetString(1))!;
        var inventory = JsonSerializer.Deserialize<CorpusFileVersion[]>(reader.GetString(2))!;
        return new(database, new(source, reader.GetString(0), CorpusContent.Digest(inventory),
                reader.GetString(3), binding.Segmentation, binding.ProviderVersion), binding, inventory,
            reader.GetInt32(4), reader.GetInt64(5), reader.GetInt64(6),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            CorpusSourceSnapshot.ReadStoredStatus(reader, 9, 8),
            exactGeneration is not null);
    }

    public static MotifDatabase OpenExisting(string databasePath, ProjectLocator project) =>
        MotifDatabase.OpenExistingReadOnly(databasePath, project);

    public CorpusRefreshResult Refresh(CorpusSourceDeclaration declaration, IWordTokeniser tokeniser,
        CancellationToken cancellationToken = default) => Build(declaration, tokeniser, false, cancellationToken);

    public CorpusRefreshResult Rebuild(CorpusSourceDeclaration declaration, IWordTokeniser tokeniser,
        CancellationToken cancellationToken = default) => Build(declaration, tokeniser, true, cancellationToken);

    internal Action? BeforePublish { get; set; }
    internal Action? AfterHeadCommit { get; set; }
    internal Action<string>? AfterRecheckFile { get; set; }

    private CorpusRefreshResult Build(CorpusSourceDeclaration declaration, IWordTokeniser tokeniser, bool rebuild,
        CancellationToken cancellationToken)
    {
        var key = Key(declaration.SourceId);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = AcquireBuildLease();
        using var connection = database.OpenConnection();
        Cleanup(connection);
        var previous = rebuild ? null : Pin(key.SourceId);
        var head = rebuild ? PrepareRebuildHead(connection, key.SourceId) : previous?.Stamp.Id;
        var basisChanged = false;

        try
        {
            var input = new CorpusSourceInput(declaration, key.ProjectId);
            basisChanged = previous is not null && previous.Binding != input.Binding;
            var spans = declaration.Segmentation.Bind(tokeniser);
            var reuse = previous?.Binding == input.Binding &&
                previous.Freshness != CorpusFreshness.InvalidProjection;
            var scan = Stopwatch.StartNew();
            var names = input.Sikor is null ? input.FileNames() : [];
            var previousFiles = input.Sikor is null ? previous?.Inventory : null;
            if (previousFiles is not null)
                basisChanged |= !names.SequenceEqual(previousFiles.Select(file => file.Name));

            void RecordFileChange(CorpusFileVersion file, int index)
            {
                if (!basisChanged && previousFiles is not null)
                    basisChanged = previousFiles[index] != file;
            }

            var probe = reuse && input.Sikor is null ? input.Recheck(names, cancellationToken, RecordFileChange) : null;
            scan.Stop();

            if (probe is not null && !basisChanged)
            {
                var guard = () => input.EnsureUnchanged(probe.Observations, cancellationToken);
                MarkChecked(connection, key.SourceId, head!, CorpusFreshness.Changed, guard);
                ConfirmHead(connection, key.SourceId, head!, guard);
                return new(previous!.Stamp, probe.Versions.Count(CorpusSourceInput.IsDocumentFile), previous.DocumentCount,
                    previous.TypeCount, previous.TokenCount, 0, previous.DocumentCount, scan.Elapsed, TimeSpan.Zero, TimeSpan.Zero);
            }

            var scanBeforeStage = scan.Elapsed;
            var stageTime = Stopwatch.StartNew();
            var deltaBase = reuse ? CorpusProjectionVerification.ReadDeltaBase(connection, previous!, cancellationToken) : null;
            reuse = deltaBase is not null;
            var previousDocuments = deltaBase ?? [];
            var byId = previousDocuments.ToDictionary(document => document.Id, StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var bookFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var manifest = new List<CorpusDocumentVersion>();
            var built = 0;
            using var stage = new CorpusStage(connection, key.SourceId, input, spans, rebuild);
            void Add(CorpusDocumentVersion version)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ids.Add(version.Id))
                    throw new InvalidDataException(input.Sikor is null
                        ? $"Duplicate Corpus Document id '{version.Id}'."
                        : "Duplicate authorized SIKOR Document identity.");
                if (input.Binding.Format != CorpusSourceFormat.Usfm) return;
                var book = version.Id[..version.Id.IndexOf(' ')];
                if (!bookFiles.TryAdd(book, version.Container) && bookFiles[book] != version.Container)
                    throw new InvalidDataException($"USFM book '{book}' appears in more than one Source file.");
            }

            void Stage(IEnumerable<CorpusInputDocument> documents)
            {
                foreach (var document in documents)
                {
                    var version = document.Version;
                    byId.TryGetValue(version.Id, out var old);
                    if (input.Sikor is not null && reuse && !SameSikorDocument(old, version))
                        basisChanged = true;
                    Add(version);
                    if (old is not null && old.TextHash == version.TextHash)
                    {
                        var reused = version with { ShardId = old.ShardId, Tokens = old.Tokens };
                        stage.AddDocument(reused);
                        manifest.Add(reused);
                    }
                    else
                    {
                        manifest.Add(stage.Add(document, cancellationToken));
                        built++;
                    }
                }
            }

            CorpusFileVersion[] inventory;
            if (input.Sikor is not null)
            {
                Stage(input.ReadSikor(cancellationToken));
                inventory = SikorInventory(input, manifest);
            }
            else
            {
                var oldFiles = reuse ? previous!.Inventory.ToDictionary(file => file.Name, StringComparer.Ordinal) : [];
                var oldByFile = previousDocuments.ToLookup(document => document.Container, StringComparer.Ordinal);
                inventory = probe?.Versions ?? new CorpusFileVersion[names.Length];
                var firstDocument = input.HasStylesheet ? 1 : 0;
                if (input.HasStylesheet && probe is not null &&
                    oldFiles.GetValueOrDefault(names[0]) != inventory[0])
                {
                    oldFiles.Clear();
                    byId.Clear();
                }
                CorpusCapturedFile Capture(int index)
                {
                    scan.Start();
                    var file = input.Capture(names[index], cancellationToken);
                    scan.Stop();
                    RecordFileChange(file.Version, index);
                    if (probe is not null && inventory[index] != file.Version) throw CorpusSourceInput.Changed();
                    inventory[index] = file.Version;
                    return file;
                }

                var styles = new Lazy<UsfmStylesheet>(() =>
                    ParatextPassages.ReadStylesheet(input.HasStylesheet ? Capture(0).Bytes : null));
                for (var i = firstDocument; i < names.Length; i++)
                {
                    if (probe is null || oldFiles.GetValueOrDefault(names[i]) != inventory[i])
                    {
                        Stage(input.Read(Capture(i), declaration.Format == CorpusSourceFormat.Usfm ? styles.Value : null,
                            cancellationToken));
                        continue;
                    }
                    foreach (var document in oldByFile[names[i]])
                    {
                        Add(document);
                        stage.AddDocument(document);
                        manifest.Add(document);
                    }
                }
            }
            basisChanged |= previous is not null && !previous.Inventory.SequenceEqual(inventory);
            var documentDigest = DocumentDigest(manifest);
            stage.Seal(inventory, documentDigest, reuse ? previous!.Stamp.Id : null, cancellationToken);
            stageTime.Stop();
            var stagingElapsed = stageTime.Elapsed - (scan.Elapsed - scanBeforeStage);
            BeforePublish?.Invoke();

            scan.Start();
            var (current, guardPublication) = InspectInput(input, cancellationToken, AfterRecheckFile, inventory, manifest);
            scan.Stop();
            if (!inventory.SequenceEqual(current)) throw CorpusSourceInput.Changed();
            cancellationToken.ThrowIfCancellationRequested();

            var publish = Stopwatch.StartNew();
            if (reuse && previous!.Stamp.DocumentDigest == documentDigest && previous.Inventory.SequenceEqual(inventory))
            {
                MarkChecked(connection, key.SourceId, head!, CorpusFreshness.Changed, guardPublication);
                ConfirmHead(connection, key.SourceId, head!, guardPublication);
                Cleanup(connection);
                return new(previous.Stamp, inventory.Count(CorpusSourceInput.IsDocumentFile), manifest.Count,
                    previous.TypeCount, previous.TokenCount, built, manifest.Count - built, scan.Elapsed, stagingElapsed,
                    publish.Elapsed);
            }

            Publish(connection, key.SourceId, stage.Id, head, guardPublication);
            head = stage.Id;
            ConfirmHead(connection, key.SourceId, head, guardPublication);
            var pinned = Pin(key.SourceId, head)!;
            return new(pinned.Stamp, inventory.Count(CorpusSourceInput.IsDocumentFile), manifest.Count,
                pinned.TypeCount, pinned.TokenCount, built, manifest.Count - built, scan.Elapsed, stagingElapsed,
                publish.Elapsed);
        }
        catch (Exception exception)
        {
            if (head is not null && (exception is not OperationCanceledException || basisChanged))
            {
                var freshness = InputFailure(exception) ?? (basisChanged ? CorpusFreshness.Changed : CorpusFreshness.Unavailable);
                MarkChecked(connection, key.SourceId, head, freshness);
            }
            Cleanup(connection);
            throw;
        }
    }

    public CorpusVerification Verify(CorpusSourceDeclaration declaration, CancellationToken cancellationToken = default)
    {
        var key = Key(declaration.SourceId);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = AcquireBuildLease();
        var snapshot = Pin(key.SourceId) ?? throw new InvalidOperationException("The Corpus Source has no published generation.");
        using var connection = database.OpenConnection();
        var scan = Stopwatch.StartNew();
        var projection = TimeSpan.Zero;
        try
        {
            var input = new CorpusSourceInput(declaration, key.ProjectId);
            var (inventory, guard) = InspectInput(input, cancellationToken);
            guard();
            scan.Stop();
            if (snapshot.Binding != input.Binding || !snapshot.Inventory.SequenceEqual(inventory))
            {
                MarkChecked(connection, key.SourceId, snapshot.Stamp.Id, CorpusFreshness.Changed);
                return new(CorpusFreshness.Changed, scan.Elapsed, projection);
            }

            var check = Stopwatch.StartNew();
            using (var transaction = connection.BeginTransaction())
            {
                var valid = CorpusProjectionVerification.Check(connection, transaction, snapshot, cancellationToken);
                projection = check.Elapsed;
                cancellationToken.ThrowIfCancellationRequested();
                UpdateHead(connection, transaction, key.SourceId, snapshot.Stamp.Id,
                    valid ? CorpusFreshness.Changed : CorpusFreshness.InvalidProjection);
                transaction.Commit();
                if (!valid)
                    return new(CorpusFreshness.InvalidProjection, scan.Elapsed, projection);
            }

            ConfirmHead(connection, key.SourceId, snapshot.Stamp.Id, guard);
            return new(CorpusFreshness.Verified, scan.Elapsed, projection);
        }
        catch (Exception exception) when (InputFailure(exception) is { } freshness)
        {
            scan.Stop();
            MarkChecked(connection, key.SourceId, snapshot.Stamp.Id, freshness);
            return new(freshness, scan.Elapsed, projection, freshness == CorpusFreshness.Unavailable
                ? "The declared Corpus Source inputs are missing, unreadable or invalid."
                : "The Corpus Source changed during verification.");
        }
    }

    private static (CorpusFileVersion[] Inventory, Action Guard) InspectInput(CorpusSourceInput input,
        CancellationToken cancellationToken, Action<string>? afterFingerprint = null,
        IReadOnlyList<CorpusFileVersion>? expectedFiles = null, IReadOnlyList<CorpusDocumentVersion>? expectedDocuments = null)
    {
        if (input.Sikor is null)
        {
            var names = input.FileNames();
            if (expectedFiles is not null && !names.SequenceEqual(expectedFiles.Select(file => file.Name)))
                throw CorpusSourceInput.Changed();
            var check = input.Recheck(names, cancellationToken, (file, index) =>
            {
                if (expectedFiles is not null && expectedFiles[index] != file) throw CorpusSourceInput.Changed();
                afterFingerprint?.Invoke(file.Name);
            });
            return (check.Versions, () => input.EnsureUnchanged(check.Observations, cancellationToken));
        }
        var observation = input.ObserveSikor();
        var documents = input.ReadSikor(cancellationToken).Select((document, index) =>
        {
            var version = document.Version;
            if (expectedDocuments is not null &&
                (index >= expectedDocuments.Count || !SameSikorDocument(expectedDocuments[index], version)))
                throw CorpusSourceInput.Changed();
            return version;
        });
        var inventory = SikorInventory(input, documents);
        return (inventory, () => input.EnsureFilesUnchanged([observation], cancellationToken));
    }

    private static bool SameSikorDocument(CorpusDocumentVersion? previous, CorpusDocumentVersion current) =>
        previous is not null && previous.Id == current.Id && previous.Position == current.Position &&
        previous.TextHash == current.TextHash;

    private static CorpusFreshness? InputFailure(Exception exception) => exception switch
    {
        CorpusSourceChangedException => CorpusFreshness.Changed,
        IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException =>
            CorpusFreshness.Unavailable,
        _ => null,
    };

    private static CorpusFileVersion[] SikorInventory(CorpusSourceInput input, IEnumerable<CorpusDocumentVersion> documents) =>
        [new("", input.Sikor!.Protect("inventory", CorpusContent.Digest(
            documents.Select(document => new[] { document.Id, document.TextHash }).ToArray())), 0)];

    internal static string DocumentDigest(IEnumerable<CorpusDocumentVersion> documents) =>
        CorpusContent.Digest(documents.ToArray());

    private SourceKey Key(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return new(database.Project.FieldWorksProjectIdentity, sourceId);
    }

    private FileStream AcquireBuildLease() =>
        new(database.FullPath + ".corpus-build.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static string? PrepareRebuildHead(SqliteConnection connection, string source)
    {
        using var transaction = connection.BeginTransaction();
        using var command = CorpusSql.Command(connection, transaction, """
            SELECT h.Generation,g.ProjectionValid,h.Freshness
            FROM IndexedCorpusHeads h JOIN IndexedCorpusGenerations g ON g.Id=h.Generation
            WHERE h.SourceId=$source AND g.SourceId=$source AND g.Published=1;
            """, ("$source", source));
        string generation;
        var malformed = false;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            generation = reader.GetString(0);
            try
            {
                CorpusSourceSnapshot.ReadStoredStatus(reader, 1, 2);
            }
            catch (CorpusSourceRefusal exception) when (exception.Reason == CorpusSourceFailure.InvalidProjection)
            {
                malformed = true;
            }
        }
        if (malformed) CorpusProjectionVerification.Invalidate(connection, transaction, generation, [], []);
        transaction.Commit();
        return generation;
    }

    private static void Cleanup(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = CorpusSql.Command(connection, transaction, """
            SELECT EXISTS (SELECT 1 FROM IndexedCorpusGenerations WHERE Published=0);
            """);
        if ((long)command.ExecuteScalar()! == 1)
        {
            command.CommandText = """
                DELETE FROM IndexedCorpusDocuments WHERE Generation IN (SELECT Id FROM IndexedCorpusGenerations WHERE Published=0);
                DELETE FROM IndexedCorpusWeights WHERE Generation IN (SELECT Id FROM IndexedCorpusGenerations WHERE Published=0);
                DELETE FROM IndexedCorpusShards WHERE StageGeneration IN (SELECT Id FROM IndexedCorpusGenerations WHERE Published=0);
                DELETE FROM IndexedCorpusGenerations WHERE Published=0;
                DELETE FROM IndexedCorpusForms WHERE NOT EXISTS
                    (SELECT 1 FROM IndexedCorpusOccurrences WHERE FormId=IndexedCorpusForms.Id);
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private void ConfirmHead(SqliteConnection connection, string source, string generation, Action guard)
    {
        // An interruption before the post-commit re-stat must leave the committed head uncertified.
        AfterHeadCommit?.Invoke();
        MarkChecked(connection, source, generation, CorpusFreshness.Verified, guard);
    }

    private static void MarkChecked(SqliteConnection connection, string source, string generation,
        CorpusFreshness freshness, Action? guard = null)
    {
        using var transaction = connection.BeginTransaction();
        UpdateHead(connection, transaction, source, generation, freshness);
        guard?.Invoke();
        transaction.Commit();
    }

    private static void UpdateHead(SqliteConnection connection, SqliteTransaction transaction, string source,
        string generation, CorpusFreshness freshness)
    {
        using var command = CorpusSql.Command(connection, transaction, """
            UPDATE IndexedCorpusHeads SET CheckedUtc=$now,Freshness=CASE
                WHEN (SELECT ProjectionValid FROM IndexedCorpusGenerations WHERE Id=$id)=0 THEN $invalid
                ELSE $freshness END
            WHERE SourceId=$source AND Generation=$id;
            """, ("$now", CheckedTime()), ("$freshness", (int)freshness), ("$invalid", (int)CorpusFreshness.InvalidProjection),
            ("$source", source), ("$id", generation));
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The Corpus Source head moved during the operation.");
    }

    private static void Publish(SqliteConnection connection, string source, string generation, string? expected, Action guard)
    {
        using var transaction = connection.BeginTransaction();
        var now = CheckedTime();
        using var publish = CorpusSql.Command(connection, transaction, """
            UPDATE IndexedCorpusGenerations SET Published=1,PublishedUtc=$now
            WHERE Id=$id AND SourceId=$source AND Published=0;
            """, ("$id", generation), ("$source", source), ("$now", now));
        if (publish.ExecuteNonQuery() != 1) throw new InvalidOperationException("The staged Corpus generation is unavailable.");
        using var head = CorpusSql.Command(connection, transaction, expected is null ? """
            INSERT OR IGNORE INTO IndexedCorpusHeads VALUES ($source,$id,$now,$freshness);
            """ : """
            UPDATE IndexedCorpusHeads SET Generation=$id,CheckedUtc=$now,Freshness=$freshness
            WHERE SourceId=$source AND Generation=$expected;
            """, ("$id", generation), ("$source", source), ("$now", now), ("$expected", expected),
            ("$freshness", (int)CorpusFreshness.Changed));
        if (head.ExecuteNonQuery() != 1) throw new InvalidOperationException("The Corpus Source head moved during publication.");
        guard();
        transaction.Commit();
    }

    private static string CheckedTime() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
