using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Receipts;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;

namespace SIL.Motif.Worker.Store;

internal enum ArtifactStoreKind
{
    Project,
    ParsimonyEvidence,
}

internal enum ArtifactReferenceFamily
{
    Assessments,
    InvocationArtifacts,
    Reports,
    ProposalDifferences,
    Baselines,
    Jobs,
    WorkflowRecords,
    ParsimonyBundles,
    ParsimonyEvidence,
}

internal enum ArtifactReferenceTargetKind
{
    Assessment,
    Invocation,
    Path,
    BaselineBundleDigest,
    ParsimonyBundle,
    ContentDigest,
    Job,
}

internal sealed class PrivateArtifactStoreSnapshot
{
    private PrivateArtifactStoreSnapshot(string storeIdentity, ArtifactStoreKind storeKind, int schemaVersion,
        string snapshotFingerprint, SqliteConnection? connection, SqliteTransaction? transaction,
        string? readFailure, string? relatedBundleId)
    {
        StoreIdentity = RequireText(storeIdentity, nameof(storeIdentity));
        if (!Enum.IsDefined(storeKind)) throw new ArgumentOutOfRangeException(nameof(storeKind));
        StoreKind = storeKind;
        DeclaredSchemaVersion = schemaVersion;
        SnapshotFingerprint = RequireText(snapshotFingerprint, nameof(snapshotFingerprint));
        Connection = connection;
        Transaction = transaction;
        ReadFailure = readFailure;
        RelatedBundleId = relatedBundleId;
        if ((connection is null) != (transaction is null))
            throw new ArgumentException("A private snapshot needs both its connection and transaction.");
        if ((connection is null) == (readFailure is null))
            throw new ArgumentException("A private snapshot is either readable or has a read failure.");
        if (storeKind == ArtifactStoreKind.ParsimonyEvidence && string.IsNullOrWhiteSpace(relatedBundleId))
            throw new ArgumentException("A Parsimony evidence snapshot needs its declared bundle id.", nameof(relatedBundleId));
    }

    internal string StoreIdentity { get; }
    internal ArtifactStoreKind StoreKind { get; }
    internal int DeclaredSchemaVersion { get; }
    internal string SnapshotFingerprint { get; }
    internal SqliteConnection? Connection { get; }
    internal SqliteTransaction? Transaction { get; }
    internal string? ReadFailure { get; }
    internal string? RelatedBundleId { get; }

    internal static PrivateArtifactStoreSnapshot FromPrivateCopy(string storeIdentity, ArtifactStoreKind storeKind,
        int schemaVersion, string snapshotFingerprint, SqliteConnection connection, SqliteTransaction transaction,
        string? relatedBundleId = null) =>
        new(storeIdentity, storeKind, schemaVersion, snapshotFingerprint, connection, transaction, null, relatedBundleId);

    internal static PrivateArtifactStoreSnapshot Unreadable(string storeIdentity, ArtifactStoreKind storeKind,
        int? schemaVersion, string snapshotFingerprint, string failure, string? relatedBundleId = null) =>
        new(storeIdentity, storeKind, schemaVersion ?? 0, snapshotFingerprint, null, null,
            RequireText(failure, nameof(failure)), relatedBundleId);

