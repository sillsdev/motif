using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Corpus.Adapter;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Corpus;

/// <summary>Publishes immutable grammar measurements and serves exact pins without opening a project or parser.</summary>
internal sealed class GrammarCoverageStore(string path, ProjectLocator project,
    Func<BaselineRecord, string, LowercasePass>? readCasing = null, Func<string>? readIcuVersion = null) : IGrammarCoverageReader
{
    private const int MeasurementSchema = 1;
    private const int CursorVersion = 1;
    private const string Query = "no-analysis";
    internal const string RankOrder = "frequency-desc,wordform-id-asc";

    public GrammarCoverageStore(ProjectLocator project) : this(ProjectDatabaseCatalog.DatabasePathFor(project), project) { }

    public GrammarCoverageSegmentation Prepare(MotifDatabase database, FrequencyManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var inventory = GrammarCoverageInventory.FromManifest(manifest,
            ComparisonVersion(manifest.Provenance.FormNormalization), cancellationToken);
        EnsurePaired(database);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        cancellationToken.ThrowIfCancellationRequested();
        InsertSegmentation(connection, transaction, inventory.Id, JsonSerializer.Serialize(inventory));
        transaction.Commit();
        return new(inventory.Id, inventory.MappingDigest, inventory.WeightDigest, inventory.Provenance.ParatextScope?.Generation);
    }

    public GrammarCoverageSegmentation PrepareParatext(MotifDatabase database, string projectId, string sourceId, string generation,
        string profileHash, CoreWritingSystemDefinition writingSystem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writingSystem);
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePaired(database);
        var casing = LowercasePass.For(writingSystem);
        using var connection = database.OpenConnection();
        GrammarCoverageInventory? inventory = null;
        GrammarCoverageSegmentation segmentation;
        using (var snapshot = connection.BeginTransaction(deferred: true))
        {
            var current = GrammarCoverageInventory.ReadParatextProvenance(connection, snapshot, projectId, sourceId,
                generation, profileHash, writingSystem);
            var provenance = current with { ParatextScope = current.ParatextScope! with { Generation = "" } };
            var comparisonVersion = ComparisonVersion(provenance.FormNormalization);
            var id = GrammarCoverageInventory.Digest(new { Kind = "paratext-inventory", provenance, casing.CasingLocale, comparisonVersion });
            var existing = ReadSegmentation(connection, snapshot, id, generation);
            if (existing is not null)
            {
                if (HasGeneration(connection, snapshot, id, generation)) return existing;
                segmentation = existing;
            }
            else
            {
                var rows = GrammarCoverageInventory.ReadParatextRows(connection, snapshot, provenance, casing, cancellationToken);
                inventory = GrammarCoverageInventory.FromManifest(new(provenance, rows), comparisonVersion, cancellationToken) with { Id = id };
                segmentation = new(id, inventory.MappingDigest, inventory.WeightDigest, generation);
            }
        }

        using var transaction = connection.BeginTransaction();
        if (SourceIndexRepository.ReadState(connection, transaction, sourceId) is not { Status: "ready" } state ||
            state.Generation != generation)
            throw new InvalidDataException("The Paratext Source generation changed while preparing Coverage.");
        cancellationToken.ThrowIfCancellationRequested();
        if (inventory is not null)
            InsertSegmentation(connection, transaction, inventory.Id, JsonSerializer.Serialize(inventory));
        InsertSegmentation(connection, transaction, GenerationKey(segmentation.Id, generation), JsonSerializer.Serialize(segmentation.Id));
        transaction.Commit();
        return segmentation;
    }

    private static GrammarCoverageSegmentation? ReadSegmentation(SqliteConnection connection, SqliteTransaction transaction,
        string id, string generation)
    {
        using var query = Command(connection, transaction, """
            SELECT json_extract(Payload, '$.MappingDigest'), json_extract(Payload, '$.WeightDigest')
            FROM GrammarCoverageSegmentations WHERE Id = $id;
            """, ("$id", id));
        using var rows = query.ExecuteReader();
        return rows.Read() ? new(id, rows.GetString(0), rows.GetString(1), generation) : null;
    }

    private static readonly Lazy<bool> LoaderInitialised = new(() =>
    {
        FwDataProjectLoader.Init();
        return true;
    }, LazyThreadSafetyMode.PublicationOnly);

    private string ComparisonVersion(string sourceNormalization)
    {
        _ = LoaderInitialised.Value;
        var icuVersion = readIcuVersion?.Invoke() ?? Icu.Wrapper.IcuVersion;
        return $"{sourceNormalization?.ToLowerInvariant()}-to-nfd/icu-{icuVersion}/ordinal/{RankOrder}";
    }

    // Generation bindings preserve source pins while all equivalent generations share one inventory payload.
    private static string GenerationKey(string id, string generation) =>
        "paratext-generation:" + GrammarCoverageInventory.Digest(new { id, generation });

    private static bool HasGeneration(SqliteConnection connection, SqliteTransaction? transaction, string id, string generation)
    {
        using var query = Command(connection, transaction, """
            SELECT 1 FROM GrammarCoverageSegmentations WHERE Id = $id AND Payload = $payload;
            """, ("$id", GenerationKey(id, generation)), ("$payload", JsonSerializer.Serialize(id)));
        return query.ExecuteScalar() is not null;
    }

    private static void InsertSegmentation(SqliteConnection connection, SqliteTransaction transaction, string id, string payload)
    {
        using var insert = Command(connection, transaction, """
            INSERT INTO GrammarCoverageSegmentations (Id, Payload) VALUES ($id, $payload) ON CONFLICT (Id) DO NOTHING;
            """, ("$id", id), ("$payload", payload));
        insert.ExecuteNonQuery();
    }

    public GrammarCoverageSummary Build(MotifDatabase database, string assessmentId, GrammarCoverageBasis basis,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (basis.Revision != basis.Baseline.SemanticSnapshotDigest)
            throw new InvalidDataException("The Assessment store does not match the pinned project revision.");
        EnsurePaired(database);
        using var reader = database.OpenConnection();
        var inventory = ReadPayload<GrammarCoverageInventory>(reader, null,
            "SELECT Payload FROM GrammarCoverageSegmentations WHERE Id = $id;", basis.SegmentationId);
        if (inventory.MappingDigest != basis.MappingDigest || inventory.Provenance.ProjectId != basis.ProjectId)
            throw new InvalidDataException("The saved Assessment was requested with an incompatible Wordform mapping.");
        if (inventory.ComparisonVersion != ComparisonVersion(inventory.Provenance.FormNormalization))
            throw new InvalidDataException("The frequency inventory was prepared with a different or unrecorded comparison basis.");
        if (inventory.Provenance.ParatextScope is { Generation: "" } scope)
        {
            if (basis.SourceGeneration is not { Length: > 0 } generation || !HasGeneration(reader, null, inventory.Id, generation))
                throw new InvalidDataException("The frequency inventory has no binding for the pinned Source generation.");
            inventory = inventory with { Provenance = inventory.Provenance with { ParatextScope = scope with { Generation = generation } } };
        }
        else if (basis.SourceGeneration is not null && inventory.Provenance.ParatextScope?.Generation != basis.SourceGeneration)
            throw new InvalidDataException("The frequency inventory has no binding for the pinned Source generation.");

        var assessment = new AssessmentRepository(database).Get(assessmentId);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        var registered = new BaselineRepository(database).GetLatestCaptured(projectKey);
        if (registered?.Token != basis.Baseline)
            registered = new RetainedInvocationRepository(database).FindAssessmentBaseline(projectKey, basis.Baseline, assessment);
        if (registered is null || !File.Exists(registered.FwDataPath))
            throw new InvalidDataException("The requested registered Baseline is unavailable in the paired project store.");

        var evidence = new GrammarCoverageAssessment(assessment, basis, inventory, registered,
            readCasing ?? GrammarCoverageParserInputs.ReadCasing, cancellationToken);
        var invocation = assessment.Invocation!;
        var baselineId = registered.Token.BundleDigest;
        var stampDigest = GrammarCoverageInventory.Digest(new
        {
            basis.ProjectId, basis.Revision, baselineId, basis.GrammarId, basis.ParserId, basis.LimitsId,
            inventory.Id, inventory.ComparisonVersion, SourceGeneration = inventory.Provenance.ParatextScope?.Generation,
            assessmentId, assessment.ScopeDigest, assessment.ModelFingerprint, evidence.InputBinding,
            invocation.ImportedSnapshotSha256, invocation.InvocationId, invocation.WordsSha256,
            invocation.TsvSha256, invocation.AnalysesSha256, invocation.StandardErrorSha256, invocation.GrammarWarnings, invocation.StoppedWordRetry,
            GrammarCoverageProjection.Policy, MeasurementSchema,
        });
        var stamp = new CorpusStamp(stampDigest[7..39], basis.Revision, baselineId, inventory.Provenance.ScopeDigest,
            inventory.Provenance.SourceDigest, inventory.Id, basis.GrammarId, basis.ParserId, [], basis.LimitsId,
            null, GrammarCoverageProjection.Policy, MeasurementSchema,
            inventory.Provenance.SourceId, inventory.Provenance.ParatextScope?.Generation, inventory.ComparisonVersion);
        var projection = new GrammarCoverageProjection();
        foreach (var row in inventory.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projection.Add(row, evidence.Measure(row, inventory.Provenance.WritingSystemId), inventory.Provenance.WritingSystemId);
        }
        var summary = projection.Summarize(inventory, stamp, assessmentId, evidence.InputBinding);
        Publish(database, summary, projection.Ranked, cancellationToken);
        return summary;
    }

    public GrammarCoverageSummary ReadSummary(string stampId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Read((connection, snapshot) => ReadSummary(connection, snapshot, stampId));
    }

    public GrammarCoveragePage ReadNoAnalysis(string stampId, PageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Limit is < 1 or > PageRequest.MaxLimit)
            throw new GrammarCoverageReadException(GrammarCoverageReadFailure.InvalidRequest, $"Page size must be 1 to {PageRequest.MaxLimit}.");
        cancellationToken.ThrowIfCancellationRequested();
        return Read((connection, snapshot) => ReadNoAnalysis(connection, snapshot, stampId, request, cancellationToken));
    }

    private static GrammarCoveragePage ReadNoAnalysis(SqliteConnection connection, SqliteTransaction snapshot,
        string stampId, PageRequest request, CancellationToken cancellationToken)
    {
        var summary = ReadSummary(connection, snapshot, stampId);
        var cursor = request.Cursor is null ? null : DecodeCursor(request.Cursor, summary.Stamp);
        if (cursor is not null)
        {
            using var anchor = Command(connection, snapshot, """
                SELECT 1 FROM GrammarCoverageNoAnalysisRank
                WHERE StampId = $id AND NegativeWeight = $weight AND WordformId = $wordform;
                """, ("$id", stampId), ("$weight", cursor.NegativeWeight), ("$wordform", cursor.WordformId));
            if (anchor.ExecuteScalar() is null) throw InvalidCursor();
        }

        var seek = cursor is null ? "" : "AND (NegativeWeight, WordformId) > ($weight, $wordform)";
        using var query = Command(connection, snapshot, $"""
            SELECT NegativeWeight, WordformId, WritingSystemId, FormNfd FROM GrammarCoverageNoAnalysisRank
            WHERE StampId = $id {seek} ORDER BY NegativeWeight, WordformId LIMIT $limit;
            """, ("$id", stampId), ("$limit", request.Limit + 1));
        if (cursor is not null)
        {
            query.Parameters.AddWithValue("$weight", cursor.NegativeWeight);
            query.Parameters.AddWithValue("$wordform", cursor.WordformId);
        }
        using var rows = query.ExecuteReader();
        var items = new List<WordformCoverage>();
        RankCursor? last = null;
        var total = summary.Aggregate.WordformBuckets.NoAnalysis.HasValue ? (long?)summary.RankedWordforms : null;
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count == request.Limit)
                return Page(EncodeCursor(last!));
            last = new(CursorVersion, summary.Stamp.SchemaVersion, stampId, summary.Stamp.ScopeDigest, Query, RankOrder,
                rows.GetInt64(0), rows.GetString(1));
            var row = new RankedWordform(last.WordformId, rows.GetString(2), rows.GetString(3), -last.NegativeWeight);
            items.Add(row.ToRow());
        }
        return Page(null);

        GrammarCoveragePage Page(string? next) =>
            GrammarCoverageWire.RankedPage(summary, items, total, next);
    }

    private static void Publish(MotifDatabase database, GrammarCoverageSummary summary, IReadOnlyList<RankedWordform> ranks,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var header = Command(connection, transaction, """
            INSERT INTO GrammarCoverageMeasurements (StampId, Payload) VALUES ($id, $payload) ON CONFLICT (StampId) DO NOTHING;
            """, ("$id", summary.Stamp.Id), ("$payload", JsonSerializer.Serialize(summary)));
        if (header.ExecuteNonQuery() == 0) return;

        using var insert = Command(connection, transaction, """
            INSERT INTO GrammarCoverageNoAnalysisRank VALUES ($id, $weight, $wordform, $ws, $form);
            """, ("$id", summary.Stamp.Id), ("$weight", 0L), ("$wordform", ""), ("$ws", ""), ("$form", ""));
        insert.Prepare();
        foreach (var row in ranks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            insert.Parameters["$weight"].Value = -row.Frequency;
            insert.Parameters["$wordform"].Value = row.Id;
            insert.Parameters["$ws"].Value = row.WritingSystem;
            insert.Parameters["$form"].Value = row.FormNfd;
            insert.ExecuteNonQuery();
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private void EnsurePaired(MotifDatabase database)
    {
        var pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(database.FullPath, Path.GetFullPath(path), pathComparison) ||
            !string.Equals(database.Project.FullFwDataPath, project.FullFwDataPath, pathComparison) ||
            database.Project.FieldWorksProjectIdentity != project.FieldWorksProjectIdentity)
            throw new InvalidDataException("The grammar coverage inputs must belong to the paired project store.");
    }

    private T Read<T>(Func<SqliteConnection, SqliteTransaction, T> read)
    {
        try
        {
            return MotifDatabase.ReadExisting(path, project, read);
        }
        catch (Exception exception) when (exception is MotifStoreLockException || SqliteConnections.IsBusy(exception) ||
            exception.InnerException is { } inner && SqliteConnections.IsBusy(inner))
        {
            throw new GrammarCoverageReadException(GrammarCoverageReadFailure.Busy, "The project store is busy. Retry the read.", exception);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or SqliteException or
            NotSupportedException or UnauthorizedAccessException or JsonException)
        {
            throw new GrammarCoverageReadException(GrammarCoverageReadFailure.Unavailable, "The project store is unavailable.", exception);
        }
    }

    private static GrammarCoverageSummary ReadSummary(SqliteConnection connection, SqliteTransaction? transaction, string stampId) =>
        ReadPayload<GrammarCoverageSummary>(connection, transaction,
            "SELECT Payload FROM GrammarCoverageMeasurements WHERE StampId = $id;", stampId);

    private static T ReadPayload<T>(SqliteConnection connection, SqliteTransaction? transaction, string sql, string id)
    {
        using var command = Command(connection, transaction, sql, ("$id", id));
        return command.ExecuteScalar() is string payload ? Deserialize<T>(payload) :
            throw new GrammarCoverageReadException(GrammarCoverageReadFailure.ExpiredPin, "The grammar coverage pin is unavailable.");
    }

    private static T Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload)
        ?? throw new InvalidDataException("The grammar coverage store contains an unreadable payload.");

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private sealed record RankCursor(int Version, int Schema, string StampId, string Scope, string Query, string Order,
        long NegativeWeight, string WordformId);

    private static string EncodeCursor(RankCursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor));

    private static RankCursor DecodeCursor(string text, CorpusStamp stamp)
    {
        try
        {
            var cursor = JsonSerializer.Deserialize<RankCursor>(Convert.FromBase64String(text));
            if (cursor is { Version: CursorVersion, Query: Query, Order: RankOrder, NegativeWeight: < 0, WordformId.Length: > 0 } &&
                cursor.Schema == stamp.SchemaVersion && cursor.StampId == stamp.Id && cursor.Scope == stamp.ScopeDigest)
                return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw InvalidCursor(exception);
        }
        throw InvalidCursor();
    }

    private static GrammarCoverageReadException InvalidCursor(Exception? inner = null) =>
        new(GrammarCoverageReadFailure.InvalidCursor, "The cursor is invalid for this pinned no-analysis query.", inner);
}
