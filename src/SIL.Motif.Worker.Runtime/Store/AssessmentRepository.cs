using System.Globalization;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Worker.Store;

/// <summary>Previously selected Assessment evidence is no longer in the store.</summary>
public sealed class AssessmentExpiredException(string id)
    : Exception($"Assessment '{id}' is no longer available. Refresh the evidence and try again.");

/// <summary>
/// Durable Assessment storage: one immutable measurement per row, plus the project's single pointer to
/// its current Assessment. Callers use this seam without depending on the normalized detail tables.
/// </summary>
public interface IAssessmentRepository
{
    /// <summary>
    /// Records one Assessment together with its words and analyses, in one transaction — a half-written
    /// Assessment must never be observable. Assessments are immutable (ADR 0042 decision 2): recording
    /// under an id that already exists is a genuine primary-key collision, not an upsert.
    /// </summary>
    void Record(NewAssessmentRecord assessment);

    /// <summary>Records every kind from one invocation atomically, including its shared evidence.</summary>
    void RecordBatch(IReadOnlyList<NewAssessmentRecord> assessments);

    /// <summary>
    /// Records every kind from a claimed Trial and completes its job in one transaction while the claim remains active.
    /// </summary>
    /// <param name="assessments">The complete Assessment batch produced by the Trial.</param>
    /// <param name="jobId">The Trial job row whose claim authorises publication.</param>
    /// <param name="claimToken">The token captured when this runner claimed the job.</param>
    /// <param name="completionJson">The Trial result that names the recorded Assessment ids.</param>
    /// <exception cref="OperationCanceledException">
    /// The job is cancelled, terminal, or no longer held by this claim.
    /// </exception>
    void RecordBatchForClaim(IReadOnlyList<NewAssessmentRecord> assessments, string jobId, string claimToken,
        string completionJson);

    /// <summary>Gets one Assessment, with its full word and analysis detail, by id.</summary>
    /// <exception cref="KeyNotFoundException">No Assessment is recorded under this id.</exception>
    AssessmentRecord Get(string assessmentId);

    /// <summary>
    /// Lists Assessment headers recorded against one Proposal, oldest first. Word and analysis detail is
    /// omitted — call <see cref="Get"/> for one Assessment's full content.
    /// </summary>
    IReadOnlyList<AssessmentRecord> ListByProposal(CanonicalId proposalId);

    /// <summary>Lists Assessment headers for one Proposal revision, oldest first.</summary>
    IReadOnlyList<AssessmentRecord> ListByProposalAndDigest(CanonicalId proposalId, string intentDigest);

    /// <summary>
    /// Lists Assessment headers of one kind, oldest first. Word and analysis detail is omitted — call
    /// <see cref="Get"/> for one Assessment's full content.
    /// </summary>
    IReadOnlyList<AssessmentRecord> ListByKind(string kind);

    /// <summary>
    /// Lists Baseline Assessments of one kind — rows whose <see cref="AssessmentRecord.ProposalId"/> is
    /// <c>null</c>, meaning they measured the project itself rather than a candidate Proposal — oldest
    /// first, so the newest is last. Unlike <see cref="ListByKind"/>, word and analysis detail is already
    /// populated: this is the stored evidence a Selection composer reads to find what a previous run
    /// failed or timed out on. It returns that evidence exactly as recorded and derives nothing from it —
    /// deciding what to retry is the composer's job, never this repository's.
    /// </summary>
    IReadOnlyList<AssessmentRecord> ListBaselineAssessments(string kind);

    /// <summary>Gets the newest complete Baseline Assessment matching a token and exact Selection content.</summary>
    AssessmentRecord? FindLatestBaselineAssessment(
        string kind, string baselineToken, string selectionSha256, IReadOnlyList<string> selectionWords);

    /// <summary>Reads one current Baseline Assessment, or the named Assessments, for selected word forms only.</summary>
    IReadOnlyList<AssessmentRecord> ReadBaselineAssessmentWords(
        string kind, string baselineToken, IReadOnlyList<string>? assessmentIds, IReadOnlyCollection<string> wordForms);

    /// <summary>
    /// Promotes one Assessment to be the project's current Assessment (ADR 0042 decision 2): a pointer
    /// the project holds, not a state the Assessment carries.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Assessment is recorded under this id.</exception>
    void PromoteToCurrent(string assessmentId);

    /// <summary>Gets the project's current Assessment, or <c>null</c> when none has been promoted yet.</summary>
    /// <exception cref="InvalidDataException">The pointer names an Assessment that is no longer recorded.</exception>
    AssessmentRecord? GetCurrent();

    /// <summary>
    /// Deletes the Assessments recorded against one Proposal that nothing else still needs, except
    /// <paramref name="exceptAssessmentId"/> — the sweep purge-on-apply drives (ADR 0042's Trial amendment). A
    /// promoted candidate must always be named here by its exact id: this call, not the order it runs in relative
    /// to promotion, is what keeps it out of the sweep. An Assessment a Report cites, a pin or retained invocation
    /// holds, that is current, or that a surviving Assessment replaces is kept, and no surviving row is rewritten.
    /// </summary>
    void DeleteByProposal(CanonicalId proposalId, string? exceptAssessmentId);
}

internal sealed class TrialPublicationOutcomeUnknownException : IOException
{
    public TrialPublicationOutcomeUnknownException(string jobId, Exception commitException,
        IReadOnlyList<Exception> cleanupExceptions, Exception readBackException)
        : base($"The Trial publication commit for job '{jobId}' could not be confirmed. " +
            $"Commit error: {commitException.Message} Cleanup errors: {string.Join(" | ", cleanupExceptions.Select(error => error.Message))} " +
            $"Read-back error: {readBackException.Message}",
            new AggregateException([commitException, .. cleanupExceptions, readBackException]))
    {
    }
}

internal interface ITrialPublicationStorageBoundary
{
    /// <summary>Commits the transaction that completes a Trial and inserts its Assessments.</summary>
    void Commit(SqliteTransaction transaction);

    /// <summary>Releases the transaction after a publication attempt.</summary>
    void DisposeTransaction(SqliteTransaction transaction);

    /// <summary>Closes the connection before the independent outcome read-back.</summary>
    void CloseConnection(SqliteConnection connection);

    /// <summary>Releases the publication connection.</summary>
    void DisposeConnection(SqliteConnection connection);

    /// <summary>Reads the job outcome after the publication connection has been released.</summary>
    JobRecord? ReadBack(Func<JobRecord?> readBack);
}

internal sealed class SqliteTrialPublicationStorageBoundary : ITrialPublicationStorageBoundary
{
    /// <summary>The production boundary over the pinned SQLite provider.</summary>
    internal static SqliteTrialPublicationStorageBoundary Instance { get; } = new();

    /// <summary>Commits the SQLite transaction.</summary>
    public void Commit(SqliteTransaction transaction) => transaction.Commit();

    /// <summary>Disposes the SQLite transaction.</summary>
    public void DisposeTransaction(SqliteTransaction transaction) => transaction.Dispose();

    /// <summary>Closes the SQLite connection.</summary>
    public void CloseConnection(SqliteConnection connection) => connection.Close();

    /// <summary>Disposes the SQLite connection.</summary>
    public void DisposeConnection(SqliteConnection connection) => connection.Dispose();

    /// <summary>Reads the saved job through a separate repository connection.</summary>
    public JobRecord? ReadBack(Func<JobRecord?> readBack) => readBack();
}

