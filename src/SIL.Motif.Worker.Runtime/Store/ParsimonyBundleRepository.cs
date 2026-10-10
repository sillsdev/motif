using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Worker.Store;

/// <summary>Stores published Parsimony files and the pins that control their lifetime.</summary>
public class EvidenceArtifactRepository : IBaselinePinQuery
{
    public static readonly TimeSpan ReaderLeaseDuration = TimeSpan.FromMinutes(2);

    // Shared by both claim queries; COALESCE stops a purged job's NULL join from hiding its claim.
    private const string LiveClaimPredicate = "COALESCE(j.JobId IS NOT NULL AND j.Status='running' AND " +
        "j.ClaimToken=c.ClaimToken AND j.LeaseUntilUtc>$now, 0)";

    private readonly MotifDatabase _database;
    private readonly IJobClock _clock;

    /// <summary>Creates an artifact repository over an opened project database.</summary>
    public EvidenceArtifactRepository(MotifDatabase database, IJobClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? new SystemJobClock();
    }

    /// <summary>Finds one bundle descriptor by its opaque identity.</summary>
    public ParsimonyBundleRecord? Get(string bundleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BundleSelect + " WHERE BundleId=$id;";
        command.Parameters.AddWithValue("$id", bundleId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var record = ReadBundle(reader);
        if (reader.Read()) throw new InvalidDataException("The bundle identity is not unique.");
        return record;
    }

    /// <summary>Finds one bundle only when it belongs to the named project workspace.</summary>
    public ParsimonyBundleRecord? Get(string projectKey, string bundleId)
    {
        var bundle = Get(bundleId);
        return bundle is not null && StringComparer.Ordinal.Equals(bundle.ProjectKey, projectKey) ? bundle : null;
    }

    /// <summary>Registers the running job claim that owns an unpublished build attempt.</summary>
    public void RegisterBuildClaim(ParsimonyBuildClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ParsimonyBuildClaims
                (BundleId, ProjectKey, JobId, ClaimToken, BaselineRootDirectory, WorkingDirectory, CreatedUtc)
            VALUES ($bundle, $project, $job, $claim, $baseline, $working, $created);
            """;
        command.Parameters.AddWithValue("$bundle", claim.BundleId);
        command.Parameters.AddWithValue("$project", claim.ProjectKey);
        command.Parameters.AddWithValue("$job", claim.JobId);
        command.Parameters.AddWithValue("$claim", claim.ClaimToken);
        command.Parameters.AddWithValue("$baseline", Path.GetFullPath(claim.BaselineRootDirectory));
        command.Parameters.AddWithValue("$working", Path.GetFullPath(claim.WorkingDirectory));
        command.Parameters.AddWithValue("$created", Stamp(_clock.UtcNow));
        command.ExecuteNonQuery();
    }

    /// <summary>Records the exact staging and final paths after the parser identity is known.</summary>
    public void SetBuildClaimPaths(string bundleId, string stagingDirectory, string finalDirectory)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ParsimonyBuildClaims SET StagingDirectory=$staging, FinalDirectory=$final " +
            "WHERE BundleId=$bundle;";
        command.Parameters.AddWithValue("$staging", Path.GetFullPath(stagingDirectory));
        command.Parameters.AddWithValue("$final", Path.GetFullPath(finalDirectory));
        command.Parameters.AddWithValue("$bundle", bundleId);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The bundle build claim was lost.");
    }

    /// <summary>Removes a build claim after its owned attempt has been cleaned up.</summary>
    public void RemoveBuildClaim(string bundleId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParsimonyBuildClaims WHERE BundleId=$bundle;";
        command.Parameters.AddWithValue("$bundle", bundleId);
        command.ExecuteNonQuery();
    }

    /// <summary>Lists attempts whose owning job no longer holds the recorded claim.</summary>
    public IReadOnlyList<ParsimonyBuildClaim> ListExpiredBuildClaims(string projectKey)
    {
        var now = Stamp(_clock.UtcNow);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.BundleId, c.ProjectKey, c.JobId, c.ClaimToken, c.BaselineRootDirectory, " +
            "c.WorkingDirectory, c.StagingDirectory, c.FinalDirectory, c.CreatedUtc " +
            "FROM ParsimonyBuildClaims AS c LEFT JOIN Jobs AS j ON j.JobId=c.JobId " +
            "WHERE c.ProjectKey=$project AND NOT (" + LiveClaimPredicate + ") ORDER BY c.CreatedUtc, c.BundleId;";
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$now", now);
        using var reader = command.ExecuteReader();
        var claims = new List<ParsimonyBuildClaim>();
        while (reader.Read()) claims.Add(ReadClaim(reader));
        return claims;
    }

    /// <summary>Returns the full working directories still owned by a running job with an unexpired lease.</summary>
    public IReadOnlySet<string> ListLiveBuildClaimWorkingDirectories(string projectKey)
    {
        var now = Stamp(_clock.UtcNow);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.WorkingDirectory FROM ParsimonyBuildClaims AS c " +
            "LEFT JOIN Jobs AS j ON j.JobId=c.JobId " +
            "WHERE c.ProjectKey=$project AND (" + LiveClaimPredicate + ");";
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$now", now);
        using var reader = command.ExecuteReader();
        var directories = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) directories.Add(Path.GetFullPath(reader.GetString(0)));
        return directories;
    }

    /// <summary>Publishes the descriptor and Report in one visibility transaction.</summary>
    public void Publish(ParsimonyBundleRecord bundle, ReportRecord report,
        CancellationToken cancellationToken = default, Action? beforeCommit = null, Action? afterCommit = null,
        bool requireCurrentBaseline = true, bool setCurrentBaseline = true)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(report);
        ValidateDescriptor(bundle);
        if (!StringComparer.Ordinal.Equals(bundle.BundleId, report.BundleIdFromJson()))
            throw new ArgumentException("The Report must name the bundle being published.", nameof(report));
        ValidateFileDigest(bundle.GrammarFactsPath, bundle.GrammarFactsSha256);
        ValidateFileDigest(bundle.EvidencePath, bundle.EvidenceSha256);

        if (bundle.InputKind == "candidate" && !Directory.Exists(bundle.BaselineRootDirectory))
            throw new FileNotFoundException("The exact source Baseline bundle is no longer available.",
                bundle.BaselineRootDirectory);

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (bundle.InputKind == "baseline" && requireCurrentBaseline)
            EnsureBaselineStillCurrent(connection, transaction, bundle);
        var now = Stamp(_clock.UtcNow);
        string? jobId = null;
        using (var build = connection.CreateCommand())
        {
            build.Transaction = transaction;
            build.CommandText = """
                SELECT c.JobId, c.ClaimToken, j.ClaimToken, j.Status, j.LeaseUntilUtc
                FROM ParsimonyBuildClaims AS c LEFT JOIN Jobs AS j ON j.JobId=c.JobId
                WHERE c.BundleId=$bundle;
                """;
            build.Parameters.AddWithValue("$bundle", bundle.BundleId);
            using var reader = build.ExecuteReader();
            if (reader.Read())
            {
                if (reader.IsDBNull(2) || reader.GetString(1) != reader.GetString(2) ||
                    reader.GetString(3) != "running" || reader.IsDBNull(4) || reader.GetString(4).CompareTo(now) <= 0)
                    throw new InvalidOperationException("The Parsimony job no longer holds its writer claim.");
                jobId = reader.GetString(0);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ParsimonyBundles
                    (BundleId, BaselineTokenJson, ProjectKey, BaselineDigest, MaterialKey,
                     EvidenceDirectory, GrammarFactsPath, EvidencePath, BaselineRootDirectory,
                     InputKind, CandidateIdentity,
                     ModelFingerprint, GrammarFactsSchemaVersion, GrammarFactsSha256,
                     EvidenceSchemaVersion, EvidenceSha256, CreatedUtc, State, UnavailableReason)
                VALUES
                    ($id, $token, $project, $baseline, $material, $directory, $facts, $evidence, $source,
                     $inputKind, $candidateIdentity,
                     $fingerprint, $factsSchema, $factsSha, $evidenceSchema, $evidenceSha, $created,
                     'available', NULL);
                """;
            BindBundle(command, bundle);
            command.ExecuteNonQuery();
        }
        ReportRepository.SaveImmutable(connection, transaction, report);
        using (var reference = connection.CreateCommand())
        {
            reference.Transaction = transaction;
            reference.CommandText = "INSERT INTO ParsimonyBundleReferences " +
                "(BundleId, ReferenceKind, ReferenceId, CreatedUtc) VALUES ($bundle, 'report', $report, $created);";
            reference.Parameters.AddWithValue("$bundle", bundle.BundleId);
            reference.Parameters.AddWithValue("$report", report.ReportId);
            reference.Parameters.AddWithValue("$created", bundle.CreatedUtc);
            reference.ExecuteNonQuery();
        }
        if (jobId is not null)
        {
            using var jobReference = connection.CreateCommand();
            jobReference.Transaction = transaction;
            jobReference.CommandText = "INSERT INTO ParsimonyBundleReferences " +
                "(BundleId, ReferenceKind, ReferenceId, CreatedUtc) VALUES ($bundle, 'job', $job, $created);";
            jobReference.Parameters.AddWithValue("$bundle", bundle.BundleId);
            jobReference.Parameters.AddWithValue("$job", jobId);
            jobReference.Parameters.AddWithValue("$created", bundle.CreatedUtc);
            jobReference.ExecuteNonQuery();
        }
        if (bundle.InputKind == "baseline" && setCurrentBaseline)
        {
            using var current = connection.CreateCommand();
            current.Transaction = transaction;
            current.CommandText = """
                INSERT INTO CurrentParsimonyBundles(ProjectKey, InputKind, BundleId)
                VALUES ($project, 'baseline', $bundle)
                ON CONFLICT(ProjectKey, InputKind) DO UPDATE SET BundleId=excluded.BundleId;
                """;
            current.Parameters.AddWithValue("$project", bundle.ProjectKey);
            current.Parameters.AddWithValue("$bundle", bundle.BundleId);
            current.ExecuteNonQuery();
        }
        using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = "DELETE FROM ParsimonyBuildClaims WHERE BundleId=$bundle;";
            claim.Parameters.AddWithValue("$bundle", bundle.BundleId);
            claim.ExecuteNonQuery();
        }
        cancellationToken.ThrowIfCancellationRequested();
        beforeCommit?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        afterCommit?.Invoke();
    }

    /// <summary>Acquires a short lease before a reader opens either published file.</summary>
    public ParsimonyReaderLease AcquireReaderLease(string bundleId, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var leaseId = Guid.NewGuid().ToString("N");
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT State FROM ParsimonyBundles WHERE BundleId=$bundle;";
            check.Parameters.AddWithValue("$bundle", bundleId);
            var state = check.ExecuteScalar() as string;
            if (state is null) throw new KeyNotFoundException($"Parsimony bundle '{bundleId}' was not found.");
            if (state != "available") throw new InvalidDataException("The Parsimony bundle is not available for reading.");
        }
        var now = _clock.UtcNow;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO ParsimonyReaderLeases " +
                "(LeaseId, BundleId, OwnerId, CreatedUtc, HeartbeatUtc, ExpiresUtc) " +
                "VALUES ($lease, $bundle, $owner, $now, $now, $expires);";
            insert.Parameters.AddWithValue("$lease", leaseId);
            insert.Parameters.AddWithValue("$bundle", bundleId);
            insert.Parameters.AddWithValue("$owner", ownerId);
            insert.Parameters.AddWithValue("$now", Stamp(now));
            insert.Parameters.AddWithValue("$expires", Stamp(now.Add(ReaderLeaseDuration)));
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
        return new ParsimonyReaderLease(this, leaseId, bundleId, ownerId);
    }

    /// <summary>Renews a live reader lease; an expired or deleting bundle cannot be revived.</summary>
    public bool RenewReaderLease(string leaseId)
    {
        var now = _clock.UtcNow;
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ParsimonyReaderLeases SET HeartbeatUtc=$now, ExpiresUtc=$expires
            WHERE LeaseId=$lease AND ExpiresUtc>$now AND EXISTS (
                SELECT 1 FROM ParsimonyBundles WHERE BundleId=ParsimonyReaderLeases.BundleId AND State='available'
            );
            """;
        command.Parameters.AddWithValue("$now", Stamp(now));
        command.Parameters.AddWithValue("$expires", Stamp(now.Add(ReaderLeaseDuration)));
        command.Parameters.AddWithValue("$lease", leaseId);
        var renewed = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return renewed;
    }

    /// <summary>Releases one reader lease after its database connections close.</summary>
    public void ReleaseReaderLease(string leaseId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParsimonyReaderLeases WHERE LeaseId=$lease;";
        command.Parameters.AddWithValue("$lease", leaseId);
        command.ExecuteNonQuery();
    }

    /// <summary>Adds a durable Report, Check Run, or job pin to an available bundle.</summary>
    public void AddReference(string bundleId, string referenceKind, string referenceId)
    {
        if (referenceKind is not ("report" or "check-run" or "job"))
            throw new ArgumentException("The Parsimony bundle reference kind is unsupported.", nameof(referenceKind));
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT State FROM ParsimonyBundles WHERE BundleId=$bundle;";
            check.Parameters.AddWithValue("$bundle", bundleId);
            if (!StringComparer.Ordinal.Equals(check.ExecuteScalar() as string, "available"))
                throw new InvalidDataException("A reference can only pin an available Parsimony bundle.");
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO ParsimonyBundleReferences " +
            "(BundleId, ReferenceKind, ReferenceId, CreatedUtc) VALUES ($bundle, $kind, $id, $created);";
        command.Parameters.AddWithValue("$bundle", bundleId);
        command.Parameters.AddWithValue("$kind", referenceKind);
        command.Parameters.AddWithValue("$id", referenceId);
        command.Parameters.AddWithValue("$created", Stamp(_clock.UtcNow));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Releases a durable bundle pin without changing the pinned record.</summary>
    public void RemoveReference(string bundleId, string referenceKind, string referenceId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParsimonyBundleReferences " +
            "WHERE BundleId=$bundle AND ReferenceKind=$kind AND ReferenceId=$id;";
        command.Parameters.AddWithValue("$bundle", bundleId);
        command.Parameters.AddWithValue("$kind", referenceKind);
        command.Parameters.AddWithValue("$id", referenceId);
        command.ExecuteNonQuery();
    }

    /// <summary>Deletes a Report and releases its bundle reference in one transaction.</summary>
    public bool DeleteReport(string reportId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var report = connection.CreateCommand();
        report.Transaction = transaction;
        report.CommandText = "DELETE FROM Reports WHERE ReportId=$id;";
        report.Parameters.AddWithValue("$id", reportId);
        var deleted = report.ExecuteNonQuery() == 1;
        using var reference = connection.CreateCommand();
        reference.Transaction = transaction;
        reference.CommandText = "DELETE FROM ParsimonyBundleReferences WHERE ReferenceKind='report' AND ReferenceId=$id;";
        reference.Parameters.AddWithValue("$id", reportId);
        reference.ExecuteNonQuery();
        transaction.Commit();
        return deleted;
    }

    /// <summary>Marks an unavailable bundle while retaining its frozen Report and provenance descriptor.</summary>
    /// <remarks>
    /// A busy fault is refused: a bundle another process holds open is intact and must stay available.
    /// </remarks>
    public void MarkUnavailable(string bundleId, ParsimonyBundleFault fault, string reason)
    {
        if (fault == ParsimonyBundleFault.Busy)
            throw new ArgumentException("A busy bundle is not unavailable; release it and retry.", nameof(fault));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ParsimonyBundles SET State=$state, UnavailableReason=$reason " +
            "WHERE BundleId=$bundle AND State='available';";
        command.Parameters.AddWithValue("$state", fault == ParsimonyBundleFault.Missing ? "missing" : "corrupt");
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$bundle", bundleId);
        command.ExecuteNonQuery();
    }

    /// <summary>Lists bundle descriptors newest first with exact pin reasons and current-material state.</summary>
    public IReadOnlyList<ParsimonyBundleRetentionItem> ListForRetention(string projectKey)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.BundleId, b.BaselineTokenJson, b.EvidenceDirectory, b.GrammarFactsPath, b.EvidencePath,
                   b.ModelFingerprint, b.GrammarFactsSchemaVersion, b.GrammarFactsSha256,
                   b.EvidenceSchemaVersion, b.EvidenceSha256, b.CreatedUtc, b.ProjectKey,
                   b.BaselineDigest, b.MaterialKey, b.State, b.UnavailableReason, b.BaselineRootDirectory,
                   b.InputKind, b.CandidateIdentity,
                   CASE WHEN c.BundleId IS NOT NULL AND EXISTS (
                       SELECT 1 FROM Baselines AS live WHERE live.ProjectKey=b.ProjectKey
                           AND REPLACE(live.BundleDigest, 'sha256:', '')=b.BaselineDigest
                   ) THEN 1 ELSE 0 END
            FROM ParsimonyBundles AS b
            LEFT JOIN CurrentParsimonyBundles AS c ON c.ProjectKey=b.ProjectKey
                AND c.InputKind='baseline' AND c.BundleId=b.BundleId
            WHERE b.ProjectKey=$project ORDER BY b.CreatedUtc DESC, b.BundleId DESC;
        """;
        command.Parameters.AddWithValue("$project", projectKey);
        var records = new List<(ParsimonyBundleRecord Bundle, bool IsCurrent)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) records.Add((ReadBundle(reader), reader.GetInt32(19) == 1));
        }
        return records.Select(item => new ParsimonyBundleRetentionItem(item.Bundle, item.IsCurrent,
            ReadBundlePinReasons(connection, item.Bundle.BundleId, _clock.UtcNow))).ToArray();
    }

    /// <summary>Returns human-readable reasons that keep one published bundle alive.</summary>
    public IReadOnlyList<string> GetPinReasons(string bundleId, DateTimeOffset? now = null)
    {
        using var connection = _database.OpenConnection();
        return ReadBundlePinReasons(connection, bundleId, now ?? _clock.UtcNow);
    }

    /// <summary>Claims an unpinned, non-current bundle for deletion in the same transaction as the pin check.</summary>
    public bool TryMarkDeleting(string bundleId, DateTimeOffset now)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT b.State, EXISTS(
                    SELECT 1 FROM CurrentParsimonyBundles AS c
                    JOIN Baselines AS live ON live.ProjectKey=c.ProjectKey
                    WHERE c.BundleId=b.BundleId AND REPLACE(live.BundleDigest, 'sha256:', '')=b.BaselineDigest
                ), EXISTS(SELECT 1 FROM ParsimonyBundleReferences WHERE BundleId=b.BundleId),
                EXISTS(SELECT 1 FROM ParsimonyReaderLeases WHERE BundleId=b.BundleId AND ExpiresUtc>$now)
                FROM ParsimonyBundles AS b WHERE b.BundleId=$bundle;
                """;
            command.Parameters.AddWithValue("$now", Stamp(now));
            command.Parameters.AddWithValue("$bundle", bundleId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            if (reader.GetString(0) is not ("available" or "deleting") || reader.GetInt32(1) != 0 ||
                reader.GetInt32(2) != 0 || reader.GetInt32(3) != 0)
            {
                transaction.Rollback();
                return false;
            }
        }
        using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = "UPDATE ParsimonyBundles SET State='deleting', UnavailableReason=NULL " +
                "WHERE BundleId=$bundle AND State IN ('available','deleting');";
            mark.Parameters.AddWithValue("$bundle", bundleId);
            if (mark.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }
        }
        transaction.Commit();
        return true;
    }

    /// <summary>Removes a deleting descriptor after every registered file has been removed.</summary>
    public bool CompleteDeletion(string bundleId, DateTimeOffset now)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM ParsimonyBundles WHERE BundleId=$bundle AND State='deleting'
                AND NOT EXISTS (SELECT 1 FROM ParsimonyBundleReferences WHERE BundleId=$bundle)
                AND NOT EXISTS (SELECT 1 FROM ParsimonyReaderLeases WHERE BundleId=$bundle AND ExpiresUtc>$now)
                AND NOT EXISTS (
                    SELECT 1 FROM CurrentParsimonyBundles AS c JOIN Baselines AS live ON live.ProjectKey=c.ProjectKey
                    WHERE c.BundleId=$bundle AND REPLACE(live.BundleDigest, 'sha256:', '')=(
                        SELECT BaselineDigest FROM ParsimonyBundles WHERE BundleId=$bundle
                    )
                );
            """;
        command.Parameters.AddWithValue("$bundle", bundleId);
        command.Parameters.AddWithValue("$now", Stamp(now));
        var deleted = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return deleted;
    }

    /// <summary>Reports Baseline files retained by active Parsimony work or a pinned frozen bundle.</summary>
    public BaselinePinSources GetPinSources(string baselinePath)
    {
        string full;
        try { full = Path.GetFullPath(baselinePath); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { return BaselinePinSources.None; }
        using var connection = _database.OpenConnection();
        var reasons = BaselinePinSources.None;
        using (var jobs = connection.CreateCommand())
        {
            jobs.CommandText = """
                SELECT 1 FROM ParsimonyBuildClaims AS c JOIN Jobs AS j ON j.JobId=c.JobId
                WHERE c.BaselineRootDirectory=$path AND j.Status='running' AND j.ClaimToken=c.ClaimToken
                    AND j.LeaseUntilUtc>$now LIMIT 1;
                """;
            jobs.Parameters.AddWithValue("$path", full);
            jobs.Parameters.AddWithValue("$now", Stamp(_clock.UtcNow));
            if (jobs.ExecuteScalar() is not null) reasons |= BaselinePinSources.ParsimonyJob;
        }
        using (var references = connection.CreateCommand())
        {
            references.CommandText = """
                SELECT DISTINCT r.ReferenceKind FROM ParsimonyBundles AS b
                JOIN ParsimonyBundleReferences AS r ON r.BundleId=b.BundleId
                WHERE b.BaselineRootDirectory=$path;
                """;
            references.Parameters.AddWithValue("$path", full);
            using var reader = references.ExecuteReader();
            while (reader.Read())
                reasons |= reader.GetString(0) switch
                {
                    "report" => BaselinePinSources.ParsimonyReport,
                    "check-run" => BaselinePinSources.ParsimonyCheckRun,
                    "job" => BaselinePinSources.ParsimonyJob,
                    _ => BaselinePinSources.None,
                };
        }
        return reasons;
    }

    private static IReadOnlyList<string> ReadBundlePinReasons(SqliteConnection connection, string bundleId,
        DateTimeOffset now)
    {
        var reasons = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReferenceKind, ReferenceId FROM ParsimonyBundleReferences " +
            "WHERE BundleId=$bundle ORDER BY ReferenceKind, ReferenceId;";
        command.Parameters.AddWithValue("$bundle", bundleId);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) reasons.Add(reader.GetString(0) + ":" + reader.GetString(1));
        using var leases = connection.CreateCommand();
        leases.CommandText = "SELECT LeaseId FROM ParsimonyReaderLeases WHERE BundleId=$bundle AND ExpiresUtc>$now;";
        leases.Parameters.AddWithValue("$bundle", bundleId);
        leases.Parameters.AddWithValue("$now", Stamp(now));
        using (var reader = leases.ExecuteReader())
            while (reader.Read()) reasons.Add("reader:" + reader.GetString(0));
        using var current = connection.CreateCommand();
        current.CommandText = """
            SELECT 1 FROM CurrentParsimonyBundles AS c JOIN ParsimonyBundles AS b ON b.BundleId=c.BundleId
            JOIN Baselines AS live ON live.ProjectKey=b.ProjectKey
            WHERE c.BundleId=$bundle AND REPLACE(live.BundleDigest, 'sha256:', '')=b.BaselineDigest LIMIT 1;
            """;
        current.Parameters.AddWithValue("$bundle", bundleId);
        if (current.ExecuteScalar() is not null) reasons.Add("current-baseline-material");
        return reasons;
    }

    private static void EnsureBaselineStillCurrent(SqliteConnection connection, SqliteTransaction transaction,
        ParsimonyBundleRecord bundle)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT REPLACE(BundleDigest, 'sha256:', ''), RootDirectory " +
            "FROM Baselines WHERE ProjectKey=$project;";
        command.Parameters.AddWithValue("$project", bundle.ProjectKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || !StringComparer.Ordinal.Equals(reader.GetString(0), bundle.BaselineDigest) ||
            !StringComparer.Ordinal.Equals(Path.GetFullPath(reader.GetString(1)),
                Path.GetFullPath(bundle.BaselineRootDirectory)))
            throw new InvalidOperationException("The Baseline changed before the Parsimony bundle could be published.");
    }

    private static void ValidateDescriptor(ParsimonyBundleRecord bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.BundleId) || string.IsNullOrWhiteSpace(bundle.ProjectKey) ||
            string.IsNullOrWhiteSpace(bundle.BaselineDigest) || string.IsNullOrWhiteSpace(bundle.MaterialKey) ||
            string.IsNullOrWhiteSpace(bundle.BaselineRootDirectory) || bundle.GrammarFactsSchemaVersion <= 0 ||
            bundle.EvidenceSchemaVersion <= 0 || bundle.State != "available" ||
            bundle.InputKind is not ("baseline" or "candidate") ||
            ((bundle.InputKind == "candidate") != !string.IsNullOrWhiteSpace(bundle.CandidateIdentity)))
            throw new ArgumentException("The Parsimony bundle descriptor is incomplete.", nameof(bundle));
        var directory = Path.GetFullPath(bundle.EvidenceDirectory);
        if (!StringComparer.Ordinal.Equals(Path.GetFullPath(bundle.GrammarFactsPath),
                Path.Combine(directory, "grammar-facts.sqlite")) ||
            !StringComparer.Ordinal.Equals(Path.GetFullPath(bundle.EvidencePath),
                Path.Combine(directory, "evidence.sqlite")))
            throw new InvalidDataException("The Parsimony descriptor must name the two files in its bundle directory.");
    }

    private static void ValidateFileDigest(string path, string expected)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("A closed Parsimony artifact is missing.", path);
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!StringComparer.OrdinalIgnoreCase.Equals(actual, StripDigest(expected)))
            throw new InvalidDataException("A Parsimony artifact changed after validation.");
    }

    private static string StripDigest(string digest) => digest.StartsWith("sha256:", StringComparison.Ordinal)
        ? digest[7..] : digest;

    private static string Stamp(DateTimeOffset value) => JobTimestamp.FormatUtc(value);

    private static void BindBundle(SqliteCommand command, ParsimonyBundleRecord bundle)
    {
        command.Parameters.AddWithValue("$id", bundle.BundleId);
        command.Parameters.AddWithValue("$token", bundle.BaselineTokenJson);
        command.Parameters.AddWithValue("$project", bundle.ProjectKey);
        command.Parameters.AddWithValue("$baseline", bundle.BaselineDigest);
        command.Parameters.AddWithValue("$material", bundle.MaterialKey);
        command.Parameters.AddWithValue("$directory", Path.GetFullPath(bundle.EvidenceDirectory));
        command.Parameters.AddWithValue("$facts", Path.GetFullPath(bundle.GrammarFactsPath));
        command.Parameters.AddWithValue("$evidence", Path.GetFullPath(bundle.EvidencePath));
        command.Parameters.AddWithValue("$source", Path.GetFullPath(bundle.BaselineRootDirectory));
        command.Parameters.AddWithValue("$inputKind", bundle.InputKind);
        command.Parameters.AddWithValue("$candidateIdentity", (object?)bundle.CandidateIdentity ?? DBNull.Value);
        command.Parameters.AddWithValue("$fingerprint", bundle.ModelFingerprint);
        command.Parameters.AddWithValue("$factsSchema", bundle.GrammarFactsSchemaVersion);
        command.Parameters.AddWithValue("$factsSha", StripDigest(bundle.GrammarFactsSha256));
        command.Parameters.AddWithValue("$evidenceSchema", bundle.EvidenceSchemaVersion);
        command.Parameters.AddWithValue("$evidenceSha", StripDigest(bundle.EvidenceSha256));
        command.Parameters.AddWithValue("$created", bundle.CreatedUtc);
    }

    private const string BundleSelect = """
        SELECT BundleId, BaselineTokenJson, EvidenceDirectory, GrammarFactsPath, EvidencePath,
               ModelFingerprint, GrammarFactsSchemaVersion, GrammarFactsSha256, EvidenceSchemaVersion,
               EvidenceSha256, CreatedUtc, ProjectKey, BaselineDigest, MaterialKey, State, UnavailableReason,
               BaselineRootDirectory, InputKind, CandidateIdentity
        FROM ParsimonyBundles
        """;

    private static ParsimonyBundleRecord ReadBundle(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.GetInt32(6), reader.GetString(7), reader.GetInt32(8), reader.GetString(9),
        reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14),
        reader.IsDBNull(15) ? null : reader.GetString(15), reader.GetString(16), reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18));

    private static ParsimonyBuildClaim ReadClaim(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8));
}