    private static string RequireText(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A nonblank value is required.", name) : value;
}

internal sealed record ArtifactReferenceDomainDeclaration(
    IReadOnlyList<string> StoreIdentities,
    bool CallerDeclaredComplete,
    string Basis)
{
    internal static ArtifactReferenceDomainDeclaration Explicit(IReadOnlyList<string> storeIdentities,
        bool callerDeclaredComplete, string basis) => new(Array.AsReadOnly(storeIdentities.ToArray()),
        callerDeclaredComplete, string.IsNullOrWhiteSpace(basis)
            ? throw new ArgumentException("A domain declaration needs its basis.", nameof(basis))
            : basis);
}

internal sealed record ArtifactStoreIdentity(string StoreIdentity, ArtifactStoreKind StoreKind, int SchemaVersion,
    string SnapshotFingerprint, string? RelatedBundleId);

internal sealed record ArtifactReference(ArtifactReferenceFamily Family, string StoreIdentity,
    ArtifactReferenceTargetKind TargetKind, string Target, string RetainingRecordId, string Reason,
    string? ScopeIdentity = null);

internal sealed record ArtifactRetentionRecord(ArtifactReferenceFamily Family, string StoreIdentity,
    string RecordId, string Reason, bool FamilyScopedUncertainty);

internal sealed record ArtifactInvocationProvenance(string StoreIdentity, string InvocationId,
    bool HasDurableProvenance, bool HasAssessment, bool FilesUnknownOwned, string RetainingRecordId);

internal sealed record ArtifactReferenceDiagnostic(ArtifactReferenceFamily Family, string? StoreIdentity,
    string Code, string? RecordId);

internal sealed record ArtifactReferenceFamilyStatus(ArtifactReferenceFamily Family, bool IsComplete);

internal sealed record ArtifactReferenceDomainStatus(bool IsComplete, bool CallerDeclaredComplete,
    string Basis, IReadOnlyList<string> MissingStoreIdentities, IReadOnlyList<string> UnexpectedStoreIdentities);

internal sealed record ArtifactBaselinePublication(string StoreIdentity, string ProjectIdentity, string BundleDigest,
    string RootDirectory, string FwDataPath, string RetainingRecordId);

internal sealed class ArtifactReferenceSnapshot(
    IReadOnlyList<ArtifactStoreIdentity> stores,
    ArtifactReferenceDomainStatus referenceDomain,
    IReadOnlyList<ArtifactReference> references,
    IReadOnlyList<ArtifactRetentionRecord> retentionRecords,
    IReadOnlyList<ArtifactBaselinePublication> baselinePublications,
    IReadOnlyList<ArtifactInvocationProvenance> invocationProvenance,
    IReadOnlyList<ArtifactReferenceFamilyStatus> familyCompleteness,
    IReadOnlyList<ArtifactReferenceDiagnostic> diagnostics)
{
    internal IReadOnlyList<ArtifactStoreIdentity> Stores { get; } = Array.AsReadOnly(stores.ToArray());
    internal ArtifactReferenceDomainStatus ReferenceDomain { get; } = referenceDomain with
    {
        MissingStoreIdentities = Array.AsReadOnly(referenceDomain.MissingStoreIdentities.ToArray()),
        UnexpectedStoreIdentities = Array.AsReadOnly(referenceDomain.UnexpectedStoreIdentities.ToArray()),
    };
    internal IReadOnlyList<ArtifactReference> References { get; } = Array.AsReadOnly(references.ToArray());
    internal IReadOnlyList<ArtifactRetentionRecord> RetentionRecords { get; } = Array.AsReadOnly(retentionRecords.ToArray());
    internal IReadOnlyList<ArtifactBaselinePublication> BaselinePublications { get; } =
        Array.AsReadOnly(baselinePublications.ToArray());
    internal IReadOnlyList<ArtifactInvocationProvenance> InvocationProvenance { get; } =
        Array.AsReadOnly(invocationProvenance.ToArray());
    internal IReadOnlyList<ArtifactReferenceFamilyStatus> FamilyCompleteness { get; } =
        Array.AsReadOnly(familyCompleteness.ToArray());
    internal IReadOnlyList<ArtifactReferenceDiagnostic> Diagnostics { get; } = Array.AsReadOnly(diagnostics.ToArray());
    internal IReadOnlyList<string> AssessmentIds => Values(ArtifactReferenceTargetKind.Assessment);
    internal IReadOnlyList<string> InvocationIds => Values(ArtifactReferenceTargetKind.Invocation);
    internal IReadOnlyList<string> Paths => Values(ArtifactReferenceTargetKind.Path);

    private IReadOnlyList<string> Values(ArtifactReferenceTargetKind kind) => Array.AsReadOnly(References
        .Where(reference => reference.TargetKind == kind).Select(reference => reference.Target)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
}

internal static class GeneratedArtifactReferences
{
    private static readonly JsonSerializerOptions JsonOptions = MotifJson.CreateOptions();
    private static readonly JsonSerializerOptions StrictJsonOptions = CreateStrictJsonOptions();
    private static readonly JsonSerializerOptions StrictWebJsonOptions = CreateStrictWebJsonOptions();

    internal static ArtifactReferenceSnapshot Capture(IReadOnlyList<PrivateArtifactStoreSnapshot> stores,
        ArtifactReferenceDomainDeclaration declaredDomain)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(declaredDomain);
        var builder = new SnapshotBuilder(stores, declaredDomain);
        foreach (var store in stores.OrderBy(item => item.StoreIdentity, StringComparer.Ordinal))
        {
            var families = FamiliesFor(store.StoreKind);
            if (store.ReadFailure is not null)
            {
                foreach (var family in families) builder.Incomplete(family, store.StoreIdentity, "declared-store-unreadable", null);
                builder.IncompleteDomain();
                continue;
            }

            if (store.Connection is null || store.Transaction is null)
            {
                foreach (var family in families) builder.Incomplete(family, store.StoreIdentity, "snapshot-connection-missing", null);
                builder.IncompleteDomain();
                continue;
            }

            try
            {
                ValidateStoreIdentity(store);
            }
            catch (Exception exception) when (IsStoredDataFailure(exception))
            {
                foreach (var family in families)
                    builder.Incomplete(family, store.StoreIdentity, "unsupported-or-invalid-store-snapshot", null);
                builder.IncompleteDomain();
                continue;
            }

            if (store.StoreKind == ArtifactStoreKind.Project)
                CaptureProjectStore(store, builder);
            else
                CaptureTable(builder, ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                    () => CaptureParsimonyEvidence(store, builder));
        }

        ResolveBaselineTokens(builder);
        CheckParsimonySnapshotCoverage(builder);
        return builder.Build();
    }

    private static void ValidateStoreIdentity(PrivateArtifactStoreSnapshot store)
    {
        var connection = store.Connection!;
        var transaction = store.Transaction!;
        var expectedApplicationId = store.StoreKind == ArtifactStoreKind.Project
            ? MotifSchema.ApplicationId : EvidenceSchema.ApplicationId;
        var expectedVersion = store.StoreKind == ArtifactStoreKind.Project
            ? MotifSchema.CurrentSchema : EvidenceSchema.Version;
        var applicationId = ReadPragmaInt(connection, transaction, "application_id");
        var schemaVersion = ReadPragmaInt(connection, transaction, "user_version");
        if (store.DeclaredSchemaVersion != expectedVersion || applicationId != expectedApplicationId ||
            schemaVersion != expectedVersion)
            throw new InvalidDataException("The supplied private store snapshot is not at its current schema.");
    }

    private static int ReadPragmaInt(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA " + name + ";";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void CaptureProjectStore(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        CaptureTable(builder, ArtifactReferenceFamily.Assessments, store.StoreIdentity,
            () => CaptureAssessments(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Assessments, store.StoreIdentity,
            () => CaptureCurrentAssessment(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Assessments, store.StoreIdentity,
            () => CaptureAssessmentPins(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Assessments, store.StoreIdentity,
            () => CaptureRetainedInvocationMembers(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity,
            () => CaptureInvocations(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Reports, store.StoreIdentity,
            () => CaptureReports(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.ProposalDifferences, store.StoreIdentity,
            () => CaptureDifferences(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity,
            () => CaptureBaselines(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity,
            () => CaptureRetainedInvocations(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity,
            () => CaptureGrammarChecks(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.Jobs, store.StoreIdentity,
            () => CaptureJobs(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
            () => CaptureWorkflowRecords(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity,
            () => CaptureParsimonyBundles(store, builder));
    }

    private static void CaptureTable(SnapshotBuilder builder, ArtifactReferenceFamily family, string storeIdentity,
        Action capture)
    {
        try
        {
            capture();
        }
        catch (Exception exception) when (IsStoredDataFailure(exception))
        {
            builder.Incomplete(family, storeIdentity, "declared-table-unreadable-or-unrecognized", null);
        }
    }

    private static void CaptureAssessments(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT AssessmentId, InvocationId, CachePath, BaselineToken, " +
            "ReplacesAssessmentId FROM Assessments ORDER BY AssessmentId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var recordId = "Assessments/" + id;
            builder.Retain(ArtifactReferenceFamily.Assessments, store.StoreIdentity, recordId, "assessment-history");
            builder.Reference(ArtifactReferenceFamily.Assessments, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, id, recordId, "assessment-history");
            AddOptionalReference(builder, ArtifactReferenceFamily.Assessments, store.StoreIdentity,
                ArtifactReferenceTargetKind.Invocation, NullableString(reader, 1), recordId, "assessment-invocation");
            AddOptionalReference(builder, ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity,
                ArtifactReferenceTargetKind.Path, NullableString(reader, 2), recordId, "assessment-cache-path");
            AddOptionalReference(builder, ArtifactReferenceFamily.Assessments, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, NullableString(reader, 4), recordId, "replacement-lineage");
            try
            {
                AddBaselineToken(builder, store.StoreIdentity, reader.GetString(3), recordId, "assessment-baseline-token");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception))
            {
                builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                    "assessment-baseline-token-unrecognized", recordId);
            }
        }
    }

    private static void CaptureCurrentAssessment(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store,
            "SELECT CurrentAssessmentId FROM MotifMetadata WHERE Id = 1;");
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("The project metadata row is absent.");
        var id = NullableString(reader, 0);
        if (id is not null)
            builder.Reference(ArtifactReferenceFamily.Assessments, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, id, "MotifMetadata/1", "current-assessment");
        if (reader.Read()) throw new InvalidDataException("The project metadata row is not unique.");
    }

    private static void CaptureAssessmentPins(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store,
            "SELECT AssessmentId, PinnedBy FROM AssessmentPins ORDER BY AssessmentId, PinnedBy;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var recordId = "AssessmentPins/" + id + "/" + reader.GetString(1);
            builder.Reference(ArtifactReferenceFamily.Assessments, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, id, recordId, "assessment-pin");
            builder.Retain(ArtifactReferenceFamily.Assessments, store.StoreIdentity, recordId, "assessment-pin");
        }
    }

    private static void CaptureRetainedInvocationMembers(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store,
            "SELECT InvocationId, Kind, AssessmentId FROM RetainedInvocationMembers ORDER BY InvocationId, Kind;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var invocationId = reader.GetString(0);
            var kind = reader.GetString(1);
            var recordId = "RetainedInvocationMembers/" + invocationId + "/" + kind;
            builder.Reference(ArtifactReferenceFamily.Assessments, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, reader.GetString(2), recordId, "retained-invocation-member");
            builder.Retain(ArtifactReferenceFamily.Assessments, store.StoreIdentity, recordId,
                "retained-invocation-member");
            if (!Enum.TryParse<AssessmentKind>(kind, false, out var parsedKind) ||
                !Enum.IsDefined(parsedKind) || !StringComparer.Ordinal.Equals(parsedKind.ToString(), kind))
                builder.Incomplete(ArtifactReferenceFamily.Assessments, store.StoreIdentity,
                    "retained-invocation-member-kind-unrecognized", recordId);
        }
    }

    private static void CaptureInvocations(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        var assessmentInvocations = builder.References.Where(reference => reference.StoreIdentity == store.StoreIdentity &&
                reference.TargetKind == ArtifactReferenceTargetKind.Invocation)
            .Select(reference => reference.Target).ToHashSet(StringComparer.Ordinal);
        using var command = Command(store,
            "SELECT InvocationId, EvidenceJson FROM AssessmentInvocations ORDER BY InvocationId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var invocationId = reader.GetString(0);
            var recordId = "AssessmentInvocations/" + invocationId;
            builder.Reference(ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity,
                ArtifactReferenceTargetKind.Invocation, invocationId, recordId, "durable-invocation-provenance");
            builder.Retain(ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity, recordId,
                "durable-invocation-provenance");
            try
            {
                var json = reader.GetString(1);
                using var document = JsonDocument.Parse(json);
                if (!CaptureInvocationReferenceBindings(document.RootElement, store.StoreIdentity, recordId, builder))
                    throw new InvalidDataException("Invocation evidence has an invalid reference shape.");
                var evidence = JsonSerializer.Deserialize<BatchInvocationEvidence>(json, StrictJsonOptions)
                    ?? throw new InvalidDataException("Invocation evidence is null.");
                if (!StringComparer.Ordinal.Equals(evidence.InvocationId, invocationId))
                    throw new InvalidDataException("Invocation evidence does not match its row id.");
                if (evidence.CollectStatistics)
                    builder.Incomplete(ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity,
                        "stats-cache-path-not-recorded", recordId);
                if (evidence.StoppedWordRetry is not null)
                {
                    builder.Incomplete(ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity,
                        "retry-copy-paths-not-recorded", recordId);
                }
            }
            catch (Exception exception) when (IsStoredDataFailure(exception))
            {
                builder.Incomplete(ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity,
                    "invocation-evidence-unrecognized", recordId);
            }
            builder.AddInvocationProvenance(store.StoreIdentity, invocationId, assessmentInvocations.Contains(invocationId), recordId);
        }
    }

    private static bool CaptureInvocationReferenceBindings(JsonElement root, string storeIdentity, string recordId,
        SnapshotBuilder builder)
    {
        string[] allowed = ["InvocationId", "SourcePath", "SourceBytesSha256", "ExecutableBytesSha256", "WordsPath",
            "WordsSha256", "TsvPath", "TsvSha256", "StandardErrorPath", "StandardErrorSha256", "PerWordTimeoutMs",
            "PerWordStepLimit", "Threads", "CollectStatistics", "ExecutableVersion", "ExecutablePath", "AnalysesPath",
            "AnalysesSha256", "StoppedWordRetry", "GrammarWarnings"];
        string[] required = ["InvocationId", "SourcePath", "SourceBytesSha256", "ExecutableBytesSha256", "WordsPath",
            "WordsSha256", "TsvPath", "TsvSha256", "StandardErrorPath", "StandardErrorSha256", "PerWordTimeoutMs",
            "PerWordStepLimit", "Threads", "CollectStatistics"];
        if (root.ValueKind != JsonValueKind.Object) return false;
        var valid = IsObjectShape(root, "invocation evidence", allowed, required);
        foreach (var (property, reason) in new[]
                 {
                     ("SourcePath", "invocation-source-path"),
                     ("WordsPath", "invocation-words-path"),
                     ("TsvPath", "invocation-tsv-path"),
                     ("StandardErrorPath", "invocation-stderr-path"),
                 })
        {
            var values = Properties(root, property).ToArray();
            if (values.Length != 1 || values[0].ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(values[0].GetString()))
            {
                valid = false;
                continue;
            }
            AddPath(builder, ArtifactReferenceFamily.InvocationArtifacts, storeIdentity, recordId, reason,
                values[0].GetString()!);
        }

        var analysesPaths = Properties(root, "AnalysesPath").ToArray();
        var analysesHashes = Properties(root, "AnalysesSha256").ToArray();
        if (analysesPaths.Length > 1 || analysesHashes.Length > 1) valid = false;
        var hasAnalysesPath = analysesPaths.Length == 1 && analysesPaths[0].ValueKind != JsonValueKind.Null;
        if (hasAnalysesPath)
        {
            if (analysesPaths[0].ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(analysesPaths[0].GetString())) valid = false;
            else AddPath(builder, ArtifactReferenceFamily.InvocationArtifacts, storeIdentity, recordId,
                "invocation-analyses-path", analysesPaths[0].GetString()!);
        }
        var hasAnalysesHash = analysesHashes.Length == 1 && analysesHashes[0].ValueKind != JsonValueKind.Null;
        if (hasAnalysesHash && (analysesHashes[0].ValueKind != JsonValueKind.String ||
            !IsCanonicalSha256(analysesHashes[0].GetString()))) valid = false;
        if (hasAnalysesPath != hasAnalysesHash) valid = false;
        var retries = Properties(root, "StoppedWordRetry").ToArray();
        if (retries.Length > 1) valid = false;
        if (retries.Length == 1 && retries[0].ValueKind != JsonValueKind.Null)
        {
            var retry = retries[0];
            string[] retryAllowed = ["LimitMs", "Stopped", "Retried", "Recovered", "StillStopped", "RetryInvocationId",
                "FirstPassTsvSha256", "FirstPassAnalysesSha256", "RetryTsvSha256", "RetryAnalysesSha256", "Words",
                "NotRetried", "Failure"];
            string[] retryRequired = ["LimitMs", "Stopped", "Retried", "Recovered", "StillStopped", "RetryInvocationId",
                "FirstPassTsvSha256", "FirstPassAnalysesSha256", "RetryTsvSha256", "RetryAnalysesSha256", "Words",
                "NotRetried"];
            if (!IsObjectShape(retry, "stopped-word retry", retryAllowed, retryRequired)) valid = false;
            var retryIds = Properties(retry, "RetryInvocationId").ToArray();
            if (retryIds.Length != 1 || retryIds[0].ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(retryIds[0].GetString())) valid = false;
            foreach (var retryId in retryIds.Where(value => value.ValueKind == JsonValueKind.String))
                AddRequiredReference(builder, ArtifactReferenceFamily.InvocationArtifacts, storeIdentity,
                    ArtifactReferenceTargetKind.Invocation, retryId.GetString() ?? string.Empty, recordId,
                    "stopped-word-retry-provenance");
        }
        return valid;
    }

    private static void CaptureReports(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store,
            "SELECT ReportId, AssessmentId, ReportJson, EvidenceJson, Kind FROM Reports ORDER BY ReportId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var reportId = reader.GetString(0);
            var recordId = "Reports/" + reportId;
            builder.Retain(ArtifactReferenceFamily.Reports, store.StoreIdentity, recordId, "saved-report");
            AddOptionalReference(builder, ArtifactReferenceFamily.Reports, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, NullableString(reader, 1), recordId, "report-direct-assessment");
            var kind = NullableString(reader, 4);
            if (kind == "parsimony")
            {
                try
                {
                    if (!CaptureFrozenParsimonyReport(store.StoreIdentity, recordId, reportId,
                            NullableString(reader, 2), builder))
                        builder.Incomplete(ArtifactReferenceFamily.Reports, store.StoreIdentity,
                            "frozen-report-reference-unrecognized", recordId);
                }
                catch (Exception exception) when (IsStoredDataFailure(exception))
                {
                    builder.Incomplete(ArtifactReferenceFamily.Reports, store.StoreIdentity,
                        "frozen-report-reference-unrecognized", recordId);
                }
                try
                {
                    if (!CaptureFrozenParsimonyEvidence(store.StoreIdentity, recordId,
                            NullableString(reader, 3), builder))
                        builder.Incomplete(ArtifactReferenceFamily.Reports, store.StoreIdentity,
                            "frozen-report-reference-unrecognized", recordId);
                }
                catch (Exception exception) when (IsStoredDataFailure(exception))
                {
                    builder.Incomplete(ArtifactReferenceFamily.Reports, store.StoreIdentity,
                        "frozen-report-reference-unrecognized", recordId);
                }
            }
            else if (kind is "coverage" or "correctness" or "difference")
            {
                if (NullableString(reader, 1) is null)
                    builder.Incomplete(ArtifactReferenceFamily.Reports, store.StoreIdentity,
                        "report-direct-assessment-missing", recordId);
            }
            else builder.Incomplete(ArtifactReferenceFamily.Reports, store.StoreIdentity,
                "report-kind-unrecognized", recordId);
        }
    }

    private static bool CaptureFrozenParsimonyReport(string storeIdentity, string recordId, string rowReportId,
        string? reportJson, SnapshotBuilder builder)
    {
        if (reportJson is null) return false;
        using var document = JsonDocument.Parse(reportJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return false;
        var valid = IsObjectShape(root, "Parsimony Report",
            ["reportId", "inputs", "findings", "text", "assessmentIds", "measureRuns", "joinQuality",
                "dispositionProjection", "notes"], ["reportId", "inputs", "findings", "text", "notes"]);
        foreach (var reportId in Properties(root, "reportId"))
            if (reportId.ValueKind != JsonValueKind.String ||
                !StringComparer.Ordinal.Equals(reportId.GetString(), rowReportId)) valid = false;
        var assessmentIds = Properties(root, "assessmentIds").ToArray();
        if (assessmentIds.Length == 0) valid = valid && true;
        foreach (var ids in assessmentIds)
            valid &= CaptureStringReferences(ids, ArtifactReferenceFamily.Reports, storeIdentity, recordId,
                "frozen-parsimony-report", builder);
        foreach (var input in Properties(root, "inputs"))
            valid &= CaptureParsimonyInputReferences(input, storeIdentity, recordId,
                "frozen-parsimony-input", builder);
        var findings = Properties(root, "findings").ToArray();
        foreach (var finding in findings) valid &= CaptureFindingReferences(finding, storeIdentity, recordId,
            ArtifactReferenceFamily.Reports, "frozen-parsimony-finding-evidence", builder);

        foreach (var notes in Properties(root, "notes"))
            valid &= ValidateNonNullArray(notes, "Parsimony Report notes");
        foreach (var measureRuns in Properties(root, "measureRuns"))
            valid &= ValidateNonNullArray(measureRuns, "Parsimony Report measure runs");
        var texts = Properties(root, "text").ToArray();
        if (texts.Length != 1 || texts[0].ValueKind != JsonValueKind.String) valid = false;

        try
        {
            _ = JsonSerializer.Deserialize<ParsimonyReportResponse>(reportJson, StrictWebJsonOptions)
                ?? throw new InvalidDataException("The frozen Parsimony Report is null.");
        }
        catch (Exception exception) when (IsStoredDataFailure(exception))
        {
            valid = false;
        }
        return valid;
    }

    private static bool CaptureFrozenParsimonyEvidence(string storeIdentity, string recordId, string? evidenceJson,
        SnapshotBuilder builder)
    {
        if (evidenceJson is null) return false;
        using var document = JsonDocument.Parse(evidenceJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return false;
        var valid = IsObjectShape(root, "frozen Parsimony evidence",
            ["inputs", "grammarFactsPath", "evidencePath", "findings", "joinQuality"],
            ["inputs", "grammarFactsPath", "evidencePath", "findings", "joinQuality"]);
        var bundles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in Properties(root, "inputs"))
        {
            var beforeCount = builder.References.Count;
            valid &= CaptureParsimonyInputReferences(input, storeIdentity, recordId, "frozen-parsimony-input",
                builder);
            foreach (var bundle in builder.References.Skip(beforeCount).Where(reference =>
                         reference.Family == ArtifactReferenceFamily.Reports &&
                         reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle))
                bundles.Add(bundle.Target);
        }
        foreach (var path in Properties(root, "grammarFactsPath"))
            valid &= CaptureScopedPaths(path, bundles, storeIdentity, recordId,
                "frozen-parsimony-grammar-facts-relative", builder);
        foreach (var path in Properties(root, "evidencePath"))
            valid &= CaptureScopedPaths(path, bundles, storeIdentity, recordId,
                "frozen-parsimony-evidence-relative", builder);
        foreach (var findings in Properties(root, "findings"))
            valid &= ValidateFrozenEvidenceFindings(findings);
        foreach (var quality in Properties(root, "joinQuality"))
            if (quality.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    _ = JsonSerializer.Deserialize<ParsimonyJoinQuality>(quality.GetRawText(), StrictWebJsonOptions)
                        ?? throw new InvalidDataException("Frozen Parsimony join quality is null.");
                }
                catch (Exception exception) when (IsStoredDataFailure(exception))
                {
                    valid = false;
                }
            }
        return valid;
    }

    private static bool CaptureParsimonyInputReferences(JsonElement input, string storeIdentity, string recordId,
        string prefix, SnapshotBuilder builder, string? bundleReason = null, string? baselineTokenReason = null)
    {
        if (input.ValueKind != JsonValueKind.Object) return false;
        var valid = IsObjectShape(input, "Parsimony inputs",
            ["bundleId", "baselineToken", "inputKind", "candidate", "modelFingerprint", "grammarFacts",
                "evidence", "selectionSha256", "expectationRevisionSha256", "assessmentIds", "evidenceScope",
                "retirementExpectationTranslation"],
            ["bundleId", "baselineToken", "inputKind", "modelFingerprint", "grammarFacts", "evidence",
                "assessmentIds", "evidenceScope"]);
        foreach (var bundle in Properties(input, "bundleId"))
        {
            if (bundle.ValueKind != JsonValueKind.String) valid = false;
            AddRequiredReference(builder, ArtifactReferenceFamily.Reports, storeIdentity,
                ArtifactReferenceTargetKind.ParsimonyBundle,
                bundle.ValueKind == JsonValueKind.String ? bundle.GetString() ?? string.Empty : string.Empty,
                recordId, bundleReason ?? prefix + "-bundle");
        }
        foreach (var token in Properties(input, "baselineToken"))
        {
            try
            {
                AddBaselineToken(builder, storeIdentity, token.GetRawText(), recordId,
                    baselineTokenReason ?? prefix + "-baseline-token");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
        }
        foreach (var assessmentIds in Properties(input, "assessmentIds"))
            valid &= CaptureStringReferences(assessmentIds, ArtifactReferenceFamily.Reports, storeIdentity, recordId,
                prefix, builder);
        try
        {
            _ = JsonSerializer.Deserialize<ParsimonyReportInputs>(input.GetRawText(), StrictWebJsonOptions)
                ?? throw new InvalidDataException("Parsimony inputs are null.");
        }
        catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
        return valid;
    }

    private static bool CaptureStringReferences(JsonElement values, ArtifactReferenceFamily family,
        string storeIdentity, string recordId, string reason, SnapshotBuilder builder)
    {
        if (values.ValueKind != JsonValueKind.Array) return false;
        var valid = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String) { valid = false; continue; }
            var target = value.GetString() ?? string.Empty;
            if (!seen.Add(target)) valid = false;
            AddRequiredReference(builder, family, storeIdentity,
                ArtifactReferenceTargetKind.Assessment, target, recordId, reason);
        }
        return valid;
    }

    private static bool CaptureScopedPaths(JsonElement value, IEnumerable<string> bundles, string storeIdentity,
        string recordId, string reason, SnapshotBuilder builder)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return false;
        var bundleList = bundles.Distinct(StringComparer.Ordinal).ToArray();
        if (bundleList.Length == 0) return false;
        foreach (var bundle in bundleList)
            AddScopedPath(builder, ArtifactReferenceFamily.Reports, storeIdentity, recordId, reason,
                value.GetString()!, bundle);
        return true;
    }

    private static bool ValidateFrozenEvidenceFindings(JsonElement findings)
    {
        if (findings.ValueKind != JsonValueKind.Array) return false;
        var valid = true;
        foreach (var finding in findings.EnumerateArray())
        {
            if (finding.ValueKind != JsonValueKind.Object) { valid = false; continue; }
            valid &= IsObjectShape(finding, "frozen Parsimony finding evidence",
                ["findingId", "evidenceDigest"], ["findingId", "evidenceDigest"]);
            foreach (var field in new[] { "findingId", "evidenceDigest" })
            {
                var values = Properties(finding, field).ToArray();
                if (values.Length != 1 || values[0].ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(values[0].GetString())) valid = false;
            }
        }
        return valid;
    }

    private static bool ValidateNonNullArray(JsonElement values, string description)
    {
        if (values.ValueKind != JsonValueKind.Array) return false;
        return values.EnumerateArray().All(value => value.ValueKind != JsonValueKind.Null);
    }

    private static IEnumerable<JsonElement> Properties(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Where(property => property.NameEquals(name)).Select(property => property.Value)
            : [];

    private static bool IsObjectShape(JsonElement root, string description, string[] allowed, string[] required)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        var allowedNames = allowed.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!allowedNames.Contains(property.Name) || !seen.Add(property.Name)) return false;
        return required.All(seen.Contains);
    }

    private static bool CaptureFindingReferences(JsonElement finding, string storeIdentity, string recordId,
        ArtifactReferenceFamily family, string reason, SnapshotBuilder builder)
    {
        if (finding.ValueKind != JsonValueKind.Array) return false;
        var valid = true;
        foreach (var item in finding.EnumerateArray())
            valid &= CaptureSingleFindingReference(item, storeIdentity, recordId, family, reason, builder);
        return valid;
    }

    private static bool CaptureSingleFindingReference(JsonElement finding, string storeIdentity, string recordId,
        ArtifactReferenceFamily family, string reason, SnapshotBuilder builder)
    {
        if (finding.ValueKind != JsonValueKind.Object) return false;
        var valid = IsObjectShape(finding, "Parsimony finding",
            ["findingId", "measureId", "axis", "tier", "attachesTo", "groupKey", "number", "threshold",
                "evidenceDigest", "evidenceRefs", "recipeLink", "limitations", "verification", "itemNames"],
            ["findingId", "measureId", "axis", "tier", "attachesTo", "groupKey", "number", "threshold",
                "evidenceDigest", "evidenceRefs", "recipeLink", "limitations", "verification"]);
        valid &= HasDefinedEnumDiscriminant(finding, "axis", typeof(ParsimonyAxis));
        valid &= HasDefinedEnumDiscriminant(finding, "tier", typeof(ParsimonyTier));
        valid &= HasDefinedEnumDiscriminant(finding, "verification", typeof(ParsimonyVerification));
        var attachments = Properties(finding, "attachesTo").ToArray();
        if (attachments.Length != 1 || attachments[0].ValueKind != JsonValueKind.Object) valid = false;
        foreach (var attachment in attachments.Where(value => value.ValueKind == JsonValueKind.Object))
        {
            valid &= IsObjectShape(attachment, "Parsimony finding attachment",
                ["kind", "identity", "authoredObjectKind", "groupKind"], ["kind", "identity"]);
            valid &= HasDefinedEnumDiscriminant(attachment, "kind", typeof(ParsimonyAttachmentKind));
            valid &= HasDefinedEnumDiscriminant(attachment, "authoredObjectKind", typeof(ParsimonyAuthoredObjectKind),
                optional: true);
            valid &= HasDefinedEnumDiscriminant(attachment, "groupKind", typeof(ParsimonyGroupKind), optional: true);
        }
        var refs = Properties(finding, "evidenceRefs").ToArray();
        if (refs.Length != 1 || refs[0].ValueKind != JsonValueKind.Array) valid = false;
        foreach (var list in refs.Where(item => item.ValueKind == JsonValueKind.Array))
        foreach (var evidenceRef in list.EnumerateArray())
        {
            if (evidenceRef.ValueKind != JsonValueKind.Object) { valid = false; continue; }
            valid &= IsObjectShape(evidenceRef, "Parsimony finding evidence reference",
                ["bundleId", "view", "arguments"], ["bundleId", "view", "arguments"]);
            var bundles = Properties(evidenceRef, "bundleId").ToArray();
            foreach (var bundle in bundles)
            {
                if (bundle.ValueKind != JsonValueKind.String) valid = false;
                AddRequiredReference(builder, family, storeIdentity, ArtifactReferenceTargetKind.ParsimonyBundle,
                    bundle.ValueKind == JsonValueKind.String ? bundle.GetString() ?? string.Empty : string.Empty,
                    recordId, reason);
            }
            var views = Properties(evidenceRef, "view").ToArray();
            if (views.Length != 1 || views[0].ValueKind != JsonValueKind.String ||
                !ParsimonyViews.All.Contains(views[0].GetString() ?? string.Empty)) valid = false;
            var arguments = Properties(evidenceRef, "arguments").ToArray();
            if (arguments.Length != 1 || arguments[0].ValueKind != JsonValueKind.Object) valid = false;
        }
        var findingIds = Properties(finding, "findingId").ToArray();
        if (findingIds.Length != 1 || findingIds[0].ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(findingIds[0].GetString())) valid = false;
        var evidenceDigests = Properties(finding, "evidenceDigest").ToArray();
        if (evidenceDigests.Length != 1 || evidenceDigests[0].ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(evidenceDigests[0].GetString())) valid = false;
        try
        {
            var parsed = JsonSerializer.Deserialize<ParsimonyFinding>(finding.GetRawText(), StrictWebJsonOptions)
                ?? throw new InvalidDataException("Parsimony finding is null.");
            if (parsed.EvidenceRefs is null || parsed.EvidenceRefs.Any(reference => reference is null)) valid = false;
            if (parsed.Limitations is null || parsed.Limitations.Any(value => value is null)) valid = false;
            if (parsed.ItemNames is null || parsed.ItemNames.Any(value => value is null)) valid = false;
        }
        catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
        return valid;
    }

    private static bool HasDefinedEnumDiscriminant(JsonElement root, string propertyName, Type enumType,
        bool optional = false)
    {
        var values = Properties(root, propertyName).ToArray();
        if (values.Length == 0) return optional;
        if (values.Length != 1 || values[0].ValueKind == JsonValueKind.Null) return optional;
        if (values[0].ValueKind != JsonValueKind.Number) return true;
        return values[0].TryGetInt32(out var value) && Enum.IsDefined(enumType, value);
    }

    private static void CaptureDifferences(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT DifferenceId, BeforeAssessmentId, AfterAssessmentId " +
            "FROM ProposalDifferences ORDER BY DifferenceId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordId = "ProposalDifferences/" + reader.GetString(0);
            builder.Reference(ArtifactReferenceFamily.ProposalDifferences, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, reader.GetString(1), recordId, "proposal-difference-before");
            builder.Reference(ArtifactReferenceFamily.ProposalDifferences, store.StoreIdentity,
                ArtifactReferenceTargetKind.Assessment, reader.GetString(2), recordId, "proposal-difference-after");
            builder.Retain(ArtifactReferenceFamily.ProposalDifferences, store.StoreIdentity, recordId,
                "proposal-difference-citation");
        }
    }

    private static void CaptureBaselines(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT ProjectKey, ProjectIdentity, SemanticSnapshotDigest, " +
            "ProjectionVersion, CapturedUtc, BundleDigest, CapturedHostSessionId, CapturedEditGeneration, " +
            "RootDirectory, FwDataPath FROM Baselines ORDER BY ProjectKey;");
        using var reader = command.ExecuteReader();
        var rowNumber = 0;
        while (reader.Read())
        {
            rowNumber++;
            var recordId = "Baselines/row-" + rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                var projectKey = NullableString(reader, 0);
                if (!string.IsNullOrWhiteSpace(projectKey)) recordId = "Baselines/" + projectKey;
                builder.Retain(ArtifactReferenceFamily.Baselines, store.StoreIdentity, recordId, "raw-baseline-row");
                var projectIdentity = NullableString(reader, 1);
                var semanticDigest = NullableString(reader, 2);
                var projectionVersion = NullableString(reader, 3);
                var capturedUtc = NullableString(reader, 4);
                var bundleDigest = NullableString(reader, 5);
                var rootDirectory = NullableString(reader, 8);
                var fwDataPath = NullableString(reader, 9);
                AddRequiredReference(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                    ArtifactReferenceTargetKind.Path, rootDirectory ?? string.Empty, recordId, "raw-baseline-root");
                AddRequiredReference(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                    ArtifactReferenceTargetKind.Path, fwDataPath ?? string.Empty, recordId, "raw-baseline-fwdata");
                if (!string.IsNullOrWhiteSpace(projectIdentity) && !string.IsNullOrWhiteSpace(bundleDigest))
                    builder.Reference(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                        ArtifactReferenceTargetKind.BaselineBundleDigest, bundleDigest, recordId, "raw-baseline-row",
                        projectIdentity);
                else
                    builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                        "baseline-row-token-unrecognized", recordId);

                var validPaths = !string.IsNullOrWhiteSpace(rootDirectory) && !string.IsNullOrWhiteSpace(fwDataPath);
                if (validPaths && !string.IsNullOrWhiteSpace(projectIdentity) && !string.IsNullOrWhiteSpace(bundleDigest))
                    builder.AddBaselinePublication(new ArtifactBaselinePublication(store.StoreIdentity, projectIdentity,
                        bundleDigest, rootDirectory!, fwDataPath!, recordId));

                _ = new BaselineToken(projectIdentity ?? string.Empty, semanticDigest ?? string.Empty,
                    projectionVersion ?? string.Empty, capturedUtc ?? string.Empty, bundleDigest ?? string.Empty,
                    NullableString(reader, 6), NullableLong(reader, 7));
                if (!validPaths)
                    builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                        "baseline-row-token-unrecognized", recordId);
            }
            catch (Exception exception) when (IsStoredDataFailure(exception) || exception is InvalidCastException)
            {
                builder.Retain(ArtifactReferenceFamily.Baselines, store.StoreIdentity, recordId, "raw-baseline-row");
                builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                    "baseline-row-token-unrecognized", recordId);
            }
        }
    }

    private static void CaptureRetainedInvocations(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT InvocationId, BaselineToken, BaselineRootDirectory, " +
            "BaselineFwDataPath, ArtifactInvocationId FROM RetainedInvocations ORDER BY InvocationId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var invocationId = reader.GetString(0);
            var recordId = "RetainedInvocations/" + invocationId;
            builder.Reference(ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity,
                ArtifactReferenceTargetKind.Invocation, reader.GetString(4), recordId, "retained-invocation-artifact");
            AddPath(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity, recordId,
                "retained-invocation-baseline-root", reader.GetString(2));
            AddPath(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity, recordId,
                "retained-invocation-baseline-fwdata", reader.GetString(3));
            builder.Retain(ArtifactReferenceFamily.InvocationArtifacts, store.StoreIdentity, recordId,
                "retained-invocation");
            try
            {
                AddBaselineToken(builder, store.StoreIdentity, reader.GetString(1), recordId,
                    "retained-invocation-baseline-token");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception))
            {
                builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                    "retained-invocation-baseline-token-unrecognized", recordId);
            }
        }
    }

    private static void CaptureGrammarChecks(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT rowid, BaselineToken, ResponseGzip FROM GrammarChecks ORDER BY rowid;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordId = "GrammarChecks/" + reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            builder.Retain(ArtifactReferenceFamily.Baselines, store.StoreIdentity, recordId, "grammar-check");
            var rowToken = NullableString(reader, 1);
            BaselineToken? parsedRowToken = null;
            try
            {
                if (rowToken is null) throw new InvalidDataException("Grammar Check Baseline token is absent.");
                RequireBaselineTokenShape(rowToken);
                parsedRowToken = JsonSerializer.Deserialize<BaselineToken>(rowToken, BaselineJsonOptions)
                    ?? throw new InvalidDataException("Grammar Check Baseline token is null.");
                AddBaselineToken(builder, store.StoreIdentity, parsedRowToken, recordId,
                    "grammar-check-baseline-token");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception) || exception is InvalidCastException)
            {
                builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                    "grammar-check-reference-unrecognized", recordId);
            }
            try
            {
                var json = GrammarCheckStorage.Decompress(reader.GetFieldValue<byte[]>(2));
                RequireObjectShape(json, "Grammar Check response", "Findings", "HasBaseline", "Locale",
                    "CheckedLexemeCount", "CheckedRuleCount", "Advisories", "Summary", "Provenance", "ObjectTimingDetail");
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                RequireObjectShape(root, "Grammar Check response",
                    ["Findings", "HasBaseline", "Locale", "CheckedLexemeCount", "CheckedRuleCount", "Advisories",
                        "Summary", "Provenance", "ObjectTimingDetail"], ["Findings", "HasBaseline"]);
                var valid = ValidateNullableObjectArray(root, "Findings") &&
                    ValidateOptionalObjectArray(root, "Advisories") && ValidateOptionalObjectArray(root, "Summary");
                foreach (var provenance in Properties(root, "Provenance"))
                {
                    if (provenance.ValueKind == JsonValueKind.Null) continue;
                    try
                    {
                        RequireObjectShape(provenance, "Grammar Check provenance",
                            ["BaselineToken", "SelectionSha256", "ParserStamp", "CheckedUtc", "SourceLastWriteUtc", "Trial"],
                            ["BaselineToken", "SelectionSha256", "CheckedUtc"]);
                        foreach (var token in Properties(provenance, "BaselineToken"))
                        {
                            RequireBaselineTokenShape(token.GetRawText());
                            var capturedToken = JsonSerializer.Deserialize<BaselineToken>(token.GetRawText(),
                                BaselineJsonOptions) ?? throw new InvalidDataException("Grammar Check provenance token is null.");
                            AddBaselineToken(builder, store.StoreIdentity, capturedToken, recordId,
                                "grammar-check-provenance-baseline-token");
                            if (parsedRowToken is null || parsedRowToken != capturedToken) valid = false;
                        }
                        if (!Properties(provenance, "BaselineToken").Any()) valid = false;
                    }
                    catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
                }
                var response = JsonSerializer.Deserialize<GrammarCheckResponse>(json, StrictJsonOptions)
                    ?? throw new InvalidDataException("The Grammar Check response is null.");
                if (response.Findings is null || response.Findings.Any(finding => finding is null) ||
                    response.Advisories is null || response.Advisories.Any(advisory => advisory is null) ||
                    response.Summary is null || response.Summary.Any(summary => summary is null)) valid = false;
                if (!valid)
                    builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                        "grammar-check-reference-unrecognized", recordId);
            }
            catch (Exception exception) when (IsStoredDataFailure(exception) || exception is InvalidCastException)
            {
                builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                    "grammar-check-reference-unrecognized", recordId);
            }
        }
    }

    private static bool ValidateNullableObjectArray(JsonElement root, string propertyName)
    {
        var values = Properties(root, propertyName).ToArray();
        return values.Length == 1 && values[0].ValueKind == JsonValueKind.Array &&
            values[0].EnumerateArray().All(value => value.ValueKind == JsonValueKind.Object);
    }

    private static bool ValidateOptionalObjectArray(JsonElement root, string propertyName)
    {
        var values = Properties(root, propertyName).ToArray();
        return values.Length == 0 || values.Length == 1 && values[0].ValueKind == JsonValueKind.Array &&
            values[0].EnumerateArray().All(value => value.ValueKind == JsonValueKind.Object);
    }

    private static void CaptureJobs(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT JobId, Kind, Status, InputJson, ResultJson, DryRunJson, " +
            "DryRunPublished " +
            "FROM Jobs ORDER BY JobId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var jobId = reader.GetString(0);
            var kind = reader.GetString(1);
            var status = reader.GetString(2);
            var inputJson = reader.GetString(3);
            var resultJson = NullableString(reader, 4);
            var dryRunJson = NullableString(reader, 5);
            var dryRunPublished = ReadStoredBoolean(reader, 6, "DryRunPublished");
            var rootId = "Jobs/" + jobId;
            JobStatus? parsedStatus = null;
            try
            {
                parsedStatus = JobStatusJson.Parse(status);
            }
            catch (JsonException)
            {
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-status-unrecognized", rootId);
            }
            builder.Retain(ArtifactReferenceFamily.Jobs, store.StoreIdentity, rootId,
                parsedStatus is { } knownStatus && IsActiveJob(knownStatus) ? "active-job" : "retained-job-record");
            var inputHasBaselineBinding = CaptureJobInput(store.StoreIdentity, rootId + "/InputJson", kind,
                inputJson, builder);
            var resultHasBaselineBinding = false;
            if (resultJson is not null)
                resultHasBaselineBinding = CaptureJobResult(store.StoreIdentity, rootId + "/ResultJson", kind,
                    parsedStatus, dryRunPublished, resultJson, builder);

            if (dryRunPublished != (dryRunJson is not null))
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-dry-run-publication-mismatch", rootId);
            if (parsedStatus is (JobStatus.CompletedDryRunOnly or JobStatus.CompletedWithAssessmentFailure) &&
                !dryRunPublished)
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-success-dry-run-unpublished", rootId);

            if (parsedStatus is { } known && IsSuccessfulJobStatus(known) &&
                !IsExpectedSuccessfulJobStatus(kind, known))
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-success-status-kind-unrecognized", rootId);
            if (parsedStatus is { } successfulStatus && IsSuccessfulJobStatus(successfulStatus) &&
                RequiresReferenceBearingResult(kind) && resultJson is null)
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-success-result-missing", rootId + "/ResultJson");
            if (parsedStatus == JobStatus.Completed && kind == ProjectJobHandlers.BaselineRefreshKind &&
                resultJson is not null)
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-success-result-unexpected", rootId + "/ResultJson");
            if (parsedStatus is { } activeStatus && IsActiveJob(activeStatus) && resultJson is not null)
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-nonterminal-result-present", rootId + "/ResultJson");
            if (dryRunPublished && kind is not ("dry-run" or TrialJobHandler.TrialKind))
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-kind-cannot-publish-dry-run", rootId);
            if (parsedStatus is { } executionStatus && MayHaveExecuted(executionStatus) &&
                (executionStatus != JobStatus.StoppedByBudget || dryRunPublished) &&
                kind is "dry-run" or TrialJobHandler.TrialKind && !inputHasBaselineBinding &&
                !resultHasBaselineBinding)
                builder.Incomplete(ArtifactReferenceFamily.Jobs, store.StoreIdentity,
                    "job-baseline-binding-missing", rootId);
            if (dryRunJson is not null)
                CapturePublishedDryRun(store.StoreIdentity, rootId + "/DryRunJson", dryRunJson, builder);
        }
    }

    private static bool MayHaveExecuted(JobStatus status) => status is JobStatus.Running or JobStatus.Completed or
        JobStatus.CompletedDryRunOnly or JobStatus.CompletedWithAssessmentFailure or JobStatus.Failed or
        JobStatus.Cancelled or JobStatus.Interrupted or JobStatus.StoppedByBudget;

    private static bool IsExpectedSuccessfulJobStatus(string kind, JobStatus status) => kind switch
    {
        ProjectJobHandlers.BaselineRefreshKind => status == JobStatus.Completed,
        "dry-run" => status == JobStatus.CompletedDryRunOnly,
        TrialJobHandler.TrialKind => status is JobStatus.Completed or JobStatus.CompletedWithAssessmentFailure,
        ParsimonyJobHandler.JobKind or ParsimonyCandidateEvidenceBuilder.JobKind or
            PhonologyCheckJobHandler.JobKind => status == JobStatus.Completed,
        _ => false,
    };

    private static bool RequiresReferenceBearingResult(string kind) => kind is
        "dry-run" or TrialJobHandler.TrialKind or ParsimonyJobHandler.JobKind or
        ParsimonyCandidateEvidenceBuilder.JobKind or PhonologyCheckJobHandler.JobKind;

    private static bool CaptureJobInput(string storeIdentity, string recordId, string kind, string json,
        SnapshotBuilder builder)
    {
        var hasBaselineBinding = false;
        try
        {
            switch (kind)
            {
                case "assess":
                    RequireObjectShape(json, "Assess job input");
                    break;
                case ProjectJobHandlers.BaselineRefreshKind:
                    RequireObjectShape(json, "Baseline refresh job input");
                    break;
                case TrialJobHandler.TrialKind:
                {
                    RequireObjectShape(json, "Trial job input",
                        ["proposalJson", "scope", "words", "allWords", "prerequisites", "limits",
                            "baselineAssessmentIds", "negativeWords", "wordList"], ["proposalJson", "scope"]);
                    var input = JsonSerializer.Deserialize<TrialJobInput>(json, StrictJsonOptions)
                        ?? throw new InvalidDataException("The Trial input is null.");
                    if (string.IsNullOrWhiteSpace(input.ProposalJson) ||
                        input.Words?.Any(string.IsNullOrWhiteSpace) == true ||
                        input.NegativeWords?.Any(string.IsNullOrWhiteSpace) == true ||
                        input.Prerequisites?.Any(prerequisite => prerequisite is null) == true)
                        throw new InvalidDataException("The Trial input contains an invalid typed member.");
                    foreach (var assessmentId in input.BaselineAssessmentIds ?? [])
                        AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                            ArtifactReferenceTargetKind.Assessment, assessmentId, recordId,
                            "trial-input-baseline-assessment");
                    break;
                }
                case "dry-run":
                {
                    RequireObjectShape(json, "Dry Run job input",
                        ["schemaVersion", "proposal", "prerequisites", "sourceBaseline"],
                        ["schemaVersion", "proposal", "prerequisites", "sourceBaseline"]);
                    var input = JsonSerializer.Deserialize<DryRunJobInput>(json, StrictJsonOptions)
                        ?? throw new InvalidDataException("The Dry Run input is null.");
                    if (input.SchemaVersion != DryRunJobInput.CurrentSchemaVersion || input.Proposal is null ||
                        input.Prerequisites is null)
                        throw new InvalidDataException("The Dry Run input has an unsupported typed shape.");
                    _ = input.Proposal.Validate();
                    foreach (var prerequisite in input.Prerequisites)
                    {
                        if (prerequisite is null) throw new InvalidDataException("A Dry Run prerequisite is null.");
                        _ = prerequisite.Validate();
                    }
                    if (input.SourceBaseline is not null)
                    {
                        using var document = JsonDocument.Parse(json);
                        RequireDryRunSourceBindingShape(Properties(document.RootElement, "sourceBaseline").SingleOrDefault());
                    }
                    if (input.SourceBaseline is not null)
                    {
                        if (input.SourceBaseline.Token is null ||
                            string.IsNullOrWhiteSpace(input.SourceBaseline.RootDirectory) ||
                            string.IsNullOrWhiteSpace(input.SourceBaseline.FwDataPath) ||
                            !IsCanonicalSha256(input.SourceBaseline.SourceSha256))
                            throw new InvalidDataException("The Dry Run input has no Baseline token.");
                        AddBaselineToken(builder, storeIdentity, input.SourceBaseline.Token, recordId,
                            "dry-run-input-baseline-token");
                        AddPath(builder, ArtifactReferenceFamily.Baselines, storeIdentity, recordId,
                            "dry-run-input-baseline-root",
                            input.SourceBaseline.RootDirectory);
                        AddPath(builder, ArtifactReferenceFamily.Baselines, storeIdentity, recordId,
                            "dry-run-input-baseline-fwdata",
                            input.SourceBaseline.FwDataPath);
                        hasBaselineBinding = true;
                    }
                    break;
                }
                case PhonologyCheckJobHandler.JobKind:
                {
                    RequireObjectShape(json, "phonology-check job input",
                        ["revisionId", "baselineToken", "limits"], ["revisionId", "baselineToken", "limits"]);
                    var input = JsonSerializer.Deserialize<PhonologyCheckJobInput>(json, StrictJsonOptions)
                        ?? throw new InvalidDataException("The phonology-check input is null.");
                    if (string.IsNullOrWhiteSpace(input.RevisionId) || input.Limits is null)
                        throw new InvalidDataException("The phonology-check input is incomplete.");
                    if (input.BaselineToken is not null)
                        AddBaselineToken(builder, storeIdentity, input.BaselineToken, recordId,
                            "phonology-check-input-baseline-token");
                    break;
                }
                case ParsimonyJobHandler.JobKind:
                {
                    RequireObjectShape(json, "Parsimony job input",
                        ["measureId", "scopeBinding", "assessmentIds"], ["measureId", "scopeBinding"]);
                    var input = JsonSerializer.Deserialize<ParsimonyJobInput>(json, StrictJsonOptions)
                        ?? throw new InvalidDataException("The Parsimony job input is null.");
                    if (string.IsNullOrWhiteSpace(input.MeasureId) ||
                        input.ScopeBinding is not null && !Enum.IsDefined(input.ScopeBinding.Kind))
                        throw new InvalidDataException("The Parsimony job input is incomplete.");
                    if (input.AssessmentIds?.Any(string.IsNullOrWhiteSpace) == true)
                        throw new InvalidDataException("The Parsimony Assessment list has a blank entry.");
                    foreach (var assessmentId in input.AssessmentIds ?? [])
                        AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                            ArtifactReferenceTargetKind.Assessment, assessmentId, recordId,
                            "parsimony-report-job-input-assessment");
                    break;
                }
                case ParsimonyCandidateEvidenceBuilder.JobKind:
                {
                    RequireObjectShape(json, "candidate Parsimony job input", "schemaVersion", "dryRunJobId",
                        "measureId", "scopeBinding", "assessmentIds", "retirementExpectationTranslation");
                    var strictInput = JsonSerializer.Deserialize<ParsimonyCandidateJobInput>(json, StrictJsonOptions)
                        ?? throw new InvalidDataException("The candidate Parsimony job input is null.");
                    if (strictInput.RetirementExpectationTranslation is { } translation &&
                        (translation.Original?.Baseline is null || translation.Translated?.Baseline is null))
                        throw new InvalidDataException("The candidate Parsimony retirement translation is incomplete.");
                    var input = ParsimonyCandidateJobInput.Parse(json);
                    AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                        ArtifactReferenceTargetKind.Job, input.DryRunJobId, recordId,
                        "parsimony-candidate-job-input-dry-run-job");
                    foreach (var assessmentId in input.AssessmentIds ?? [])
                        AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                            ArtifactReferenceTargetKind.Assessment, assessmentId, recordId,
                            "parsimony-candidate-job-input-assessment");
                    if (input.RetirementExpectationTranslation is { } retirement)
                        AddBaselineToken(builder, storeIdentity, retirement.Original.Baseline, recordId,
                            "parsimony-candidate-job-input-retirement-baseline-token");
                    break;
                }
                default:
                    builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity, "job-input-kind-unrecognized", recordId);
                    break;
            }
        }
        catch (Exception exception) when (IsStoredDataFailure(exception))
        {
            builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity, "job-input-shape-unrecognized", recordId);
        }
        return hasBaselineBinding;
    }

    private static bool CaptureJobResult(string storeIdentity, string recordId, string kind, JobStatus? status,
        bool dryRunPublished, string json, SnapshotBuilder builder)
    {
        var hasBaselineBinding = false;
        try
        {
            if (status == JobStatus.StoppedByBudget && kind is "assess" or TrialJobHandler.TrialKind)
            {
                RequireObjectShape(json, "Budget stop result",
                    ["code", "message", "wordsDone", "wordsNotDone", "baselineToken"],
                    ["code", "message", "wordsDone", "wordsNotDone"]);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("baselineToken", out var token))
                {
                    var baseline = JsonSerializer.Deserialize<BaselineToken>(token.GetRawText(), StrictJsonOptions)
                        ?? throw new InvalidDataException("The budget stop has no Baseline token.");
                    AddBaselineToken(builder, storeIdentity, baseline, recordId, "budget-stop-baseline-token");
                    hasBaselineBinding = true;
                }
                return hasBaselineBinding;
            }
            if (kind == ProjectJobHandlers.BaselineRefreshKind)
            {
                if (status == JobStatus.Completed)
                {
                    builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity,
                        "job-success-result-unexpected", recordId);
                }
                else if (status == JobStatus.Failed)
                {
                    CaptureBaselineRefreshFailureResult(json);
                }
                else CaptureNonSuccessJobResult(kind, status, dryRunPublished, json);
                return false;
            }

            if (!IsArtifactReferenceJobKind(kind))
            {
                builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity,
                    "job-result-kind-unrecognized", recordId);
                return false;
            }

            if (status is JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted)
            {
                CaptureNonSuccessJobResult(kind, status, dryRunPublished, json);
                return false;
            }
            if (kind == TrialJobHandler.TrialKind && status == JobStatus.CompletedWithAssessmentFailure)
            {
                CaptureKnownJobFailureResult(json);
                return false;
            }
            switch (kind)
            {
                case "dry-run":
                {
                    RequireObjectShape(json, "Dry Run result",
                        ["baselineToken", "capturedUtc", "freshness", "sourceBaseline", "contentDigest"],
                        ["baselineToken", "capturedUtc", "freshness", "sourceBaseline", "contentDigest"]);
                    var completion = JsonSerializer.Deserialize<DryRunJobCompletion>(json, StrictJsonOptions)
                        ?? throw new InvalidDataException("The Dry Run result is null.");
                    if (completion.BaselineToken is not null)
                        AddBaselineToken(builder, storeIdentity, completion.BaselineToken, recordId,
                            "dry-run-result-baseline-token");
                    if (completion.SourceBaseline?.Token is { } sourceToken)
                        AddBaselineToken(builder, storeIdentity, sourceToken, recordId,
                            "dry-run-result-source-baseline-token");
                    if (completion.SourceBaseline?.RootDirectory is { } rootDirectory)
                        AddPath(builder, ArtifactReferenceFamily.Baselines, storeIdentity, recordId,
                            "dry-run-result-baseline-root", rootDirectory);
                    if (completion.SourceBaseline?.FwDataPath is { } fwDataPath)
                        AddPath(builder, ArtifactReferenceFamily.Baselines, storeIdentity, recordId,
                            "dry-run-result-baseline-fwdata", fwDataPath);
                    if (completion.SourceBaseline is not null)
                    {
                        using var document = JsonDocument.Parse(json);
                        var source = Properties(document.RootElement, "sourceBaseline").SingleOrDefault();
                        RequireDryRunSourceBindingShape(source);
                    }
                    var valid = completion.BaselineToken is not null && completion.SourceBaseline is not null &&
                        completion.SourceBaseline.Token is not null &&
                        completion.SourceBaseline.Token == completion.BaselineToken &&
                        !string.IsNullOrWhiteSpace(completion.SourceBaseline.RootDirectory) &&
                        !string.IsNullOrWhiteSpace(completion.SourceBaseline.FwDataPath) &&
                        IsCanonicalSha256(completion.SourceBaseline.SourceSha256) &&
                        !string.IsNullOrWhiteSpace(completion.CapturedUtc) &&
                        completion.Freshness is "current" or "known-old" or "currentness-not-checked" &&
                        IsCanonicalSha256(completion.ContentDigest);
                    if (!valid) throw new InvalidDataException("The Dry Run result has no exact Baseline source binding.");
                    hasBaselineBinding = true;
                    break;
                }
                case TrialJobHandler.TrialKind:
                {
                    RequireObjectShape(json, "Trial result",
                        ["baselineToken", "assessmentIds", "allWords", "grammarCheckWarning"],
                        ["baselineToken", "assessmentIds", "allWords", "grammarCheckWarning"]);
                    var completion = JsonSerializer.Deserialize<TrialJobReferenceCompletion>(json, StrictJsonOptions)
                        ?? throw new InvalidDataException("The Trial result is null.");
                    if (completion.BaselineToken is not null)
                        AddBaselineToken(builder, storeIdentity, completion.BaselineToken, recordId,
                            "trial-result-baseline-token");
                    foreach (var assessmentId in completion.AssessmentIds ?? [])
                        AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                            ArtifactReferenceTargetKind.Assessment, assessmentId, recordId, "trial-result-assessment");
                    if (completion.BaselineToken is null || completion.AssessmentIds is null ||
                        completion.AssessmentIds.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidDataException("The Trial result Assessment list is null.");
                    hasBaselineBinding = true;
                    break;
                }
                case ParsimonyJobHandler.JobKind:
                    CaptureParsimonyJobResult(storeIdentity, recordId, json, builder);
                    break;
                case ParsimonyCandidateEvidenceBuilder.JobKind:
                    CaptureParsimonyCandidateJobResult(storeIdentity, recordId, json, builder);
                    break;
                case PhonologyCheckJobHandler.JobKind:
                    CapturePhonologyCheckJobResult(storeIdentity, recordId, json, builder);
                    break;
                default:
                    builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity, "job-result-kind-unrecognized", recordId);
                    break;
            }

            if (status is { } parsed && !IsExpectedSuccessfulJobStatus(kind, parsed))
                builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity,
                    "job-result-status-kind-unrecognized", recordId);
        }
        catch (Exception exception) when (IsStoredDataFailure(exception))
        {
            builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity, "job-result-shape-unrecognized", recordId);
        }
        return hasBaselineBinding;
    }

    private static void CaptureNonSuccessJobResult(string kind, JobStatus? status, bool dryRunPublished, string json)
    {
        if (status == JobStatus.Cancelled && dryRunPublished)
        {
            RequireObjectShape(json, "published cancellation result", "assessmentDisposition");
            using var document = JsonDocument.Parse(json);
            var disposition = document.RootElement.GetProperty("assessmentDisposition");
            if (disposition.ValueKind != JsonValueKind.String || disposition.GetString() != "cancelled")
                throw new InvalidDataException("A published cancellation must record its disposition.");
            return;
        }

        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A non-success job result must be an object.");
        var properties = parsed.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Length == 1 && properties[0] == "detail")
        {
            RequireObjectShape(json, "job failure result", "detail");
            var detail = parsed.RootElement.GetProperty("detail");
            if (detail.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(detail.GetString()))
                throw new InvalidDataException("The job failure result has no detail.");
            return;
        }
        if (status == JobStatus.Failed && properties.Length == 1 && properties[0] == "failure")
        {
            RequireObjectShape(json, "job recovery result", "failure");
            var failure = parsed.RootElement.GetProperty("failure");
            if (failure.ValueKind != JsonValueKind.String ||
                failure.GetString() != "infrastructure-retry-exhausted")
                throw new InvalidDataException("The job recovery result is not recognized.");
            return;
        }
        if ((status is null or JobStatus.Interrupted) && properties.Length == 0) return;
        throw new InvalidDataException("The non-success job result is not a supported shape.");
    }

    private static void CaptureBaselineRefreshFailureResult(string json)
    {
        using (var candidate = JsonDocument.Parse(json))
        {
            var names = candidate.RootElement.ValueKind == JsonValueKind.Object
                ? candidate.RootElement.EnumerateObject().Select(property => property.Name).ToArray()
                : [];
            if (names.Length == 1 && names[0] is "detail" or "failure")
            {
                CaptureNonSuccessJobResult(ProjectJobHandlers.BaselineRefreshKind, JobStatus.Failed, false, json);
                return;
            }
        }
        RequireObjectShape(json, "Baseline refresh failure result", "detail", "outcome");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("detail", out var detail) || detail.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(detail.GetString()) ||
            !root.TryGetProperty("outcome", out var outcome) || outcome.ValueKind != JsonValueKind.String ||
            outcome.GetString() is not ("project-in-use" or "capture-failed"))
            throw new InvalidDataException("The Baseline refresh failure result is not recognized.");
    }

    private static void CaptureParsimonyJobResult(string storeIdentity, string recordId, string json,
        SnapshotBuilder builder)
    {
        RequireObjectShape(json, "Parsimony job result", "reportId", "reportIds", "bundleId", "assessmentIds");
        var completion = JsonSerializer.Deserialize<ParsimonyJobReferenceCompletion>(json, StrictWebJsonOptions)
            ?? throw new InvalidDataException("The Parsimony job result is null.");
        if (completion.ReportIds is null || completion.ReportIds.Count == 0 ||
            completion.ReportIds.Any(string.IsNullOrWhiteSpace) ||
            completion.ReportIds.Count == 1 && !StringComparer.Ordinal.Equals(completion.ReportId, completion.ReportIds[0]) ||
            completion.ReportIds.Count > 1 && completion.ReportId is not null ||
            completion.AssessmentIds is null)
            throw new InvalidDataException("The Parsimony job result is incomplete.");
        AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
            ArtifactReferenceTargetKind.ParsimonyBundle, completion.BundleId, recordId,
            "parsimony-report-job-result-bundle");
        foreach (var assessmentId in completion.AssessmentIds)
            AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                ArtifactReferenceTargetKind.Assessment, assessmentId, recordId,
                "parsimony-report-job-result-assessment");
    }

    private static void CaptureParsimonyCandidateJobResult(string storeIdentity, string recordId, string json,
        SnapshotBuilder builder)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The candidate Parsimony job result must be an object.");

        var valid = IsObjectShape(root, "candidate Parsimony job result",
            ["SchemaVersion", "MeasureId", "Candidate", "Before", "After", "FindingChanges",
                "ApprovedAnalysisPreservation"],
            ["SchemaVersion", "MeasureId", "Candidate", "Before", "After", "FindingChanges",
                "ApprovedAnalysisPreservation"]);
        var schemaVersions = Properties(root, "SchemaVersion").ToArray();
        if (schemaVersions.Length != 1 || schemaVersions[0].ValueKind != JsonValueKind.Number ||
            !schemaVersions[0].TryGetInt32(out var schemaVersion) || schemaVersion != 1) valid = false;
        var measureIds = Properties(root, "MeasureId").ToArray();
        if (measureIds.Length != 1 || measureIds[0].ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(measureIds[0].GetString())) valid = false;

        var candidates = Properties(root, "Candidate").ToArray();
        if (candidates.Length != 1) valid = false;
        foreach (var candidate in candidates)
            valid &= CaptureParsimonyCandidateBindingReferences(storeIdentity, recordId, candidate, builder);

        var beforeReports = Properties(root, "Before").ToArray();
        if (beforeReports.Length != 1) valid = false;
        foreach (var report in beforeReports)
            valid &= CaptureParsimonyCandidateReportReferences(storeIdentity, recordId,
                "parsimony-candidate-before", report, builder);

        var afterReports = Properties(root, "After").ToArray();
        if (afterReports.Length != 1) valid = false;
        foreach (var report in afterReports)
            valid &= CaptureParsimonyCandidateReportReferences(storeIdentity, recordId,
                "parsimony-candidate-after", report, builder);

        try
        {
            var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(json, StrictJsonOptions)
                ?? throw new InvalidDataException("The candidate Parsimony job result is null.");
            if (response.SchemaVersion != 1 || string.IsNullOrWhiteSpace(response.MeasureId) ||
                response.Candidate is null || response.Before is null || response.After is null ||
                response.FindingChanges is null || response.FindingChanges.Any(change => change is null) ||
                response.ApprovedAnalysisPreservation is null ||
                response.ApprovedAnalysisPreservation.UnmatchedBaselineTuples is null ||
                response.ApprovedAnalysisPreservation.UnmatchedBaselineTuples.Any(tuple => tuple is null))
                valid = false;
        }
        catch (Exception exception) when (IsStoredDataFailure(exception))
        {
            valid = false;
        }

        if (!valid) throw new InvalidDataException("The candidate Parsimony job result is incomplete.");
    }

    private static bool CaptureParsimonyCandidateBindingReferences(string storeIdentity, string recordId,
        JsonElement candidate, SnapshotBuilder builder)
    {
        if (candidate.ValueKind != JsonValueKind.Object) return false;
        var valid = IsObjectShape(candidate, "Parsimony candidate binding",
            ["IdentitySha256", "BaselineToken", "DryRunJobId", "ProposalId", "ProposalIntentDigest",
                "ProposalContentSha256", "Prerequisites", "DryRunEffectDigest", "DryRunAnchorDigest",
                "BaselineSourceSha256", "CandidateSourceSha256", "HumanJudgmentDigest"],
            ["IdentitySha256", "BaselineToken", "DryRunJobId", "ProposalId", "ProposalIntentDigest",
                "ProposalContentSha256", "Prerequisites", "DryRunEffectDigest", "DryRunAnchorDigest",
                "BaselineSourceSha256", "CandidateSourceSha256", "HumanJudgmentDigest"]);

        var dryRunIds = Properties(candidate, "DryRunJobId").ToArray();
        if (dryRunIds.Length != 1) valid = false;
        foreach (var dryRunId in dryRunIds)
        {
            if (dryRunId.ValueKind != JsonValueKind.String) valid = false;
            AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                ArtifactReferenceTargetKind.Job,
                dryRunId.ValueKind == JsonValueKind.String ? dryRunId.GetString() ?? string.Empty : string.Empty,
                recordId, "parsimony-candidate-job-result-dry-run-job");
        }

        var baselineTokens = Properties(candidate, "BaselineToken").ToArray();
        if (baselineTokens.Length != 1) valid = false;
        foreach (var baselineToken in baselineTokens)
        {
            if (baselineToken.ValueKind == JsonValueKind.Null) { valid = false; continue; }
            try
            {
                AddBaselineToken(builder, storeIdentity, baselineToken.GetRawText(), recordId,
                    "parsimony-candidate-job-result-baseline-token");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ParsimonyCandidateBinding>(candidate.GetRawText(), StrictJsonOptions)
                ?? throw new InvalidDataException("The Parsimony candidate binding is null.");
            if (parsed.BaselineToken is null || string.IsNullOrWhiteSpace(parsed.DryRunJobId) ||
                parsed.Prerequisites is null || parsed.Prerequisites.Any(prerequisite => prerequisite is null))
                valid = false;
        }
        catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
        return valid;
    }

    private static bool CaptureParsimonyCandidateReportReferences(string storeIdentity, string recordId,
        string prefix, JsonElement report, SnapshotBuilder builder)
    {
        if (report.ValueKind != JsonValueKind.Object) return false;
        var valid = IsObjectShape(report, "candidate Parsimony Report",
            ["ReportId", "Inputs", "Findings", "Text", "AssessmentIds", "MeasureRuns", "JoinQuality",
                "DispositionProjection", "Notes"], ["ReportId", "Inputs", "Findings", "Text", "Notes"]);
        var reportIds = Properties(report, "ReportId").ToArray();
        if (reportIds.Length != 1 || reportIds[0].ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(reportIds[0].GetString())) valid = false;

        var inputs = Properties(report, "Inputs").ToArray();
        if (inputs.Length != 1) valid = false;
        foreach (var input in inputs)
            valid &= CaptureParsimonyInputReferences(input, storeIdentity, recordId, prefix + "-input", builder,
                bundleReason: prefix + "-bundle", baselineTokenReason: prefix + "-baseline-token");

        var assessmentLists = Properties(report, "AssessmentIds").ToArray();
        if (assessmentLists.Length > 1) valid = false;
        foreach (var assessmentList in assessmentLists)
            valid &= CaptureStringReferences(assessmentList, ArtifactReferenceFamily.Jobs, storeIdentity, recordId,
                prefix + "-report", builder);

        var findings = Properties(report, "Findings").ToArray();
        if (findings.Length != 1) valid = false;
        foreach (var findingList in findings)
            valid &= CaptureCandidateFindingReferences(findingList, storeIdentity, recordId,
                prefix + "-finding-evidence", builder);

        foreach (var notes in Properties(report, "Notes")) valid &= ValidateNonNullArray(notes, "candidate Report notes");
        var measureRuns = Properties(report, "MeasureRuns").ToArray();
        if (measureRuns.Length > 1) valid = false;
        foreach (var list in measureRuns) valid &= ValidateNonNullArray(list, "candidate Report measure runs");
        var texts = Properties(report, "Text").ToArray();
        if (texts.Length != 1 || texts[0].ValueKind != JsonValueKind.String) valid = false;

        try
        {
            var parsed = JsonSerializer.Deserialize<ParsimonyReportResponse>(report.GetRawText(), StrictJsonOptions)
                ?? throw new InvalidDataException("The candidate Parsimony Report is null.");
            if (string.IsNullOrWhiteSpace(parsed.ReportId) || parsed.Inputs is null || parsed.Findings is null ||
                parsed.AssessmentIds is null || parsed.Findings.Any(finding => finding is null) || parsed.Notes is null ||
                parsed.Notes.Any(note => note is null) || parsed.MeasureRuns is null ||
                parsed.MeasureRuns.Any(run => run is null)) valid = false;
        }
        catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
        return valid;
    }

    private static bool CaptureCandidateFindingReferences(JsonElement findings, string storeIdentity, string recordId,
        string reason, SnapshotBuilder builder)
    {
        if (findings.ValueKind != JsonValueKind.Array) return false;
        var valid = true;
        foreach (var finding in findings.EnumerateArray())
        {
            if (finding.ValueKind != JsonValueKind.Object) { valid = false; continue; }
            valid &= IsObjectShape(finding, "candidate Parsimony finding",
                ["FindingId", "MeasureId", "Axis", "Tier", "AttachesTo", "GroupKey", "Number", "Threshold",
                    "EvidenceDigest", "EvidenceRefs", "RecipeLink", "Limitations", "Verification", "ItemNames"],
                ["FindingId", "MeasureId", "Axis", "Tier", "AttachesTo", "GroupKey", "Number", "Threshold",
                    "EvidenceDigest", "EvidenceRefs", "RecipeLink", "Limitations", "Verification"]);
            valid &= HasDefinedEnumDiscriminant(finding, "Axis", typeof(ParsimonyAxis));
            valid &= HasDefinedEnumDiscriminant(finding, "Tier", typeof(ParsimonyTier));
            valid &= HasDefinedEnumDiscriminant(finding, "Verification", typeof(ParsimonyVerification));

            var attachments = Properties(finding, "AttachesTo").ToArray();
            if (attachments.Length != 1 || attachments[0].ValueKind != JsonValueKind.Object) valid = false;
            foreach (var attachment in attachments.Where(value => value.ValueKind == JsonValueKind.Object))
            {
                valid &= IsObjectShape(attachment, "candidate Parsimony finding attachment",
                    ["Kind", "Identity", "AuthoredObjectKind", "GroupKind"], ["Kind", "Identity"]);
                valid &= HasDefinedEnumDiscriminant(attachment, "Kind", typeof(ParsimonyAttachmentKind));
                valid &= HasDefinedEnumDiscriminant(attachment, "AuthoredObjectKind",
                    typeof(ParsimonyAuthoredObjectKind), optional: true);
                valid &= HasDefinedEnumDiscriminant(attachment, "GroupKind", typeof(ParsimonyGroupKind), optional: true);
            }

            var evidenceLists = Properties(finding, "EvidenceRefs").ToArray();
            if (evidenceLists.Length != 1 || evidenceLists[0].ValueKind != JsonValueKind.Array) valid = false;
            foreach (var evidenceList in evidenceLists.Where(value => value.ValueKind == JsonValueKind.Array))
            foreach (var evidenceReference in evidenceList.EnumerateArray())
            {
                if (evidenceReference.ValueKind != JsonValueKind.Object) { valid = false; continue; }
                valid &= IsObjectShape(evidenceReference, "candidate finding evidence reference",
                    ["BundleId", "View", "Arguments"], ["BundleId", "View", "Arguments"]);
                var bundles = Properties(evidenceReference, "BundleId").ToArray();
                if (bundles.Length != 1) valid = false;
                foreach (var bundle in bundles)
                {
                    if (bundle.ValueKind != JsonValueKind.String) valid = false;
                    AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
                        ArtifactReferenceTargetKind.ParsimonyBundle,
                        bundle.ValueKind == JsonValueKind.String ? bundle.GetString() ?? string.Empty : string.Empty,
                        recordId, reason);
                }
                var views = Properties(evidenceReference, "View").ToArray();
                if (views.Length != 1 || views[0].ValueKind != JsonValueKind.String ||
                    !ParsimonyViews.All.Contains(views[0].GetString() ?? string.Empty)) valid = false;
                var arguments = Properties(evidenceReference, "Arguments").ToArray();
                if (arguments.Length != 1 || arguments[0].ValueKind != JsonValueKind.Object) valid = false;
            }
            var findingIds = Properties(finding, "FindingId").ToArray();
            if (findingIds.Length != 1 || findingIds[0].ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(findingIds[0].GetString())) valid = false;
            var evidenceDigests = Properties(finding, "EvidenceDigest").ToArray();
            if (evidenceDigests.Length != 1 || evidenceDigests[0].ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(evidenceDigests[0].GetString())) valid = false;

            try
            {
                var parsed = JsonSerializer.Deserialize<ParsimonyFinding>(finding.GetRawText(), StrictJsonOptions)
                    ?? throw new InvalidDataException("A candidate Parsimony finding is null.");
                if (parsed.EvidenceRefs is null || parsed.EvidenceRefs.Any(reference => reference is null) ||
                    parsed.Limitations is null || parsed.Limitations.Any(limitation => limitation is null) ||
                    parsed.ItemNames is null || parsed.ItemNames.Any(name => name is null)) valid = false;
            }
            catch (Exception exception) when (IsStoredDataFailure(exception)) { valid = false; }
        }
        return valid;
    }

    private static void CapturePhonologyCheckJobResult(string storeIdentity, string recordId, string json,
        SnapshotBuilder builder)
    {
        RequireObjectShape(json, "phonology-check job result",
            ["assessmentIds", "assessmentId", "revisionId"], ["assessmentIds", "assessmentId", "revisionId"]);
        var completion = JsonSerializer.Deserialize<PhonologyCheckJobReferenceCompletion>(json, StrictWebJsonOptions)
            ?? throw new InvalidDataException("The phonology-check job result is null.");
        if (completion.AssessmentIds is null || completion.AssessmentIds.Count != 1 ||
            string.IsNullOrWhiteSpace(completion.AssessmentId) ||
            !StringComparer.Ordinal.Equals(completion.AssessmentIds[0], completion.AssessmentId) ||
            string.IsNullOrWhiteSpace(completion.RevisionId))
            throw new InvalidDataException("The phonology-check job result is incomplete.");
        AddRequiredReference(builder, ArtifactReferenceFamily.Jobs, storeIdentity,
            ArtifactReferenceTargetKind.Assessment, completion.AssessmentId, recordId,
            "phonology-check-job-result-assessment");
    }

    private static void CaptureKnownJobFailureResult(string json)
    {
        RequireObjectShape(json, "job failure result", "detail");
        var failure = JsonSerializer.Deserialize<JobFailureReferenceCompletion>(json, StrictJsonOptions)
            ?? throw new InvalidDataException("The job failure result is null.");
        if (string.IsNullOrWhiteSpace(failure.Detail))
            throw new InvalidDataException("The job failure result has no detail.");
    }

    private static bool IsArtifactReferenceJobKind(string kind) => kind is "dry-run" or TrialJobHandler.TrialKind or
        ParsimonyJobHandler.JobKind or ParsimonyCandidateEvidenceBuilder.JobKind or PhonologyCheckJobHandler.JobKind;

    private static bool IsSuccessfulJobStatus(JobStatus status) => status is JobStatus.Completed or
        JobStatus.CompletedDryRunOnly or JobStatus.CompletedWithAssessmentFailure;

    private static bool IsSuccessfulJobResultStatus(string kind, string status) => kind switch
    {
        "dry-run" => status == JobStatusJson.ToWire(JobStatus.CompletedDryRunOnly),
        TrialJobHandler.TrialKind => status == JobStatusJson.ToWire(JobStatus.Completed),
        ParsimonyJobHandler.JobKind or ParsimonyCandidateEvidenceBuilder.JobKind or
            PhonologyCheckJobHandler.JobKind => status == JobStatusJson.ToWire(JobStatus.Completed),
        _ => false,
    };

    private static void CapturePublishedDryRun(string storeIdentity, string recordId, string json,
        SnapshotBuilder builder)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireObjectShape(root, "published Dry Run",
                ["intentDigest", "baselineNote", "expectedEffects", "effectDigest", "anchor"],
                ["intentDigest", "baselineNote", "expectedEffects", "effectDigest", "anchor"]);
            if (root.GetProperty("intentDigest").ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(root.GetProperty("intentDigest").GetString()) ||
                root.GetProperty("baselineNote").ValueKind != JsonValueKind.String ||
                root.GetProperty("effectDigest").ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(root.GetProperty("effectDigest").GetString()))
                throw new InvalidDataException("Published Dry Run required text is invalid.");
            var effects = root.GetProperty("expectedEffects");
            if (effects.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Published Dry Run effects must be an array.");
            foreach (var effect in effects.EnumerateArray())
            {
                if (effect.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Published effect is null or not an object.");
                RequireObjectShape(effect, "published expected effect",
                    ["canonicalId", "field", "before", "after", "preview"],
                    ["canonicalId", "field", "before", "after"]);
                if (effect.GetProperty("before").ValueKind != JsonValueKind.Object ||
                    effect.GetProperty("after").ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Published expected effect maps must be objects.");
                _ = JsonSerializer.Deserialize<ExpectedEffect>(effect.GetRawText(), StrictWebJsonOptions)
                    ?? throw new InvalidDataException("Published expected effect is null.");
            }
            var anchors = Properties(root, "anchor").ToArray();
            if (anchors.Length != 1) throw new InvalidDataException("Published Dry Run anchor is absent or repeated.");
            var anchor = anchors[0];
            RequireBoundAnchorShape(anchor.GetRawText());
            var parsed = JsonSerializer.Deserialize<BoundDryRunAnchor>(anchor.GetRawText(), StrictAnchorJsonOptions)
                ?? throw new InvalidDataException("The published Dry Run anchor is null.");
            if (new[] { parsed.IntentDigest, parsed.FootprintDigest, parsed.EffectDigest, parsed.RunnerVersion,
                    parsed.LibLcmVersion, parsed.ProjectionVersion, parsed.DryRunAtUtc }
                .Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("The published Dry Run anchor is incomplete.");
            builder.Retain(ArtifactReferenceFamily.Jobs, storeIdentity, recordId,
                "published-dry-run-anchor-has-no-artifact-identity");
        }
        catch (Exception exception) when (IsStoredDataFailure(exception))
        {
            builder.Incomplete(ArtifactReferenceFamily.Jobs, storeIdentity,
                "published-dry-run-shape-unrecognized", recordId);
        }
    }

    private static void CaptureWorkflowRecords(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        CaptureTable(builder, ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
            () => CaptureProposalAnchors(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
            () => CaptureProposalRevisions(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
            () => CaptureReceipts(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
            () => CaptureReviewMarks(store, builder));
        CaptureTable(builder, ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
            () => CaptureAppliedIndex(store, builder));
    }

    private static void CaptureProposalAnchors(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT ProposalId, AnchorJson FROM Proposals ORDER BY ProposalId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var recordId = "Proposals/" + id + "/AnchorJson";
            var json = NullableString(reader, 1);
            if (json is null) continue;
            try
            {
                RequireBoundAnchorShape(json);
                _ = JsonSerializer.Deserialize<BoundDryRunAnchor>(json, StrictAnchorJsonOptions)
                    ?? throw new InvalidDataException("The Proposal anchor is null.");
                builder.Retain(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity, recordId,
                    "typed-proposal-anchor-has-no-artifact-identity");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception))
            {
                builder.Incomplete(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
                    "proposal-anchor-shape-unrecognized", recordId);
            }
        }
    }

    private static void CaptureProposalRevisions(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT ProposalId, IntentDigest, ProposalJson FROM ProposalRevisions " +
            "ORDER BY ProposalId, IntentDigest;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordId = "ProposalRevisions/" + reader.GetString(0) + "/" + reader.GetString(1);
            try
            {
                var bytes = reader.GetFieldValue<byte[]>(2);
                _ = ProposalJsonParser.Parse(Encoding.UTF8.GetString(bytes));
                builder.Retain(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity, recordId,
                    "typed-proposal-revision-has-no-artifact-binding");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception))
            {
                builder.Incomplete(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
                    "proposal-revision-shape-unrecognized", recordId);
            }
        }
    }

    private static void CaptureReceipts(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT ReceiptId, ReceiptJson FROM Receipts ORDER BY ReceiptId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordId = "Receipts/" + reader.GetString(0);
            try
            {
                var json = reader.GetString(1);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                RequireObjectShape(root, "Apply Receipt",
                    ["ProposalId", "IntentDigest", "AlreadyApplied", "BaselineNote", "ResultNote", "ActualEffects",
                        "EffectDigest", "AppliedLogEntry", "Mode", "Message"],
                    ["ProposalId", "IntentDigest", "AlreadyApplied", "BaselineNote", "ResultNote", "ActualEffects",
                        "EffectDigest", "AppliedLogEntry"]);
                var effects = root.GetProperty("ActualEffects");
                if (effects.ValueKind != JsonValueKind.Array ||
                    effects.EnumerateArray().Any(effect => effect.ValueKind != JsonValueKind.Object))
                    throw new InvalidDataException("Receipt ActualEffects must be a non-null array of objects.");
                var logEntry = root.GetProperty("AppliedLogEntry");
                RequireObjectShape(logEntry, "Receipt AppliedLogEntry",
                    ["ProposalId", "Format", "TimestampUtc", "User", "IntentDigest", "Description", "Mode"],
                    ["ProposalId", "Format", "TimestampUtc", "User", "IntentDigest", "Description"]);
                var parsed = JsonSerializer.Deserialize<Receipt>(json, StrictJsonOptions)
                    ?? throw new InvalidDataException("The Receipt is null.");
                if (parsed.ActualEffects is null || parsed.ActualEffects.Any(effect => effect is null) ||
                    parsed.AppliedLogEntry is null)
                    throw new InvalidDataException("The Receipt contains a null typed reference node.");
                builder.Retain(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity, recordId,
                    "typed-receipt-has-no-artifact-binding");
            }
            catch (Exception exception) when (IsStoredDataFailure(exception))
            {
                builder.Incomplete(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
                    "receipt-shape-unrecognized", recordId);
            }
        }
    }

    private static void CaptureReviewMarks(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT ProposalId, EntryKey FROM ReviewMarks ORDER BY ProposalId, EntryKey;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordId = "ReviewMarks/" + reader.GetString(0) + "/" + reader.GetString(1);
            builder.Retain(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity, recordId,
                "review-decision-mark-has-no-artifact-binding");
        }
    }

    private static void CaptureAppliedIndex(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT ProposalId, IntentDigest, RecordJson FROM AppliedIndex " +
            "ORDER BY ProposalId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordId = "AppliedIndex/" + reader.GetString(0) + "/" + reader.GetString(1);
            builder.Retain(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity, recordId,
                "applied-index-record");
            if (!reader.IsDBNull(2))
                builder.Incomplete(ArtifactReferenceFamily.WorkflowRecords, store.StoreIdentity,
                    "applied-index-payload-has-no-typed-reader", recordId);
        }
    }

    private static void CaptureParsimonyBundles(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using (var command = Command(store, "SELECT BundleId, BaselineTokenJson, EvidenceDirectory, " +
            "GrammarFactsPath, EvidencePath, BaselineRootDirectory, InputKind, CandidateIdentity, State " +
            "FROM ParsimonyBundles ORDER BY BundleId;"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var bundleId = reader.GetString(0);
                var recordId = "ParsimonyBundles/" + bundleId;
                builder.Reference(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity,
                    ArtifactReferenceTargetKind.ParsimonyBundle, bundleId, recordId, "parsimony-bundle-row");
                AddPath(builder, ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                    "parsimony-bundle-directory", reader.GetString(2));
                AddPath(builder, ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                    "parsimony-grammar-facts-path", reader.GetString(3));
                AddPath(builder, ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                    "parsimony-evidence-path", reader.GetString(4));
                AddPath(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity, recordId,
                    "parsimony-baseline-root", reader.GetString(5));
                builder.Retain(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                    "parsimony-bundle-row");
                try
                {
                    AddBaselineToken(builder, store.StoreIdentity, reader.GetString(1), recordId,
                        "parsimony-bundle-baseline-token");
                }
                catch (Exception exception) when (IsStoredDataFailure(exception))
                {
                    builder.Incomplete(ArtifactReferenceFamily.Baselines, store.StoreIdentity,
                        "parsimony-bundle-baseline-token-unrecognized", recordId);
                }
                var inputKind = reader.GetString(6);
                var candidateIdentity = NullableString(reader, 7);
                var state = reader.GetString(8);
                if (inputKind is not ("baseline" or "candidate") ||
                    (inputKind == "candidate") != !string.IsNullOrWhiteSpace(candidateIdentity) ||
                    state is not ("available" or "deleting" or "missing" or "corrupt"))
                    builder.Incomplete(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity,
                        "parsimony-bundle-descriptor-unrecognized", recordId);
            }
        }

        CaptureBundleIds(store, builder, "SELECT ProjectKey, InputKind, BundleId FROM CurrentParsimonyBundles " +
            "ORDER BY ProjectKey, InputKind;", "current-parsimony-bundle");
        CaptureBundleReferences(store, builder);
        CaptureBundleIds(store, builder, "SELECT LeaseId, BundleId FROM ParsimonyReaderLeases ORDER BY LeaseId;",
            "parsimony-reader-lease");

        using var claims = Command(store, "SELECT BundleId, JobId, BaselineRootDirectory, WorkingDirectory, " +
            "StagingDirectory, FinalDirectory FROM ParsimonyBuildClaims ORDER BY BundleId;");
        using var claimReader = claims.ExecuteReader();
        while (claimReader.Read())
        {
            var bundleId = claimReader.GetString(0);
            var recordId = "ParsimonyBuildClaims/" + bundleId + "/" + claimReader.GetString(1);
            builder.Reference(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity,
                ArtifactReferenceTargetKind.ParsimonyBundle, bundleId, recordId, "parsimony-build-claim");
            AddPath(builder, ArtifactReferenceFamily.Baselines, store.StoreIdentity, recordId,
                "parsimony-build-baseline-root", claimReader.GetString(2));
            AddPath(builder, ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                "parsimony-build-working-directory", claimReader.GetString(3));
            AddOptionalPath(builder, ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                "parsimony-build-staging-directory",
                NullableString(claimReader, 4));
            AddOptionalPath(builder, ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                "parsimony-build-final-directory",
                NullableString(claimReader, 5));
            builder.Retain(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                "parsimony-build-claim");
        }
    }

    private static void CaptureBundleIds(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder, string sql,
        string reason)
    {
        using var command = Command(store, sql);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var recordId = "ParsimonyReferences/" + reason + "/" + reader.GetString(0);
            var bundleId = reader.GetString(reader.FieldCount - 1);
            builder.Reference(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity,
                ArtifactReferenceTargetKind.ParsimonyBundle, bundleId, recordId, reason);
            builder.Retain(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId, reason);
        }
    }

    private static void CaptureBundleReferences(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        using var command = Command(store, "SELECT BundleId, ReferenceKind, ReferenceId FROM ParsimonyBundleReferences " +
            "ORDER BY BundleId, ReferenceKind, ReferenceId;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var bundleId = reader.GetString(0);
            var kind = reader.GetString(1);
            var referenceId = reader.GetString(2);
            var recordId = $"ParsimonyBundleReferences/{kind}/{referenceId}";
            builder.Reference(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity,
                ArtifactReferenceTargetKind.ParsimonyBundle, bundleId, recordId, "parsimony-bundle-reference-" + kind);
            builder.Retain(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity, recordId,
                "parsimony-bundle-reference");
            if (kind is not ("report" or "check-run" or "job"))
                builder.Incomplete(ArtifactReferenceFamily.ParsimonyBundles, store.StoreIdentity,
                    "parsimony-bundle-reference-kind-unrecognized", recordId);
        }
    }

    private static void CaptureParsimonyEvidence(PrivateArtifactStoreSnapshot store, SnapshotBuilder builder)
    {
        var connection = store.Connection!;
        var transaction = store.Transaction!;
        try
        {
            using var metadata = Command(store, "SELECT application_id, schema_version, baseline_token_json, " +
                "input_kind, candidate_identity, complete, source_sha256, model_fingerprint " +
                "FROM artifact_metadata WHERE singleton = 1;");
            using var reader = metadata.ExecuteReader();
            if (!reader.Read())
            {
                builder.Incomplete(ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                    "parsimony-evidence-publication-unrecognized", "artifact_metadata/1");
            }
            else
            {
                var valid = true;
                var applicationId = reader.GetInt32(0);
                var schemaVersion = reader.GetInt32(1);
                var tokenJson = reader.GetString(2);
                var inputKind = reader.GetString(3);
                var candidateIdentity = NullableString(reader, 4);
                var complete = reader.GetInt64(5);
                var sourceSha256 = reader.GetString(6);
                var modelFingerprint = reader.GetString(7);
                try
                {
                    AddBaselineToken(builder, store.StoreIdentity, tokenJson,
                        "artifact_metadata/1", "parsimony-evidence-baseline-token");
                }
                catch (Exception exception) when (IsStoredDataFailure(exception) || exception is InvalidCastException)
                {
                    valid = false;
                }
                if (applicationId != EvidenceSchema.ApplicationId || schemaVersion != EvidenceSchema.Version ||
                    complete != 1 || inputKind is not ("baseline" or "candidate") ||
                    (inputKind == "candidate") != !string.IsNullOrWhiteSpace(candidateIdentity) ||
                    !IsCanonicalSha256(sourceSha256) || string.IsNullOrWhiteSpace(modelFingerprint)) valid = false;
                if (reader.Read()) valid = false;
                if (!valid)
                    builder.Incomplete(ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                        "parsimony-evidence-publication-unrecognized", "artifact_metadata/1");
            }
        }
        catch (Exception exception) when (IsStoredDataFailure(exception) || exception is InvalidCastException)
        {
            builder.Incomplete(ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                "parsimony-evidence-publication-unrecognized", "artifact_metadata/1");
        }

        using var runs = connection.CreateCommand();
        runs.Transaction = transaction;
        runs.CommandText = "SELECT assessment_id, invocation_id, source_sha256, status FROM parser_runs " +
            "ORDER BY assessment_id;";
        using var runReader = runs.ExecuteReader();
        var rowNumber = 0;
        while (runReader.Read())
        {
            rowNumber++;
            string? assessmentId = null;
            var recordId = "parser_runs/row-" + rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                assessmentId = NullableString(runReader, 0);
                if (!string.IsNullOrWhiteSpace(assessmentId)) recordId = "parser_runs/" + assessmentId;
                var invocationId = NullableString(runReader, 1);
                var sourceDigest = NullableString(runReader, 2);
                var status = NullableString(runReader, 3);
                AddRequiredReference(builder, ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                    ArtifactReferenceTargetKind.Assessment, assessmentId ?? string.Empty, recordId,
                    "parser-run-assessment");
                AddRequiredReference(builder, ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                    ArtifactReferenceTargetKind.Invocation, invocationId ?? string.Empty, recordId,
                    "parser-run-invocation");
                AddRequiredReference(builder, ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                    ArtifactReferenceTargetKind.ContentDigest, sourceDigest ?? string.Empty, recordId,
                    "parser-run-source-digest");
                builder.Retain(ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity, recordId,
                    "parser-run-citation");
                if (!IsCanonicalSha256(sourceDigest) || status is not ("complete" or "unavailable"))
                    builder.Incomplete(ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                        "parser-run-reference-unrecognized", recordId);
            }
            catch (Exception exception) when (IsStoredDataFailure(exception) || exception is InvalidCastException)
            {
                if (!string.IsNullOrWhiteSpace(assessmentId))
                    AddRequiredReference(builder, ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                        ArtifactReferenceTargetKind.Assessment, assessmentId, recordId, "parser-run-assessment");
                builder.Retain(ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity, recordId,
                    "parser-run-citation");
                builder.Incomplete(ArtifactReferenceFamily.ParsimonyEvidence, store.StoreIdentity,
                    "parser-run-reference-unrecognized", recordId);
            }
        }
    }

    private static bool IsCanonicalSha256(string? value) => value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static void ResolveBaselineTokens(SnapshotBuilder builder)
    {
        var publications = builder.BaselinePublications.GroupBy(
                item => (item.ProjectIdentity, item.BundleDigest))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var tokens = builder.References.Where(reference => reference.TargetKind == ArtifactReferenceTargetKind.BaselineBundleDigest)
            .ToArray();
        foreach (var token in tokens)
        {
            if (token.ScopeIdentity is not null && publications.TryGetValue((token.ScopeIdentity, token.Target),
                    out var matches) && matches.Length == 1)
            {
                var publication = matches[0];
                builder.Reference(ArtifactReferenceFamily.Baselines, token.StoreIdentity,
                    ArtifactReferenceTargetKind.Path, publication.RootDirectory, token.RetainingRecordId,
                    "baseline-token-resolved-root", token.ScopeIdentity);
                builder.Reference(ArtifactReferenceFamily.Baselines, token.StoreIdentity,
                    ArtifactReferenceTargetKind.Path, publication.FwDataPath, token.RetainingRecordId,
                    "baseline-token-resolved-fwdata", token.ScopeIdentity);
            }
            else if (token.ScopeIdentity is not null && publications.TryGetValue((token.ScopeIdentity, token.Target),
                         out matches))
            {
                foreach (var publication in matches)
                {
                    builder.Reference(ArtifactReferenceFamily.Baselines, token.StoreIdentity,
                        ArtifactReferenceTargetKind.Path, publication.RootDirectory, token.RetainingRecordId,
                        "baseline-token-ambiguous-root", token.ScopeIdentity);
                    builder.Reference(ArtifactReferenceFamily.Baselines, token.StoreIdentity,
                        ArtifactReferenceTargetKind.Path, publication.FwDataPath, token.RetainingRecordId,
                        "baseline-token-ambiguous-fwdata", token.ScopeIdentity);
                }
                builder.Incomplete(ArtifactReferenceFamily.Baselines, token.StoreIdentity,
                    "baseline-token-path-ambiguous", token.RetainingRecordId);
            }
            else
            {
                builder.Incomplete(ArtifactReferenceFamily.Baselines, token.StoreIdentity,
                    "baseline-token-path-unresolved", token.RetainingRecordId);
            }
        }
    }

    private static void CheckParsimonySnapshotCoverage(SnapshotBuilder builder)
    {
        foreach (var bundle in builder.References.Where(reference =>
                     reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle))
        {
            if (!builder.Stores.Any(store => store.StoreKind == ArtifactStoreKind.ParsimonyEvidence &&
                    StringComparer.Ordinal.Equals(store.RelatedBundleId, bundle.Target)))
                builder.Incomplete(ArtifactReferenceFamily.ParsimonyEvidence, bundle.StoreIdentity,
                    "parsimony-evidence-snapshot-not-supplied", bundle.RetainingRecordId);
            if (!builder.Stores.Any(store => store.StoreKind == ArtifactStoreKind.ParsimonyEvidence &&
                    StringComparer.Ordinal.Equals(store.RelatedBundleId, bundle.Target)))
                builder.IncompleteDomain();
        }
        foreach (var snapshot in builder.Stores.Where(store => store.StoreKind == ArtifactStoreKind.ParsimonyEvidence))
        {
            if (!builder.References.Any(reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
                    StringComparer.Ordinal.Equals(reference.Target, snapshot.RelatedBundleId)))
                builder.Incomplete(ArtifactReferenceFamily.ParsimonyEvidence, snapshot.StoreIdentity,
                    "parsimony-evidence-owner-not-supplied", snapshot.StoreIdentity);
            if (!builder.References.Any(reference => reference.TargetKind == ArtifactReferenceTargetKind.ParsimonyBundle &&
                    StringComparer.Ordinal.Equals(reference.Target, snapshot.RelatedBundleId)))
                builder.IncompleteDomain();
        }
    }

    private static void AddBaselineToken(SnapshotBuilder builder, string storeIdentity, string json,
        string recordId, string reason)
    {
        RequireBaselineTokenShape(json);
        var token = JsonSerializer.Deserialize<BaselineToken>(json, BaselineJsonOptions)
            ?? throw new InvalidDataException("The Baseline token is null.");
        AddBaselineToken(builder, storeIdentity, token, recordId, reason);
    }

    private static JsonSerializerOptions BaselineJsonOptions { get; } = CreateBaselineJsonOptions();

    private static void RequireBaselineTokenShape(string json)
    {
        string[] camel = ["projectIdentity", "semanticSnapshotDigest", "projectionVersion", "capturedUtc",
            "bundleDigest", "capturedHostSessionId", "capturedEditGeneration"];
        string[] pascal = ["ProjectIdentity", "SemanticSnapshotDigest", "ProjectionVersion", "CapturedUtc",
            "BundleDigest", "CapturedHostSessionId", "CapturedEditGeneration"];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Baseline token must be an object.");
        var names = root.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.All(name => camel.Contains(name, StringComparer.Ordinal)))
        {
            RequireObjectShape(root, "Baseline token", camel,
                ["projectIdentity", "semanticSnapshotDigest", "projectionVersion", "capturedUtc", "bundleDigest"]);
            RequirePairedOptionalProperties(root, "capturedHostSessionId", "capturedEditGeneration");
        }
        else if (names.All(name => pascal.Contains(name, StringComparer.Ordinal)))
        {
            RequireObjectShape(root, "Baseline token", pascal,
                ["ProjectIdentity", "SemanticSnapshotDigest", "ProjectionVersion", "CapturedUtc", "BundleDigest"]);
            RequirePairedOptionalProperties(root, "CapturedHostSessionId", "CapturedEditGeneration");
        }
        else
            throw new InvalidDataException("Baseline token has a mixed, repeated, or unrecognized property.");
    }

    private static void RequirePairedOptionalProperties(JsonElement root, string first, string second)
    {
        if (root.TryGetProperty(first, out _) != root.TryGetProperty(second, out _))
            throw new InvalidDataException("Baseline token optional capture fields must be paired.");
    }

    private static void RequireBoundAnchorShape(string json)
    {
        string[] camel = ["intentDigest", "footprintDigest", "effectDigest", "runnerVersion", "libLcmVersion",
            "projectionVersion", "dryRunAtUtc"];
        string[] pascal = ["IntentDigest", "FootprintDigest", "EffectDigest", "RunnerVersion", "LibLcmVersion",
            "ProjectionVersion", "DryRunAtUtc"];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Dry Run anchor must be an object.");
        var names = root.EnumerateObject().Select(property => property.Name).ToArray();
        string[] fields;
        if (names.All(name => camel.Contains(name, StringComparer.Ordinal))) fields = camel;
        else if (names.All(name => pascal.Contains(name, StringComparer.Ordinal))) fields = pascal;
        else
            throw new InvalidDataException("Dry Run anchor has a mixed, repeated, or unrecognized property.");
        RequireObjectShape(root, "Dry Run anchor", fields, fields);
        foreach (var field in fields)
            if (root.GetProperty(field).ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(root.GetProperty(field).GetString()))
                throw new InvalidDataException("Dry Run anchor members must be nonblank strings.");
    }

    private static void RequireDryRunSourceBindingShape(JsonElement source)
    {
        string[] pascal = ["Token", "RootDirectory", "FwDataPath", "SourceSha256"];
        string[] camel = ["token", "rootDirectory", "fwDataPath", "sourceSha256"];
        if (source.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Dry Run source binding must be an object.");
        var names = source.EnumerateObject().Select(property => property.Name).ToArray();
        string[] fields;
        if (names.All(name => pascal.Contains(name, StringComparer.Ordinal))) fields = pascal;
        else if (names.All(name => camel.Contains(name, StringComparer.Ordinal))) fields = camel;
        else throw new InvalidDataException("Dry Run source binding has mixed or unsupported properties.");
        RequireObjectShape(source, "Dry Run source binding", fields, fields);
        var tokenName = fields[0];
        var rootName = fields[1];
        var pathName = fields[2];
        var digestName = fields[3];
        var token = source.GetProperty(tokenName);
        RequireBaselineTokenShape(token.GetRawText());
        foreach (var name in new[] { rootName, pathName })
            if (source.GetProperty(name).ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(source.GetProperty(name).GetString()))
                throw new InvalidDataException("Dry Run source binding paths must be nonblank.");
        if (source.GetProperty(digestName).ValueKind != JsonValueKind.String ||
            !IsCanonicalSha256(source.GetProperty(digestName).GetString()))
            throw new InvalidDataException("Dry Run source binding digest is invalid.");
    }

    private static void AddBaselineToken(SnapshotBuilder builder, string storeIdentity, BaselineToken token,
        string recordId, string reason)
    {
        builder.Reference(ArtifactReferenceFamily.Baselines, storeIdentity,
            ArtifactReferenceTargetKind.BaselineBundleDigest, token.BundleDigest, recordId, reason,
            token.ProjectIdentity);
        builder.Retain(ArtifactReferenceFamily.Baselines, storeIdentity, recordId, reason);
    }

    private static void AddOptionalReference(SnapshotBuilder builder, ArtifactReferenceFamily family,
        string storeIdentity, ArtifactReferenceTargetKind kind, string? target, string recordId, string reason)
    {
        if (target is null) return;
        AddRequiredReference(builder, family, storeIdentity, kind, target, recordId, reason);
    }

    private static void AddRequiredReference(SnapshotBuilder builder, ArtifactReferenceFamily family,
        string storeIdentity, ArtifactReferenceTargetKind kind, string target, string recordId, string reason)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            builder.Incomplete(family, storeIdentity, "blank-reference-value", recordId);
            return;
        }
        builder.Reference(family, storeIdentity, kind, target, recordId, reason);
    }

    private static void AddPath(SnapshotBuilder builder, ArtifactReferenceFamily family, string storeIdentity,
        string recordId, string reason, string path) => AddRequiredReference(builder, family, storeIdentity,
        ArtifactReferenceTargetKind.Path, path, recordId, reason);

    private static void AddScopedPath(SnapshotBuilder builder, ArtifactReferenceFamily family, string storeIdentity,
        string recordId, string reason, string path, string scopeIdentity)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            builder.Incomplete(family, storeIdentity, "blank-reference-value", recordId);
            return;
        }
        builder.Reference(family, storeIdentity, ArtifactReferenceTargetKind.Path, path, recordId, reason, scopeIdentity);
    }

    private static void AddOptionalPath(SnapshotBuilder builder, ArtifactReferenceFamily family, string storeIdentity,
        string recordId, string reason, string? path)
    {
        if (path is not null) AddPath(builder, family, storeIdentity, recordId, reason, path);
    }

    private static SqliteCommand Command(PrivateArtifactStoreSnapshot store, string sql)
    {
        var command = store.Connection!.CreateCommand();
        command.Transaction = store.Transaction;
        command.CommandText = sql;
        return command;
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? NullableLong(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static bool IsActiveJob(JobStatus status) => status is JobStatus.Queued or JobStatus.WaitingForBaseline or
        JobStatus.WaitingForProjectHost or JobStatus.Running;

    private static bool ReadStoredBoolean(SqliteDataReader reader, int ordinal, string name)
    {
        var value = reader.GetInt64(ordinal);
        if (value is not 0 and not 1) throw new InvalidDataException("Job " + name + " must be 0 or 1.");
        return value == 1;
    }

    private static void RequireObjectShape(string json, string description, params string[] allowedProperties)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException(description + " must be an object.");
        var allowed = allowedProperties.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name))
                throw new InvalidDataException(description + " has a repeated or unrecognized property.");
    }

    private static void RequireObjectShape(string json, string description, string[] allowedProperties,
        string[] requiredProperties)
    {
        using var document = JsonDocument.Parse(json);
        RequireObjectShape(document.RootElement, description, allowedProperties, requiredProperties);
    }

    private static void RequireObjectShape(JsonElement root, string description, string[] allowedProperties,
        string[] requiredProperties)
    {
        if (!IsObjectShape(root, description, allowedProperties, requiredProperties))
            throw new InvalidDataException(description + " has a missing, repeated, or unrecognized property.");
    }

    private static bool IsStoredDataFailure(Exception exception) => exception is SqliteException or JsonException or
        InvalidDataException or ContractParseException or ArgumentException or FormatException or InvalidOperationException or
        NotSupportedException or OverflowException;

    private static JsonSerializerOptions CreateStrictWebJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private static JsonSerializerOptions CreateStrictJsonOptions()
    {
        var options = MotifJson.CreateOptions();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.RespectRequiredConstructorParameters = true;
        return options;
    }

    private static JsonSerializerOptions CreateBaselineJsonOptions()
    {
        var options = MotifJson.CreateOptions();
        options.PropertyNameCaseInsensitive = true;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        return options;
    }

    private static JsonSerializerOptions StrictAnchorJsonOptions { get; } = CreateStrictAnchorJsonOptions();

    private static JsonSerializerOptions CreateStrictAnchorJsonOptions()
    {
        var options = CreateStrictJsonOptions();
        options.PropertyNameCaseInsensitive = true;
        return options;
    }

    private static ArtifactReferenceFamily[] FamiliesFor(ArtifactStoreKind storeKind) => storeKind switch
    {
        ArtifactStoreKind.Project => Enum.GetValues<ArtifactReferenceFamily>()
            .Where(family => family != ArtifactReferenceFamily.ParsimonyEvidence).ToArray(),
        ArtifactStoreKind.ParsimonyEvidence => [ArtifactReferenceFamily.ParsimonyEvidence, ArtifactReferenceFamily.Baselines],
        _ => Enum.GetValues<ArtifactReferenceFamily>(),
    };

    private sealed record TrialJobReferenceCompletion(
        [property: JsonPropertyName("baselineToken")] BaselineToken BaselineToken,
        [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string> AssessmentIds,
        [property: JsonPropertyName("allWords")] bool AllWords,
        [property: JsonPropertyName("grammarCheckWarning")] string? GrammarCheckWarning);

    private sealed record ParsimonyJobReferenceCompletion(
        [property: JsonPropertyName("reportIds")] IReadOnlyList<string> ReportIds,
        [property: JsonPropertyName("bundleId")] string BundleId,
        [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string> AssessmentIds,
        [property: JsonPropertyName("reportId")] string? ReportId = null);

    private sealed record PhonologyCheckJobReferenceCompletion(
        [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string> AssessmentIds,
        [property: JsonPropertyName("assessmentId")] string AssessmentId,
        [property: JsonPropertyName("revisionId")] string RevisionId);

    private sealed record JobFailureReferenceCompletion(
        [property: JsonPropertyName("detail")] string Detail);

    private sealed class SnapshotBuilder
    {
        private readonly Dictionary<ArtifactReferenceFamily, bool> _complete = Enum
            .GetValues<ArtifactReferenceFamily>().ToDictionary(family => family, _ => true);
        private readonly List<ArtifactReference> _references = [];
        private readonly List<ArtifactRetentionRecord> _retentionRecords = [];
        private readonly List<ArtifactBaselinePublication> _baselinePublications = [];
        private readonly List<ArtifactInvocationProvenance> _invocationProvenance = [];
        private readonly List<ArtifactReferenceDiagnostic> _diagnostics = [];
        private readonly ArtifactStoreIdentity[] _stores;
        private ArtifactReferenceDomainStatus _domain;

        internal SnapshotBuilder(IReadOnlyList<PrivateArtifactStoreSnapshot> stores,
            ArtifactReferenceDomainDeclaration declaration)
        {
            _stores = stores.Select(store => new ArtifactStoreIdentity(store.StoreIdentity, store.StoreKind,
                store.DeclaredSchemaVersion, store.SnapshotFingerprint, store.RelatedBundleId)).ToArray();
            var declared = declaration.StoreIdentities.ToHashSet(StringComparer.Ordinal);
            var actual = stores.Select(store => store.StoreIdentity).ToArray();
            var actualSet = actual.ToHashSet(StringComparer.Ordinal);
            var missing = declared.Except(actualSet, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var unexpected = actualSet.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var duplicates = actual.Length != actualSet.Count || declaration.StoreIdentities.Count != declared.Count;
            var complete = declaration.CallerDeclaredComplete && missing.Length == 0 && unexpected.Length == 0 && !duplicates;
            _domain = new ArtifactReferenceDomainStatus(complete, declaration.CallerDeclaredComplete, declaration.Basis,
                Array.AsReadOnly(missing), Array.AsReadOnly(unexpected));
            if (!complete)
            {
                foreach (var family in Enum.GetValues<ArtifactReferenceFamily>()) _complete[family] = false;
                foreach (var store in missing)
                    Incomplete(ArtifactReferenceFamily.Assessments, store, "declared-store-snapshot-missing", null);
                foreach (var store in unexpected)
                    Incomplete(ArtifactReferenceFamily.Assessments, store, "undeclared-store-snapshot-supplied", null);
                if (!declaration.CallerDeclaredComplete)
                    Incomplete(ArtifactReferenceFamily.Assessments, null, "reference-domain-not-declared-complete", null);
                if (duplicates) Incomplete(ArtifactReferenceFamily.Assessments, null, "duplicate-store-identity", null);
            }
        }

        internal IReadOnlyList<ArtifactReference> References => _references;
        internal IReadOnlyList<ArtifactBaselinePublication> BaselinePublications => _baselinePublications;
        internal IReadOnlyList<ArtifactStoreIdentity> Stores => _stores;

        internal void Reference(ArtifactReferenceFamily family, string storeIdentity, ArtifactReferenceTargetKind kind,
            string target, string recordId, string reason, string? scopeIdentity = null)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                Incomplete(family, storeIdentity, "blank-reference-value", recordId);
                return;
            }
            _references.Add(new ArtifactReference(family, storeIdentity, kind, target, recordId, reason, scopeIdentity));
        }

        internal void Retain(ArtifactReferenceFamily family, string storeIdentity, string recordId, string reason,
            bool familyScopedUncertainty = false) =>
            _retentionRecords.Add(new ArtifactRetentionRecord(family, storeIdentity, recordId, reason,
                familyScopedUncertainty));

        internal void Incomplete(ArtifactReferenceFamily family, string? storeIdentity, string code, string? recordId)
        {
            _complete[family] = false;
            _diagnostics.Add(new ArtifactReferenceDiagnostic(family, storeIdentity, code, recordId));
            if (storeIdentity is not null && recordId is not null)
                Retain(family, storeIdentity, recordId, code, familyScopedUncertainty: true);
        }

        internal void AddBaselinePublication(ArtifactBaselinePublication publication) =>
            _baselinePublications.Add(publication);

        internal void IncompleteDomain()
        {
            _domain = _domain with { IsComplete = false };
        }

        internal void AddInvocationProvenance(string storeIdentity, string invocationId, bool hasAssessment,
            string recordId) => _invocationProvenance.Add(new ArtifactInvocationProvenance(storeIdentity,
                invocationId, true, hasAssessment, true, recordId));

        internal ArtifactReferenceSnapshot Build()
        {
            var references = _references.Distinct().OrderBy(reference => reference.Family)
                .ThenBy(reference => reference.StoreIdentity, StringComparer.Ordinal)
                .ThenBy(reference => reference.RetainingRecordId, StringComparer.Ordinal)
                .ThenBy(reference => reference.Reason, StringComparer.Ordinal)
                .ThenBy(reference => reference.TargetKind).ThenBy(reference => reference.Target, StringComparer.Ordinal)
                .ThenBy(reference => reference.ScopeIdentity, StringComparer.Ordinal).ToArray();
            var retentionRecords = _retentionRecords.Distinct().OrderBy(record => record.Family)
                .ThenBy(record => record.StoreIdentity, StringComparer.Ordinal)
                .ThenBy(record => record.RecordId, StringComparer.Ordinal)
                .ThenBy(record => record.Reason, StringComparer.Ordinal).ToArray();
            var diagnostics = _diagnostics.Distinct().OrderBy(diagnostic => diagnostic.Family)
                .ThenBy(diagnostic => diagnostic.StoreIdentity, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.RecordId, StringComparer.Ordinal).ToArray();
            return new ArtifactReferenceSnapshot(_stores, _domain, references, retentionRecords,
                _baselinePublications.OrderBy(item => item.StoreIdentity, StringComparer.Ordinal)
                    .ThenBy(item => item.ProjectIdentity, StringComparer.Ordinal)
                    .ThenBy(item => item.BundleDigest, StringComparer.Ordinal).ToArray(),
                _invocationProvenance.OrderBy(item => item.StoreIdentity, StringComparer.Ordinal)
                    .ThenBy(item => item.InvocationId, StringComparer.Ordinal).ToArray(),
                _complete.Select(pair => new ArtifactReferenceFamilyStatus(pair.Key, pair.Value))
                    .OrderBy(item => item.Family).ToArray(), diagnostics);
        }
    }
}