/// <summary>
/// One Assessment ready to record: its identity, Assessor, kind, scope and digests, and the words and
/// analyses it produced. Word measurements use null <see cref="ProposalId"/> and <see cref="ProposalIntentDigest"/>
/// for a Baseline run. Phonology material names its Baseline source explicitly and may retain the authored
/// revision's supporting Proposal association, with no candidate intent digest.
/// An unfinalized Draft uses its normalized content digest in <see cref="ProposalIntentDigest"/>;
/// Finalize of unchanged content has the same evidence identity.
/// </summary>
public sealed record NewAssessmentRecord(
    string AssessmentId,
    CanonicalId? ProposalId,
    string? ProposalIntentDigest,
    string Assessor,
    string Kind,
    string ScopeJson,
    string ScopeDigest,
    AssessmentSubject Subject,
    string BaselineToken,
    string? OutcomeDigest,
    string? SemanticDigest,
    string GrammarSourceSha256,
    string? ModelFingerprint,
    string? Pipeline,
    int? DiagnosticCount,
    AssessmentRecordMaterial Material,
    string? SavedUtc = null)
{
    public string? ReplacesAssessmentId { get; init; }

    public IReadOnlyList<AssessedWord> Words => RequireWordMaterial().Rows;
    public BatchInvocationEvidence? Invocation => RequireWordMaterial().Invocation;
    public IReadOnlyList<AssessmentObjectTiming> ObjectTimings => RequireWordMaterial().ObjectTimings;
    public string? CachePath => RequireWordMaterial().CachePath;
    public string? CacheDigest => RequireWordMaterial().CacheDigest;
    public Selection Selection => ((AssessmentSubject.Words)Subject).Selection;

    public AssessmentRecordMaterial.Words RequireWordMaterial() => Material as AssessmentRecordMaterial.Words
        ?? throw new InvalidOperationException("This operation requires Words material.");
}

/// <summary>The immutable subject and identity, with explicitly unloaded or subject-specific detail.</summary>
public sealed record AssessmentRecord(
    string AssessmentId,
    CanonicalId? ProposalId,
    string? ProposalIntentDigest,
    string Assessor,
    string Kind,
    string ScopeJson,
    string ScopeDigest,
    AssessmentSubject Subject,
    string BaselineToken,
    string? OutcomeDigest,
    string? SemanticDigest,
    string GrammarSourceSha256,
    string? ModelFingerprint,
    string? Pipeline,
    int? DiagnosticCount,
    string SavedUtc,
    AssessmentRecordDetails Details)
{
    public string? ReplacesAssessmentId { get; init; }

    public AssessmentSubject.Words RequireWords() => Subject as AssessmentSubject.Words
        ?? throw new InvalidOperationException("This projection requires a Words Assessment.");

    private AssessmentRecordDetails.Words? WordDetails()
    {
        RequireWords();
        return Details as AssessmentRecordDetails.Words;
    }

    public Selection Selection => RequireWords().Selection;
    public string TokeniserName => RequireWords().TokeniserName;
    public string TokeniserVersion => RequireWords().TokeniserVersion;
    public IReadOnlyList<AssessedWord>? Words => WordDetails()?.Rows;
    public string? CachePath => Details switch { AssessmentRecordDetails.Words w => w.CachePath,
        AssessmentRecordDetails.Unloaded u => u.CachePath, _ => null };
    public string? CacheDigest => Details switch { AssessmentRecordDetails.Words w => w.CacheDigest,
        AssessmentRecordDetails.Unloaded u => u.CacheDigest, _ => null };
    public BatchInvocationEvidence? Invocation => Details switch { AssessmentRecordDetails.Words w => w.Invocation,
        AssessmentRecordDetails.Unloaded u => u.Invocation, _ => null };
    public IReadOnlyList<AssessmentObjectTiming> ObjectTimings => WordDetails()?.ObjectTimings ?? [];
    public AssessmentTimingEvidence? TimingEvidence => WordDetails()?.TimingEvidence;
}

/// <summary>
/// Where a caller turns one recorded Assessment into the narrower view it actually needs — a Report's, a
/// comparison's, or a regression check's (ADR 0042's projections). Kept beside <see cref="AssessmentRecord"/>
/// itself rather than beside each narrower type: those live in <c>SIL.Motif.Host</c>, which this module
/// depends on and never the reverse, so this is the one place able to see both the row and every view of it.
/// </summary>
public static class AssessmentRecordProjections
{
    /// <summary>The material a Report is computed from — see <see cref="ReportableAssessment"/>.</summary>
    public static ReportableAssessment ToReportable(this AssessmentRecord record)
    {
        record.RequireWords();
        return new(
        record.AssessmentId, record.Assessor, record.Kind, record.ScopeJson,
        record.Selection.Name, record.Selection.Words, record.Selection.Sha256, record.GrammarSourceSha256,
        record.Words ?? Array.Empty<AssessedWord>());
    }

    /// <summary>The fields a join between two Assessments needs — see <see cref="ComparableAssessment"/>.</summary>
    public static ComparableAssessment ToComparable(this AssessmentRecord record)
    {
        record.RequireWords();
        return new(
        record.AssessmentId, record.Assessor, record.Kind, record.TokeniserName, record.TokeniserVersion,
        record.Words ?? Array.Empty<AssessedWord>(),
        record.Invocation?.ExecutableVersion, record.Invocation?.ExecutableBytesSha256,
        record.Invocation?.StoppedWordRetry is { Failure: null });
    }

    /// <summary>The <c>Correctness</c>-kind fields a regression check needs — see <see cref="CorrectnessAssessment"/>.</summary>
    public static CorrectnessAssessment ToCorrectness(this AssessmentRecord record)
    {
        record.RequireWords();
        return new(
        record.AssessmentId, record.Assessor, record.TokeniserName, record.TokeniserVersion,
        ScopeCodec.ReadTrial(record.ScopeJson, RegressionChecker.RequiredKind),
        record.Selection, record.GrammarSourceSha256, record.Words ?? Array.Empty<AssessedWord>());
    }

    /// <summary>The parsed report and Selection <c>motif analyses --assessment</c> reads — see <see cref="StoredAssessment"/>.</summary>
    public static StoredAssessment ToStored(this AssessmentRecord record)
    {
        record.RequireWords();
        if (record.OutcomeDigest is null || record.SemanticDigest is null || record.ModelFingerprint is null ||
            record.Pipeline is null || record.DiagnosticCount is null)
            throw new InvalidOperationException(
                "This Assessment contains timing or statistics evidence without an authoritative analysis report.");
        return new StoredAssessment(new AssessReport(
            record.Words ?? Array.Empty<AssessedWord>(), record.OutcomeDigest, record.SemanticDigest,
            record.GrammarSourceSha256, record.ModelFingerprint, record.Pipeline, record.DiagnosticCount.Value),
            record.Selection);
    }
}

/// <summary>Reads and writes normalized Assessment tables and the project's current-Assessment pointer.</summary>
public sealed class AssessmentRepository : IAssessmentRepository
{
    private static readonly JsonSerializerOptions InvocationJsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    private readonly MotifDatabase _database;
    private readonly ITrialPublicationStorageBoundary _trialPublicationStorage;

    /// <summary>Creates a repository over an already worker-owned database.</summary>
    public AssessmentRepository(MotifDatabase database)
        : this(database, SqliteTrialPublicationStorageBoundary.Instance)
    {
    }