/// <summary>Compatibility name for commands that resolve a bundle in the project database.</summary>
public sealed class ParsimonyBundleRepository : EvidenceArtifactRepository
{
    /// <summary>Creates a repository over an opened project database.</summary>
    public ParsimonyBundleRepository(MotifDatabase database, IJobClock? clock = null) : base(database, clock) { }
}

/// <summary>One immutable bundle descriptor and its project/source material keys.</summary>
public sealed record ParsimonyBundleRecord(
    string BundleId,
    string BaselineTokenJson,
    string EvidenceDirectory,
    string GrammarFactsPath,
    string EvidencePath,
    string ModelFingerprint,
    int GrammarFactsSchemaVersion,
    string GrammarFactsSha256,
    int EvidenceSchemaVersion,
    string EvidenceSha256,
    string CreatedUtc,
    string ProjectKey = "",
    string BaselineDigest = "",
    string MaterialKey = "",
    string State = "available",
    string? UnavailableReason = null,
    string BaselineRootDirectory = "",
    string InputKind = "baseline",
    string? CandidateIdentity = null);

/// <summary>One registered writer attempt and its exact worker-owned directories.</summary>
public sealed record ParsimonyBuildClaim(string BundleId, string ProjectKey, string JobId, string ClaimToken,
    string BaselineRootDirectory, string WorkingDirectory, string? StagingDirectory = null,
    string? FinalDirectory = null, string CreatedUtc = "");

/// <summary>A bundle and its computed retention status.</summary>
public sealed record ParsimonyBundleRetentionItem(ParsimonyBundleRecord Bundle, bool IsCurrent,
    IReadOnlyList<string> PinReasons);

/// <summary>One short-lived lease held while a reader has either artifact open.</summary>
public sealed class ParsimonyReaderLease : IDisposable
{
    private readonly EvidenceArtifactRepository _repository;
    private CancellationTokenSource? _heartbeatCancellation;
    private CancellationTokenSource? _lostCancellation;
    private Task? _heartbeat;

    internal ParsimonyReaderLease(EvidenceArtifactRepository repository, string leaseId, string bundleId, string ownerId)
    {
        _repository = repository;
        LeaseId = leaseId;
        BundleId = bundleId;
        OwnerId = ownerId;
    }

    /// <summary>The durable lease identity.</summary>
    public string LeaseId { get; }
    /// <summary>The bundle protected by this lease.</summary>
    public string BundleId { get; }
    /// <summary>The reader's diagnostic identity.</summary>
    public string OwnerId { get; }
    /// <summary>Signals that the lease could no longer be renewed.</summary>
    public CancellationToken CancellationToken => _lostCancellation?.Token ?? CancellationToken.None;

    /// <summary>Starts renewal every twenty seconds while the reader is active.</summary>
    public void StartHeartbeat()
    {
        if (_heartbeat is not null) throw new InvalidOperationException("The reader lease heartbeat already started.");
        _heartbeatCancellation = new CancellationTokenSource();
        _lostCancellation = new CancellationTokenSource();
        _heartbeat = RunHeartbeatAsync(_heartbeatCancellation.Token);
    }

    /// <summary>Renews the lease immediately, primarily for a bounded page or a deterministic test.</summary>
    public bool Renew() => _repository.RenewReaderLease(LeaseId);

    public void Dispose()
    {
        _heartbeatCancellation?.Cancel();
        _repository.ReleaseReaderLease(LeaseId);
        _heartbeatCancellation?.Dispose();
        _lostCancellation?.Dispose();
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                if (!_repository.RenewReaderLease(LeaseId))
                {
                    _lostCancellation?.Cancel();
                    return;
                }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            _lostCancellation?.Cancel();
        }
    }
}

internal static class ParsimonyReportJson
{
    public static string? BundleIdFromJson(this ReportRecord report)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(report.ReportJson);
            return document.RootElement.GetProperty("inputs").GetProperty("bundleId").GetString();
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or
                                           InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }
}