    internal AssessmentRepository(MotifDatabase database, ITrialPublicationStorageBoundary trialPublicationStorage)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _trialPublicationStorage = trialPublicationStorage ?? throw new ArgumentNullException(nameof(trialPublicationStorage));
    }

    /// <inheritdoc />
    public void Record(NewAssessmentRecord assessment) => RecordBatch([assessment]);

    /// <inheritdoc />
    public void RecordBatch(IReadOnlyList<NewAssessmentRecord> assessments)
    {
        var prepared = PrepareBatch(assessments);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        InsertPrepared(connection, transaction, prepared);
        transaction.Commit();
    }

    /// <inheritdoc />
    public void RecordBatchForClaim(IReadOnlyList<NewAssessmentRecord> assessments, string jobId, string claimToken,
        string completionJson)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("A job id is required.", nameof(jobId));
        if (string.IsNullOrWhiteSpace(claimToken))
            throw new ArgumentException("A claim token is required.", nameof(claimToken));
        ArgumentNullException.ThrowIfNull(completionJson);
        var prepared = PrepareBatch(assessments);
        using var resources = new TrialPublicationResources(_database.OpenConnection(), _trialPublicationStorage, jobId);
        var connection = resources.Connection;
        var transaction = resources.BeginTransaction(deferred: false);
        try
        {
            new JobRepository(_database).CompleteTrialForClaim(connection, transaction, jobId, claimToken,
                completionJson);
            InsertPrepared(connection, transaction, prepared);
        }
        catch (Exception)
        {
            try { transaction.Rollback(); }
            catch (Exception rollbackException)
            {
                resources.Report("rollback unpublished Trial transaction", rollbackException);
            }
            throw;
        }

        try
        {
            _trialPublicationStorage.Commit(transaction);
        }
        catch (Exception commitException)
        {
            var cleanupExceptions = resources.Release();
            JobRecord? observed;
            try
            {
                observed = _trialPublicationStorage.ReadBack(() => new JobRepository(_database).Get(jobId));
            }
            catch (Exception readBackException)
            {
                throw new TrialPublicationOutcomeUnknownException(jobId, commitException, cleanupExceptions,
                    readBackException);
            }

            if (observed is { Status: JobStatus.Completed, ResultJson: var result } &&
                observed.Kind == TrialJobHandler.TrialKind &&
                string.Equals(observed.ClaimToken, claimToken, StringComparison.Ordinal) &&
                string.Equals(result, completionJson, StringComparison.Ordinal))
            {
                resources.ReportConfirmedCommit(commitException, cleanupExceptions);
                return;
            }

            resources.ReportCleanup(commitException, cleanupExceptions);
            ExceptionDispatchInfo.Capture(commitException).Throw();
            throw;
        }
    }

    private sealed class TrialPublicationResources(SqliteConnection connection,
        ITrialPublicationStorageBoundary storage, string jobId) : IDisposable
    {
        private readonly List<Exception> _cleanupExceptions = [];
        private SqliteTransaction? _transaction;
        private bool _released;

        public SqliteConnection Connection { get; } = connection;

        public SqliteTransaction BeginTransaction(bool deferred)
        {
            _transaction = Connection.BeginTransaction(deferred);
            return _transaction;
        }

        public IReadOnlyList<Exception> Release()
        {
            if (_released) return _cleanupExceptions;
            _released = true;
            if (_transaction is not null)
                TryCleanup(() => storage.DisposeTransaction(_transaction));
            TryCleanup(() => storage.CloseConnection(Connection));
            TryCleanup(() => storage.DisposeConnection(Connection));
            return _cleanupExceptions;
        }

        public void Dispose()
        {
            if (_released) return;
            ReportCleanup(null, Release());
        }

        public void Report(string action, Exception exception) =>
            WriteDiagnostic($"could not {action}: {exception}");

        public void ReportConfirmedCommit(Exception commitException, IReadOnlyList<Exception> cleanupExceptions)
        {
            var cleanupDetails = cleanupExceptions.Count == 0
                ? "none"
                : string.Join(" | ", cleanupExceptions.Select(error => error.Message));
            WriteDiagnostic($"commit error '{commitException.Message}' was resolved by exact read-back; " +
                $"cleanup errors: {cleanupDetails}; details: " +
                new AggregateException([commitException, .. cleanupExceptions]));
        }

        public void ReportCleanup(Exception? commitException, IReadOnlyList<Exception> cleanupExceptions)
        {
            if (cleanupExceptions.Count == 0) return;
            var failures = commitException is null
                ? cleanupExceptions
                : new[] { commitException }.Concat(cleanupExceptions).ToArray();
            Report("release Trial publication resources; " + string.Join(" | ", failures.Select(error => error.Message)),
                new AggregateException(failures));
        }

        private void TryCleanup(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception) { _cleanupExceptions.Add(exception); }
        }

        private void WriteDiagnostic(string message)
        {
            try { Console.Error.WriteLine($"warning: Trial '{jobId}' {message}"); }
            catch (Exception) { }
        }
    }

    private static PreparedAssessment[] PrepareBatch(IReadOnlyList<NewAssessmentRecord> assessments)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        foreach (var assessment in assessments) ValidateRecord(assessment);
        // Serialized before the write lock is taken, so a large Trial holds it only for its inserts.
        return assessments.Select(Prepare).ToArray();
    }

    internal static void ValidateRecords(IReadOnlyList<NewAssessmentRecord> assessments)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        foreach (var assessment in assessments) ValidateRecord(assessment);
    }

    internal static void InsertRecords(
        SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<NewAssessmentRecord> assessments)
    {
        ValidateRecords(assessments);
        InsertPrepared(connection, transaction, assessments.Select(Prepare).ToArray());
    }

    private static void InsertPrepared(
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<PreparedAssessment> assessments)
    {
        var payloads = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        foreach (var prepared in assessments)
            foreach (var row in prepared.Words)
                foreach (var index in new[] { 8, 9, 12, 14, 18 })
                    if (row[index] is byte[] payload)
                    {
                        var hash = SHA256.HashData(payload);
                        var key = Convert.ToHexString(hash);
                        if (payloads.TryGetValue(key, out var existing) &&
                            !payload.AsSpan().SequenceEqual((byte[])existing[1]!))
                            throw new InvalidDataException("Word payload hash collision.");
                        payloads[key] = [hash, payload];
                        row[index] = hash;
                    }
        InsertRows(connection, transaction, "AssessmentWordPayloads", ["Hash", "Payload"], payloads.Values.ToArray(),
            " ON CONFLICT(Hash) DO UPDATE SET Payload = CASE WHEN Payload = excluded.Payload THEN Payload ELSE NULL END");
        foreach (var prepared in assessments)
        {
            var assessment = prepared.Record;
            if (assessment.Material is AssessmentRecordMaterial.Words { Invocation: { } invocation })
                InsertInvocation(connection, transaction, invocation, prepared.InvocationJson!);
            if (assessment.ReplacesAssessmentId is { } replaces)
            {
                var root = ReadHeader(connection, transaction, replaces)
                    ?? throw new KeyNotFoundException("The replaced Assessment does not exist.");
                root.RequireWords();
            }
            InsertHeader(connection, transaction, prepared);
            switch (assessment.Material)
            {
                case AssessmentRecordMaterial.Words:
                    InsertWordsAndAnalyses(connection, transaction, prepared);
                    InsertRows(connection, transaction, "AssessmentObjectTimings", ObjectTimingColumns,
                        prepared.ObjectTimings);
                    break;
                case AssessmentRecordMaterial.PhonologyExamples examples:
                    PhonologyAssessmentRepository.Insert(connection, transaction, assessment, examples);
                    break;
                default: throw new ArgumentException("Unknown Assessment material.");
            }
        }
    }

    // Every value a row needs, computed outside the transaction; word rows leave their id to be assigned inside it.
    private sealed record PreparedAssessment(
        NewAssessmentRecord Record,
        string? InvocationJson,
        string? SelectionWordsJson,
        string? SelectionProvenanceJson,
        IReadOnlyList<object?[]> Words,
        IReadOnlyList<(int Word, object?[] Values)> Analyses,
        IReadOnlyList<object?[]> ObjectTimings);

    private static readonly string[] WordColumns =
    [
        "AssessedWordId", "AssessmentId", "OrdinalIndex", "Word", "Outcome", "ElapsedMs", "ElapsedNs", "RawSignature",
        "MorphologyJson", "CorrectnessJson", "ProjectStanding", "OccurrenceCount", "ReadingGradesJson",
        "MissedApprovedCount", "MissedApprovedJson", "IsIncomplete", "Attempts", "Passes", "AnalysisComparisonJson",
        "WorkSteps",
    ];

    private static readonly string[] AnalysisColumns =
        ["AssessedWordId", "OrdinalIndex", "CategoryGuid", "MorphemeGuidsJson", "RootIndex", "IdentityDigest"];

    private static readonly string[] ObjectTimingColumns =
    [
        "AssessmentId", "Detail", "Evidence",
    ];

    private static PreparedAssessment Prepare(NewAssessmentRecord assessment)
    {
        var rows = (assessment.Material as AssessmentRecordMaterial.Words)?.Rows ?? [];
        var words = new List<object?[]>(rows.Count);
        var analyses = new List<(int, object?[])>();
        for (var wordIndex = 0; wordIndex < rows.Count; wordIndex++)
        {
            var word = rows[wordIndex];
            words.Add(
            [
                null, assessment.AssessmentId, wordIndex, word.Word, word.Outcome, word.ElapsedMs, word.ElapsedNs,
                word.RawSignature,
                word.Morphology is null ? null : Compress(JsonSerializer.Serialize(word.Morphology, ParseMorphEvidence.JsonOptions)),
                word.Correctness is null ? null : Compress(JsonSerializer.Serialize(word.Correctness, ParseMorphEvidence.JsonOptions)),
                word.ProjectStanding, word.OccurrenceCount,
                word.ReadingGrades is null ? null : Compress(JsonSerializer.Serialize(word.ReadingGrades)),
                word.MissedApprovedCount,
                word.MissedApproved is null ? null : Compress(JsonSerializer.Serialize(word.MissedApproved, ParseMorphEvidence.JsonOptions)),
                word.IsIncomplete ? 1 : 0, word.Attempts, word.Passes,
                word.AnalysisComparison is null ? null
                    : Compress(JsonSerializer.Serialize(word.AnalysisComparison, ParseMorphEvidence.JsonOptions)),
                word.WorkSteps,
            ]);
            for (var analysisIndex = 0; analysisIndex < word.Analyses.Count; analysisIndex++)
            {
                var analysis = word.Analyses[analysisIndex];
                analyses.Add((wordIndex,
                [
                    null, analysisIndex, analysis.CategoryGuid, JsonSerializer.Serialize(analysis.MorphemeGuids),
                    analysis.RootIndex, analysis.IdentityDigest,
                ]));
            }
        }
        var wordsMaterial = assessment.Material as AssessmentRecordMaterial.Words;
        object?[][] timings = wordsMaterial is null ||
            (wordsMaterial.ObjectTimings.Count == 0 && wordsMaterial.TimingEvidence is null)
            ? [] : [[assessment.AssessmentId, Compress(JsonSerializer.Serialize(wordsMaterial.ObjectTimings)),
                wordsMaterial.TimingEvidence is null ? null : Compress(JsonSerializer.Serialize(wordsMaterial.TimingEvidence))]];
        var selection = (assessment.Subject as AssessmentSubject.Words)?.Selection;
        return new PreparedAssessment(assessment,
            wordsMaterial?.Invocation is null ? null : JsonSerializer.Serialize(wordsMaterial.Invocation, InvocationJsonOptions),
            selection is null ? null : JsonSerializer.Serialize(selection.Words),
            selection?.Provenance is null ? null : JsonSerializer.Serialize(selection.Provenance),
            words, analyses, timings);
    }

    private static void ValidateRecord(NewAssessmentRecord assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (string.IsNullOrWhiteSpace(assessment.AssessmentId) || string.IsNullOrWhiteSpace(assessment.Assessor) ||
            string.IsNullOrWhiteSpace(assessment.Kind) || string.IsNullOrWhiteSpace(assessment.ScopeJson) ||
            string.IsNullOrWhiteSpace(assessment.ScopeDigest) || string.IsNullOrWhiteSpace(assessment.BaselineToken))
            throw new ArgumentException("Assessment id, Assessor, Kind, scope and Baseline token are required.");
        switch (assessment.Subject, assessment.Material)
        {
            case (AssessmentSubject.Words words, AssessmentRecordMaterial.Words material)
                when !assessment.Kind.IsStoredKind(AssessmentKind.PhonologyExamples):
                ArgumentNullException.ThrowIfNull(words.Selection);
                ArgumentNullException.ThrowIfNull(material.Rows);
                ArgumentNullException.ThrowIfNull(material.ObjectTimings);
                ArgumentException.ThrowIfNullOrWhiteSpace(words.TokeniserName);
                ArgumentException.ThrowIfNullOrWhiteSpace(words.TokeniserVersion);
                break;
            case (AssessmentSubject.PhonologyExamples examples, AssessmentRecordMaterial.PhonologyExamples material)
                when assessment.Kind.IsStoredKind(AssessmentKind.PhonologyExamples) &&
                    assessment.ReplacesAssessmentId is null &&
                    assessment.ProposalIntentDigest is null && assessment.OutcomeDigest is null &&
                    assessment.SemanticDigest is null && assessment.ModelFingerprint is null &&
                    assessment.Pipeline is null && assessment.DiagnosticCount is null:
                ArgumentNullException.ThrowIfNull(examples.Revision);
                ArgumentNullException.ThrowIfNull(material.Source);
                ArgumentNullException.ThrowIfNull(material.RequestedLimits);
                ArgumentNullException.ThrowIfNull(material.Outcome);
                break;
            default: throw new ArgumentException("Assessment subject, kind and material do not match.");
        }
    }

    /// <inheritdoc />
    public AssessmentRecord Get(string assessmentId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        return Get(connection, transaction, assessmentId);
    }

    internal AssessmentRecord GetSelected(string assessmentId)
    {
        try { return Get(assessmentId); }
        catch (KeyNotFoundException) { throw new AssessmentExpiredException(assessmentId); }
    }

    internal AssessmentRecord Get(SqliteConnection connection, SqliteTransaction transaction, string assessmentId)
    {
        var header = ReadHeader(connection, transaction, assessmentId) ??
            throw new KeyNotFoundException($"Assessment '{assessmentId}' was not found.");
        return header.Subject is AssessmentSubject.PhonologyExamples
            ? header with { Details = PhonologyAssessmentRepository.ReadDetails(connection, header.AssessmentId, transaction) }
            : header with { Details = LoadWordDetails(connection, header,
                ReadWords(connection, assessmentId, null, transaction), ReadObjectTimings(connection, assessmentId, transaction), transaction) };
    }

    public AssessmentRecord? GetHeader(string assessmentId)
    {
        using var connection = _database.OpenConnection();
        return GetHeader(connection, assessmentId);
    }

    internal AssessmentRecord? GetHeader(SqliteConnection connection, SqliteTransaction transaction,
        string assessmentId) => ReadHeader(connection, transaction, assessmentId);

    /// <inheritdoc />
    public IReadOnlyList<AssessmentRecord> ListByProposal(CanonicalId proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = HeaderSelectSql + " WHERE ProposalId = $proposalId ORDER BY SavedUtc, AssessmentId;";
        command.Parameters.AddWithValue("$proposalId", proposalId.Value);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var records = new List<AssessmentRecord>();
        while (reader.Read()) records.Add(ReadHeader(reader));
        return records;
    }

    /// <inheritdoc />
    public IReadOnlyList<AssessmentRecord> ListByProposalAndDigest(CanonicalId proposalId, string intentDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentDigest);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = HeaderSelectSql +
            " WHERE ProposalId = $proposalId AND ProposalIntentDigest = $digest ORDER BY SavedUtc, AssessmentId;";
        command.Parameters.AddWithValue("$proposalId", proposalId.Value);
        command.Parameters.AddWithValue("$digest", intentDigest);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var records = new List<AssessmentRecord>();
        while (reader.Read()) records.Add(ReadHeader(reader));
        return records;
    }

    /// <inheritdoc />
    public IReadOnlyList<AssessmentRecord> ListByKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A kind is required.", nameof(kind));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = HeaderSelectSql + " WHERE Kind = $kind ORDER BY SavedUtc, AssessmentId;";
        command.Parameters.AddWithValue("$kind", kind);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var records = new List<AssessmentRecord>();
        while (reader.Read()) records.Add(ReadHeader(reader));
        return records;
    }

    /// <inheritdoc />
    public IReadOnlyList<AssessmentRecord> ListBaselineAssessments(string kind)
    {
        if (kind.IsStoredKind(AssessmentKind.PhonologyExamples))
            throw new ArgumentException("This read requires a Words Assessment kind.");
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A kind is required.", nameof(kind));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        return ListBaselineAssessments(connection, transaction, kind);
    }

    internal IReadOnlyList<AssessmentRecord> ListBaselineAssessments(SqliteConnection connection,
        SqliteTransaction transaction, string kind)
    {
        var headers = new List<AssessmentRecord>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                HeaderSelectSql + " WHERE Kind = $kind AND ProposalId IS NULL ORDER BY SavedUtc, AssessmentId;";
            command.Parameters.AddWithValue("$kind", kind);
            RepositoryReadCounters.QueryExecuted();
            using var reader = command.ExecuteReader();
            while (reader.Read()) headers.Add(ReadHeader(reader));
        }

        return headers.Select(header => header with
        {
            Details = LoadWordDetails(connection, header, ReadWords(connection, header.AssessmentId, null, transaction),
                ReadObjectTimings(connection, header.AssessmentId, transaction), transaction)
        }).ToList();
    }

    /// <inheritdoc />
    public AssessmentRecord? FindLatestBaselineAssessment(
        string kind, string baselineToken, string selectionSha256, IReadOnlyList<string> selectionWords)
    {
        if (kind.IsStoredKind(AssessmentKind.PhonologyExamples))
            throw new ArgumentException("This read requires a Words Assessment kind.");
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionSha256);
        ArgumentNullException.ThrowIfNull(selectionWords);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        return FindLatestBaselineAssessment(connection, transaction, kind, baselineToken, selectionSha256, selectionWords);
    }

    internal AssessmentRecord? FindLatestBaselineAssessment(SqliteConnection connection, SqliteTransaction transaction,
        string kind, string baselineToken, string selectionSha256, IReadOnlyList<string> selectionWords)
    {
        AssessmentRecord? header;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = HeaderSelectSql + """
                 WHERE Kind = $kind AND ProposalId IS NULL AND BaselineToken = $baseline
                   AND SelectionSha256 = $selectionSha AND SelectionWordsJson = $selectionWords
                   AND ReplacesAssessmentId IS NULL
                 ORDER BY SavedUtc DESC, AssessmentId DESC LIMIT 1;
                """;
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$baseline", baselineToken);
            command.Parameters.AddWithValue("$selectionSha", selectionSha256);
            command.Parameters.AddWithValue("$selectionWords", JsonSerializer.Serialize(selectionWords));
            RepositoryReadCounters.QueryExecuted();
            using var reader = command.ExecuteReader();
            header = reader.Read() ? ReadHeader(reader) : null;
        }
        if (header is null) return null;
        return header with
        {
            Details = LoadWordDetails(connection, header, ReadWords(connection, header.AssessmentId, null, transaction),
                ReadObjectTimings(connection, header.AssessmentId, transaction), transaction)
        };
    }

    internal AssessmentRecord? FindLatestBaselineAssessment(
        SqliteConnection connection, SqliteTransaction transaction, string kind, string baselineToken,
        string selectionSha256, IReadOnlyList<string> selectionWords, string selectionName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = HeaderSelectSql + """
             WHERE Kind = $kind AND ProposalId IS NULL AND BaselineToken = $baseline
               AND SelectionSha256 = $selectionSha AND SelectionWordsJson = $selectionWords
               AND SelectionName = $selectionName AND ReplacesAssessmentId IS NULL
             ORDER BY SavedUtc DESC, AssessmentId DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$baseline", baselineToken);
        command.Parameters.AddWithValue("$selectionSha", selectionSha256);
        command.Parameters.AddWithValue("$selectionWords", JsonSerializer.Serialize(selectionWords));
        command.Parameters.AddWithValue("$selectionName", selectionName);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var header = ReadHeader(reader);
        return header with
        {
            Details = LoadWordDetails(connection, header, ReadWords(connection, header.AssessmentId,
                selectionWords.ToHashSet(StringComparer.Ordinal), transaction), ReadObjectTimings(connection, header.AssessmentId, transaction), transaction),
        };
    }

    internal AssessmentRecord? FindLatestBaselineAssessmentHeader(
        SqliteConnection connection, SqliteTransaction transaction, string kind, string baselineToken,
        string selectionSha256, string selectionName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = HeaderSelectSql + """
             WHERE Kind = $kind AND ProposalId IS NULL AND BaselineToken = $baseline
               AND SelectionSha256 = $selectionSha AND SelectionName = $selectionName
               AND ReplacesAssessmentId IS NULL
             ORDER BY SavedUtc DESC, AssessmentId DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$baseline", baselineToken);
        command.Parameters.AddWithValue("$selectionSha", selectionSha256);
        command.Parameters.AddWithValue("$selectionName", selectionName);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadHeader(reader) : null;
    }

    internal IReadOnlyList<AssessmentRecord> ReadReplacementHeaders(
        SqliteConnection connection, SqliteTransaction transaction, string kind, string baselineToken,
        string rootAssessmentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = HeaderSelectSql + """
             WHERE Kind = $kind AND ProposalId IS NULL AND BaselineToken = $baseline
               AND ReplacesAssessmentId = $root
             ORDER BY SavedUtc, AssessmentId;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$baseline", baselineToken);
        command.Parameters.AddWithValue("$root", rootAssessmentId);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var rows = new List<AssessmentRecord>();
        while (reader.Read()) rows.Add(ReadHeader(reader));
        return rows;
    }

    internal string? FindSameInvocationId(SqliteConnection connection, SqliteTransaction transaction,
        string kind, string baselineToken, string invocationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT AssessmentId FROM Assessments WHERE Kind = $kind AND ProposalId IS NULL
                AND BaselineToken = $baseline AND InvocationId = $invocation
            ORDER BY SavedUtc DESC, AssessmentId DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$baseline", baselineToken);
        command.Parameters.AddWithValue("$invocation", invocationId);
        return command.ExecuteScalar() as string;
    }

    internal IReadOnlyList<AssessmentRecord> ReadSelectedWords(
        SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<AssessmentRecord> headers, IReadOnlyCollection<string> wordForms)
    {
        ArgumentNullException.ThrowIfNull(headers);
        foreach (var header in headers) header.RequireWords();
        var selected = wordForms.ToHashSet(StringComparer.Ordinal);
        return headers.Select(header => header with
        {
            Details = LoadWordDetails(connection, header, ReadWords(connection, header.AssessmentId, selected, transaction), ReadObjectTimings(connection, header.AssessmentId, transaction), transaction),
        }).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<AssessmentRecord> ReadBaselineAssessmentWords(
        string kind, string baselineToken, IReadOnlyList<string>? assessmentIds, IReadOnlyCollection<string> wordForms)
    {
        if (kind.IsStoredKind(AssessmentKind.PhonologyExamples))
            throw new ArgumentException("This read requires a Words Assessment kind.");
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        ArgumentNullException.ThrowIfNull(wordForms);
        if (wordForms.Count == 0 || assessmentIds is { Count: 0 }) return [];

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var headers = new List<AssessmentRecord>();
        if (assessmentIds is not null)
        {
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT value FROM json_each($ids) WHERE NOT EXISTS " +
                "(SELECT 1 FROM Assessments WHERE AssessmentId = value) LIMIT 1;";
            check.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(assessmentIds));
            if (check.ExecuteScalar() is string missing) throw new AssessmentExpiredException(missing);
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = assessmentIds is null
                ? HeaderSelectSql + " WHERE Kind = $kind AND ProposalId IS NULL AND BaselineToken = $baseline " +
                  "ORDER BY SavedUtc DESC, AssessmentId DESC LIMIT 1;"
                : HeaderSelectSql + " WHERE Kind = $kind AND ProposalId IS NULL AND BaselineToken = $baseline " +
                  "AND AssessmentId IN (SELECT value FROM json_each($ids)) ORDER BY SavedUtc, AssessmentId;";
            command.Parameters.AddWithValue("$kind", kind);
            command.Parameters.AddWithValue("$baseline", baselineToken);
            if (assessmentIds is not null)
                command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(assessmentIds));
            RepositoryReadCounters.QueryExecuted();
            using var reader = command.ExecuteReader();
            while (reader.Read()) headers.Add(ReadHeader(reader));
        }

        var selectedForms = wordForms.ToHashSet(StringComparer.Ordinal);
        return headers.Select(header => header with
        {
            Details = LoadWordDetails(connection, header, ReadWords(connection, header.AssessmentId, selectedForms, transaction),
                new StoredTimings([], null), transaction),
        }).ToArray();
    }

    /// <inheritdoc />
    public void PromoteToCurrent(string assessmentId)
    {
        if (string.IsNullOrWhiteSpace(assessmentId))
            throw new ArgumentException("An Assessment id is required.", nameof(assessmentId));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT SubjectKind FROM Assessments WHERE AssessmentId = $id;";
            check.Parameters.AddWithValue("$id", assessmentId);
            var subjectKind = check.ExecuteScalar() as string
                ?? throw new KeyNotFoundException($"Assessment '{assessmentId}' was not found.");
            if (subjectKind != "words") throw new InvalidOperationException("Only Words Assessments can be current.");
        }
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE MotifMetadata SET CurrentAssessmentId = $id WHERE Id = 1;";
        update.Parameters.AddWithValue("$id", assessmentId);
        update.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <inheritdoc />
    public AssessmentRecord? GetCurrent()
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        string? currentId;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT CurrentAssessmentId FROM MotifMetadata WHERE Id = 1;";
            RepositoryReadCounters.QueryExecuted();
            currentId = command.ExecuteScalar() as string;
        }
        if (currentId is null) return null;

        var header = ReadHeader(connection, transaction, currentId) ?? throw new InvalidDataException(
            $"MotifMetadata points at current Assessment '{currentId}', which is not recorded " +
            "(store inconsistency).");
        header.RequireWords();
        return header with
        {
            Details = LoadWordDetails(connection, header, ReadWords(connection, currentId, null, transaction),
                ReadObjectTimings(connection, currentId, transaction), transaction)
        };
    }

    /// <inheritdoc />
    public void DeleteByProposal(CanonicalId proposalId, string? exceptAssessmentId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        AssessmentRetention.SweepProposal(connection, transaction, proposalId.Value, exceptAssessmentId);
        transaction.Commit();
    }

    private static void InsertInvocation(
        SqliteConnection connection, SqliteTransaction transaction, BatchInvocationEvidence invocation,
        string invocationJson)
    {
        if (string.IsNullOrWhiteSpace(invocation.InvocationId))
            throw new ArgumentException("An invocation id is required.", nameof(invocation));
        using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT EvidenceJson FROM AssessmentInvocations WHERE InvocationId = $id;";
        find.Parameters.AddWithValue("$id", invocation.InvocationId);
        if (find.ExecuteScalar() is string existing)
        {
            if (ReadInvocation(existing) != invocation)
                throw new InvalidOperationException($"Invocation '{invocation.InvocationId}' already records different evidence.");
            return;
        }
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO AssessmentInvocations (InvocationId, EvidenceJson) VALUES ($id, $evidence);";
        insert.Parameters.AddWithValue("$id", invocation.InvocationId);
        insert.Parameters.AddWithValue("$evidence", invocationJson);
        insert.ExecuteNonQuery();
    }

    private static void InsertHeader(
        SqliteConnection connection, SqliteTransaction transaction, PreparedAssessment prepared)
    {
        var assessment = prepared.Record;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Assessments
                (AssessmentId, SubjectKind, SubjectJson, SelectionName, SelectionWordsJson, SelectionSha256, SelectionProvenanceJson,
                 OutcomeDigest, SemanticDigest, GrammarSourceSha256, ModelFingerprint, Pipeline,
                 DiagnosticCount, SavedUtc, ProposalId, ProposalIntentDigest, Assessor, Kind,
                 ScopeJson, ScopeDigest, TokeniserName, TokeniserVersion, BaselineToken, CachePath, CacheDigest, InvocationId, ReplacesAssessmentId)
            VALUES
                ($id, $subjectKind, $subjectJson, $selectionName, $selectionWords, $selectionSha, $selectionProvenance,
                 $outcomeDigest, $semanticDigest, $grammarSha, $modelFingerprint, $pipeline,
                 $diagnosticCount, $savedUtc, $proposalId, $proposalIntentDigest, $assessor, $kind,
                 $scopeJson, $scopeDigest, $tokeniserName, $tokeniserVersion, $baselineToken, $cachePath, $cacheDigest, $invocationId, $replacesAssessmentId);
            """;
        var words = assessment.Subject as AssessmentSubject.Words;
        var material = assessment.Material as AssessmentRecordMaterial.Words;
        command.Parameters.AddWithValue("$id", assessment.AssessmentId);
        command.Parameters.AddWithValue("$subjectKind", words is null ? "phonologyExamples" : "words");
        command.Parameters.AddWithValue("$subjectJson", words is null
            ? JsonSerializer.Serialize(((AssessmentSubject.PhonologyExamples)assessment.Subject).Revision,
                SIL.Motif.Contract.Phonology.PhonologyJson.CreateOptions()) : DBNull.Value);
        command.Parameters.AddWithValue("$selectionName", (object?)words?.Selection.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("$selectionWords", (object?)prepared.SelectionWordsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$selectionSha", (object?)words?.Selection.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$selectionProvenance",
            (object?)prepared.SelectionProvenanceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$outcomeDigest", (object?)assessment.OutcomeDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("$semanticDigest", (object?)assessment.SemanticDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("$grammarSha", assessment.GrammarSourceSha256);
        command.Parameters.AddWithValue("$modelFingerprint", (object?)assessment.ModelFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$pipeline", (object?)assessment.Pipeline ?? DBNull.Value);
        command.Parameters.AddWithValue("$diagnosticCount", (object?)assessment.DiagnosticCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$savedUtc",
            assessment.SavedUtc ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$proposalId", (object?)assessment.ProposalId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$proposalIntentDigest", (object?)assessment.ProposalIntentDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("$assessor", assessment.Assessor);
        command.Parameters.AddWithValue("$kind", assessment.Kind);
        command.Parameters.AddWithValue("$scopeJson", assessment.ScopeJson);
        command.Parameters.AddWithValue("$scopeDigest", assessment.ScopeDigest);
        command.Parameters.AddWithValue("$tokeniserName", (object?)words?.TokeniserName ?? DBNull.Value);
        command.Parameters.AddWithValue("$tokeniserVersion", (object?)words?.TokeniserVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$baselineToken", assessment.BaselineToken);
        command.Parameters.AddWithValue("$cachePath", (object?)material?.CachePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$cacheDigest", (object?)material?.CacheDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("$invocationId", (object?)material?.Invocation?.InvocationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$replacesAssessmentId", (object?)assessment.ReplacesAssessmentId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertWordsAndAnalyses(
        SqliteConnection connection, SqliteTransaction transaction, PreparedAssessment prepared)
    {
        if (prepared.Words.Count == 0) return;
        // AUTOINCREMENT never reuses an id, so the next one is past both the sequence and the table.
        using var next = connection.CreateCommand();
        next.Transaction = transaction;
        next.CommandText = """
            SELECT MAX(COALESCE((SELECT seq FROM sqlite_sequence WHERE name = 'AssessedWords'), 0),
                       COALESCE((SELECT MAX(AssessedWordId) FROM AssessedWords), 0));
            """;
        var highWater = (long)next.ExecuteScalar()!;
        // Ids never wrap or repeat; the Assessment rolls back instead (pinned by `IdAllocationNeverWraps`).
        if (highWater > long.MaxValue - prepared.Words.Count)
            throw new InvalidOperationException(
                "The project's store has no assessed-word ids left, so the Assessment was not recorded.");
        var firstId = highWater + 1;
        for (var index = 0; index < prepared.Words.Count; index++) prepared.Words[index][0] = checked(firstId + index);
        InsertRows(connection, transaction, "AssessedWords", WordColumns, prepared.Words);
        foreach (var (word, values) in prepared.Analyses) values[0] = checked(firstId + word);
        InsertRows(connection, transaction, "ParsedAnalyses", AnalysisColumns,
            prepared.Analyses.Select(analysis => analysis.Values).ToArray());
    }

    // Many rows per statement keep the write lock to a few statements per hundred words.
    private const int RowsPerStatement = 100;

    private static void InsertRows(SqliteConnection connection, SqliteTransaction transaction, string table,
        IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, string suffix = "")
    {
        for (var start = 0; start < rows.Count; start += RowsPerStatement)
        {
            var count = Math.Min(RowsPerStatement, rows.Count - start);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var sql = new StringBuilder("INSERT INTO ").Append(table).Append(" (")
                .AppendJoin(", ", columns).Append(") VALUES ");
            for (var row = 0; row < count; row++)
            {
                sql.Append(row == 0 ? "(" : ", (");
                for (var column = 0; column < columns.Count; column++)
                {
                    var name = "$p" + row.ToString(CultureInfo.InvariantCulture) + "_" +
                        column.ToString(CultureInfo.InvariantCulture);
                    sql.Append(column == 0 ? name : ", " + name);
                    command.Parameters.AddWithValue(name, rows[start + row][column] ?? DBNull.Value);
                }
                sql.Append(')');
            }
            command.CommandText = sql.Append(suffix).Append(';').ToString();
            command.ExecuteNonQuery();
        }
    }

    private sealed record StoredTimings(IReadOnlyList<AssessmentObjectTiming> Rows, AssessmentTimingEvidence? Evidence);

    private static StoredTimings ReadObjectTimings(SqliteConnection connection,
        string assessmentId, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Detail, Evidence FROM AssessmentObjectTimings WHERE AssessmentId = $id;";
        command.Parameters.AddWithValue("$id", assessmentId);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new StoredTimings([], null);
        var rows = JsonSerializer.Deserialize<AssessmentObjectTiming[]>(Decompress(reader, 0))
            ?? throw new InvalidDataException("Stored object timing detail is missing.");
        var evidence = reader.IsDBNull(1) ? null : JsonSerializer.Deserialize<AssessmentTimingEvidence>(Decompress(reader, 1))
            ?? throw new InvalidDataException("Stored object timing evidence is missing.");
        return new StoredTimings(rows, evidence);
    }

    private const string HeaderSelectSql = """
        SELECT AssessmentId, ProposalId, ProposalIntentDigest, Assessor, Kind, ScopeJson, ScopeDigest,
               TokeniserName, TokeniserVersion, BaselineToken, SelectionName, SelectionWordsJson, SelectionSha256,
               SelectionProvenanceJson, OutcomeDigest, SemanticDigest, GrammarSourceSha256, ModelFingerprint,
               Pipeline, DiagnosticCount, SavedUtc, CachePath, CacheDigest,
               (SELECT EvidenceJson FROM AssessmentInvocations ai WHERE ai.InvocationId = Assessments.InvocationId),
               ReplacesAssessmentId, SubjectKind, SubjectJson
        FROM Assessments
        """;

    private static AssessmentRecord? ReadHeader(SqliteConnection connection, SqliteTransaction? transaction, string assessmentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = HeaderSelectSql + " WHERE AssessmentId = $id;";
        command.Parameters.AddWithValue("$id", assessmentId);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadHeader(reader) : null;
    }

    private static AssessmentRecord? GetHeader(SqliteConnection connection, string assessmentId) =>
        ReadHeader(connection, null, assessmentId);

    private static AssessmentRecord ReadHeader(SqliteDataReader reader)
    {
        RepositoryReadCounters.RecordDeserialized();
        AssessmentSubject subject = reader.GetString(25) switch
        {
            "words" => new AssessmentSubject.Words(new Selection(reader.GetString(10),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(11))!, reader.GetString(12),
                reader.IsDBNull(13) ? null : JsonSerializer.Deserialize<CorpusProvenance>(reader.GetString(13))),
                reader.GetString(7), reader.GetString(8)),
            "phonologyExamples" => new AssessmentSubject.PhonologyExamples(
                JsonSerializer.Deserialize<SIL.Motif.Contract.Phonology.PhonologyRevisionReference>(reader.GetString(26),
                    SIL.Motif.Contract.Phonology.PhonologyJson.CreateOptions())
                ?? throw new InvalidDataException("Missing phonology subject.")),
            _ => throw new InvalidDataException("Unknown stored Assessment subject."),
        };
        return new AssessmentRecord(
            AssessmentId: reader.GetString(0),
            ProposalId: reader.IsDBNull(1) ? null : CanonicalId.Parse(reader.GetString(1)),
            ProposalIntentDigest: reader.IsDBNull(2) ? null : reader.GetString(2),
            Assessor: reader.GetString(3),
            Kind: reader.GetString(4),
            ScopeJson: reader.GetString(5),
            ScopeDigest: reader.GetString(6),
            Subject: subject,
            BaselineToken: reader.GetString(9),
            OutcomeDigest: reader.IsDBNull(14) ? null : reader.GetString(14),
            SemanticDigest: reader.IsDBNull(15) ? null : reader.GetString(15),
            GrammarSourceSha256: reader.GetString(16),
            ModelFingerprint: reader.IsDBNull(17) ? null : reader.GetString(17),
            Pipeline: reader.IsDBNull(18) ? null : reader.GetString(18),
            DiagnosticCount: reader.IsDBNull(19) ? null : reader.GetInt32(19),
            SavedUtc: reader.GetString(20),
            Details: subject is AssessmentSubject.Words
                ? new AssessmentRecordDetails.Unloaded(reader.IsDBNull(21) ? null : reader.GetString(21),
                    reader.IsDBNull(22) ? null : reader.GetString(22),
                    reader.IsDBNull(23) ? null : ReadInvocation(reader.GetString(23)))
                : new AssessmentRecordDetails.Unloaded())
        {
            ReplacesAssessmentId = reader.IsDBNull(24) ? null : reader.GetString(24)
        };
    }

    private static AssessmentRecordDetails.Words LoadWordDetails(SqliteConnection connection,
        AssessmentRecord header, IReadOnlyList<AssessedWord> rows, StoredTimings timings,
        SqliteTransaction? transaction = null)
    {
        header.RequireWords();
        return new AssessmentRecordDetails.Words(rows, header.CachePath, header.CacheDigest, timings.Rows, header.Invocation)
            { TimingEvidence = timings.Evidence };
    }

    private static BatchInvocationEvidence ReadInvocation(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BatchInvocationEvidence>(json, InvocationJsonOptions)
                ?? throw new JsonException("Missing invocation evidence.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The stored invocation evidence is obsolete or invalid; delete the project .motif.db file and recreate the Assessments.",
                exception);
        }
    }

    private static byte[] Compress(string value)
    {
        using var output = new MemoryStream();
        using (var compressed = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            compressed.Write(Encoding.UTF8.GetBytes(value));
        return output.ToArray();
    }

    private static string Decompress(SqliteDataReader reader, int ordinal)
    {
        using var input = new MemoryStream((byte[])reader.GetValue(ordinal), writable: false);
        using var compressed = new BrotliStream(input, CompressionMode.Decompress);
        using var text = new StreamReader(compressed, Encoding.UTF8);
        return text.ReadToEnd();
    }

    // One streaming pass over a word/analysis join, grouped by word — no N+1 querying.
    private static List<AssessedWord> ReadWords(SqliteConnection connection, string assessmentId,
        IReadOnlySet<string>? wordForms = null, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT aw.AssessedWordId, aw.Word, aw.Outcome, aw.ElapsedMs, aw.ElapsedNs,
                   pa.CategoryGuid, pa.MorphemeGuidsJson, pa.RootIndex, pa.IdentityDigest, aw.RawSignature,
                   (SELECT Payload FROM AssessmentWordPayloads WHERE Hash = aw.MorphologyJson), (SELECT Payload FROM AssessmentWordPayloads WHERE Hash = aw.CorrectnessJson), aw.OrdinalIndex, aw.ProjectStanding, aw.OccurrenceCount,
                   (SELECT Payload FROM AssessmentWordPayloads WHERE Hash = aw.ReadingGradesJson), aw.MissedApprovedCount, aw.IsIncomplete, (SELECT Payload FROM AssessmentWordPayloads WHERE Hash = aw.MissedApprovedJson), aw.Attempts, aw.Passes, (SELECT Payload FROM AssessmentWordPayloads WHERE Hash = aw.AnalysisComparisonJson),
                   aw.WorkSteps,
                   (aw.MorphologyJson IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AssessmentWordPayloads WHERE Hash = aw.MorphologyJson)) OR
                   (aw.CorrectnessJson IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AssessmentWordPayloads WHERE Hash = aw.CorrectnessJson)) OR
                   (aw.ReadingGradesJson IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AssessmentWordPayloads WHERE Hash = aw.ReadingGradesJson)) OR
                   (aw.AnalysisComparisonJson IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AssessmentWordPayloads WHERE Hash = aw.AnalysisComparisonJson)) OR
                   (aw.MissedApprovedJson IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AssessmentWordPayloads WHERE Hash = aw.MissedApprovedJson))
            FROM AssessedWords aw
            LEFT JOIN ParsedAnalyses pa ON pa.AssessedWordId = aw.AssessedWordId
            WHERE aw.AssessmentId = $id
              AND ($forms IS NULL OR aw.Word IN (SELECT value FROM json_each($forms)))
            ORDER BY aw.OrdinalIndex, pa.OrdinalIndex;
            """;
        command.Parameters.AddWithValue("$id", assessmentId);
        command.Parameters.AddWithValue("$forms", wordForms is null ? DBNull.Value :
            JsonSerializer.Serialize(wordForms));

        var words = new List<AssessedWord>();
        long? currentWordId = null;
        string currentWord = "";
        string currentOutcome = "";
        int? currentElapsedMs = null;
        long? currentElapsedNs = null;
        string? currentSignature = null;
        ParseWordEvidence? currentMorphology = null;
        WordCorrectness? currentCorrectness = null;
        string? currentStanding = null;
        int? currentOccurrenceCount = null;
        IReadOnlyList<string>? currentReadingGrades = null;
        WordAnalysisComparison? currentComparison = null;
        int? currentMissedApprovedCount = null;
        IReadOnlyList<ParserReading>? currentMissedApproved = null;
        bool currentIncomplete = false;
        int? currentAttempts = null;
        int? currentPasses = null;
        long? currentWorkSteps = null;
        List<ParsedAnalysis> currentAnalyses = [];

        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var wordId = reader.GetInt64(0);
            if (wordId != currentWordId)
            {
                if (reader.GetBoolean(23)) throw new InvalidDataException("An Assessment word payload is missing.");
                if (currentWordId is not null)
                {
                    RepositoryReadCounters.RecordDeserialized();
                    words.Add(new AssessedWord(currentWord, currentOutcome, currentAnalyses, currentElapsedMs, currentSignature)
                    { Morphology = currentMorphology, ElapsedNs = currentElapsedNs, Correctness = currentCorrectness,
                        ProjectStanding = currentStanding, OccurrenceCount = currentOccurrenceCount,
                        ReadingGrades = currentReadingGrades, AnalysisComparison = currentComparison, MissedApprovedCount = currentMissedApprovedCount,
                        MissedApproved = currentMissedApproved, IsIncomplete = currentIncomplete,
                        Attempts = currentAttempts, Passes = currentPasses, WorkSteps = currentWorkSteps });
                }
                if (wordForms is null && reader.GetInt32(12) != words.Count)
                    throw new InvalidDataException("Assessment case ordinals must be contiguous and begin at zero.");
                currentWordId = wordId;
                currentWord = reader.GetString(1);
                currentOutcome = reader.GetString(2);
                currentElapsedMs = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                currentElapsedNs = reader.IsDBNull(4) ? null : reader.GetInt64(4);
                currentSignature = reader.IsDBNull(9) ? null : reader.GetString(9);
                currentMorphology = reader.IsDBNull(10) ? null
                    : JsonSerializer.Deserialize<ParseWordEvidence>(Decompress(reader, 10), ParseMorphEvidence.JsonOptions);
                currentCorrectness = reader.IsDBNull(11) ? null
                    : JsonSerializer.Deserialize<WordCorrectness>(Decompress(reader, 11), ParseMorphEvidence.JsonOptions);
                currentStanding = reader.IsDBNull(13) ? null : reader.GetString(13);
                currentOccurrenceCount = reader.IsDBNull(14) ? null : reader.GetInt32(14);
                currentReadingGrades = reader.IsDBNull(15) ? null : JsonSerializer.Deserialize<string[]>(Decompress(reader, 15));
                currentMissedApprovedCount = reader.IsDBNull(16) ? null : reader.GetInt32(16);
                currentIncomplete = reader.GetInt32(17) != 0;
                currentMissedApproved = reader.IsDBNull(18) ? null
                    : JsonSerializer.Deserialize<ParserReading[]>(Decompress(reader, 18), ParseMorphEvidence.JsonOptions);
                currentAttempts = reader.IsDBNull(19) ? null : reader.GetInt32(19);
                currentPasses = reader.IsDBNull(20) ? null : reader.GetInt32(20);
                currentComparison = reader.IsDBNull(21) ? null
                    : JsonSerializer.Deserialize<WordAnalysisComparison>(Decompress(reader, 21), ParseMorphEvidence.JsonOptions);
                currentWorkSteps = reader.IsDBNull(22) ? null : reader.GetInt64(22);
                currentAnalyses = [];
            }

            if (!reader.IsDBNull(8)) // NULL here means no analysis row; the column itself is NOT NULL.
            {
                RepositoryReadCounters.RecordDeserialized();
                currentAnalyses.Add(new ParsedAnalysis(
                    CategoryGuid: reader.IsDBNull(5) ? null : reader.GetString(5),
                    MorphemeGuids: JsonSerializer.Deserialize<List<string>>(reader.GetString(6))!,
                    RootIndex: reader.GetInt32(7),
                    IdentityDigest: reader.GetString(8)));
            }
        }

        if (currentWordId is not null)
        {
            RepositoryReadCounters.RecordDeserialized();
            words.Add(new AssessedWord(currentWord, currentOutcome, currentAnalyses, currentElapsedMs, currentSignature)
            { Morphology = currentMorphology, ElapsedNs = currentElapsedNs, Correctness = currentCorrectness,
                ProjectStanding = currentStanding, OccurrenceCount = currentOccurrenceCount,
                ReadingGrades = currentReadingGrades, AnalysisComparison = currentComparison, MissedApprovedCount = currentMissedApprovedCount,
                MissedApproved = currentMissedApproved, IsIncomplete = currentIncomplete,
                        Attempts = currentAttempts, Passes = currentPasses, WorkSteps = currentWorkSteps });
        }
        return words;
    }
}
