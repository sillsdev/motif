using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// Atomically records and validates the immutable aggregate for one retained invocation, including its
/// member Assessments, and queries complete retained aggregates by project or invocation identity.
/// </summary>
public sealed class RetainedInvocationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    private readonly MotifDatabase _database;

    /// <summary>Creates a retained-result repository over an already worker-owned database.</summary>
    public RetainedInvocationRepository(MotifDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>
    /// Records the aggregate and all member Assessment rows in one transaction. The aggregate's member set
    /// must name exactly one supplied Assessment per kind, and every supplied Assessment must agree with it.
    /// </summary>
    public void Record(RetainedInvocationRecord retained, IReadOnlyList<NewAssessmentRecord> assessments)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(assessments);
        ValidateAggregate(retained, assessments);

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        AssessmentRepository.InsertRecords(connection, transaction, assessments);
        InsertAggregate(connection, transaction, retained);
        InsertMembers(connection, transaction, retained);
        transaction.Commit();
    }

    /// <summary>Gets and validates one retained invocation, including all member Assessment detail.</summary>
    /// <exception cref="KeyNotFoundException">No retained invocation has the requested identity.</exception>
    public RetainedInvocationRecord Get(string invocationId)
    {
        if (string.IsNullOrWhiteSpace(invocationId))
            throw new ArgumentException("An invocation id is required.", nameof(invocationId));

        return ReadComplete(invocationId, includeWords: true);
    }

    /// <summary>Lists validated retained invocations for one project, oldest saved result first.</summary>
    public IReadOnlyList<RetainedInvocationRecord> List(string projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            throw new ArgumentException("A project workspace key is required.", nameof(projectKey));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT InvocationId FROM RetainedInvocations " +
            "WHERE ProjectKey = $project ORDER BY SavedUtc, InvocationId;";
        command.Parameters.AddWithValue("$project", projectKey);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        reader.Dispose();
        return ids.Select(id => ReadComplete(id, includeWords: false)).ToArray();
    }

    private RetainedInvocationRecord ReadComplete(string invocationId, bool includeWords)
    {
        using var connection = _database.OpenConnection();
        var stored = ReadAggregate(connection, invocationId) ??
            throw new KeyNotFoundException($"Retained invocation '{invocationId}' was not found.");
        var members = ReadMembers(connection, invocationId);
        var repository = new AssessmentRepository(_database);
        var assessments = new List<AssessmentRecord>(members.Count);
        foreach (var member in members)
        {
            var assessment = includeWords
                ? repository.Get(member.AssessmentId)
                : repository.GetHeader(member.AssessmentId);
            if (assessment is null)
                throw new InvalidDataException("A retained invocation is missing an expected Assessment member.");
            assessments.Add(assessment);
        }
        var complete = stored.Record with { Members = members, Assessments = assessments };
        ValidateStoredAggregate(complete, ReadExpectedKinds(stored.ExpectedKindsJson));
        return complete;
    }

    private void ValidateAggregate(RetainedInvocationRecord retained, IReadOnlyList<NewAssessmentRecord> assessments)
    {
        ValidateIdentity(retained, "is missing required identity or provenance");
        ArgumentNullException.ThrowIfNull(retained.BaselineToken);
        ArgumentNullException.ThrowIfNull(retained.Selection);
        ArgumentNullException.ThrowIfNull(retained.Members);
        ValidateSelectionDescriptor(retained.Selection, retained.BaselineToken.ProjectIdentity);
        if (retained.ArtifactInvocationId != retained.InvocationId)
            throw new InvalidDataException("A retained invocation's artifact reference must name its invocation.");
        if (retained.Members.Count == 0 || retained.Members.Select(member => member.Kind).Distinct().Count() !=
            retained.Members.Count || retained.Members.Select(member => member.AssessmentId).Distinct().Count() !=
            retained.Members.Count)
            throw new InvalidDataException("A retained invocation must have unique members by kind and Assessment id.");
        if (assessments.Count != retained.Members.Count)
            throw new InvalidDataException("A retained invocation is missing an expected Assessment member.");
        if (assessments.Select(assessment => assessment.AssessmentId).Distinct().Count() != assessments.Count)
            throw new InvalidDataException("A retained invocation supplied duplicate Assessment ids.");

        AssessmentRepository.ValidateRecords(assessments);
        if (assessments.Any(assessment => assessment.Invocation is null) ||
            assessments.Select(assessment => assessment.Invocation).Distinct().Count() != 1)
            throw new InvalidDataException("A retained invocation requires one shared invocation evidence record.");
        var byId = assessments.ToDictionary(assessment => assessment.AssessmentId, StringComparer.Ordinal);
        var baselineJson = JsonSerializer.Serialize(retained.BaselineToken, MotifJson.CreateOptions());
        foreach (var member in retained.Members)
        {
            if (!byId.TryGetValue(member.AssessmentId, out var assessment) ||
                !StringComparer.Ordinal.Equals(member.Kind, assessment.Kind))
                throw new InvalidDataException("A retained invocation member does not identify the supplied Assessment.");
            ValidateMember(retained, baselineJson, assessment);
        }
        ValidateRetrySource(retained.Selection, baselineJson);
    }

    private void ValidateStoredAggregate(
        RetainedInvocationRecord retained, IReadOnlyList<string> expectedKinds)
    {
        ValidateIdentity(retained, "has invalid identity or provenance");
        if (retained.ArtifactInvocationId != retained.InvocationId)
            throw new InvalidDataException("A retained invocation has invalid identity or provenance.");
        if (!expectedKinds.SequenceEqual(
                retained.Members.Select(member => member.Kind), StringComparer.Ordinal) ||
            retained.Members.Select(member => member.AssessmentId).Distinct().Count() != retained.Members.Count ||
            retained.Assessments.Count != retained.Members.Count ||
            !retained.Members.Select(member => member.AssessmentId).SequenceEqual(
                retained.Assessments.Select(assessment => assessment.AssessmentId), StringComparer.Ordinal))
            throw new InvalidDataException("A retained invocation is missing or has an unexpected Assessment member.");
        ValidateSelectionDescriptor(retained.Selection, retained.BaselineToken.ProjectIdentity);
        var baselineJson = JsonSerializer.Serialize(retained.BaselineToken, MotifJson.CreateOptions());
        BatchInvocationEvidence? sharedInvocation = null;
        foreach (var (member, assessment) in retained.Members.Zip(retained.Assessments))
        {
            if (!StringComparer.Ordinal.Equals(member.Kind, assessment.Kind))
                throw new InvalidDataException("A retained invocation member has the wrong Assessment kind.");
            ValidateMember(retained, baselineJson, assessment);
            if (sharedInvocation is null) sharedInvocation = assessment.Invocation;
            else if (sharedInvocation != assessment.Invocation)
                throw new InvalidDataException("A retained invocation's members disagree about shared evidence.");
        }
        ValidateRetrySource(retained.Selection, baselineJson);
    }

    private static void ValidateIdentity(RetainedInvocationRecord retained, string failure)
    {
        var fields = new (string Name, string? Value)[]
        {
            (nameof(RetainedInvocationRecord.InvocationId), retained.InvocationId),
            (nameof(RetainedInvocationRecord.ProjectKey), retained.ProjectKey),
            (nameof(RetainedInvocationRecord.BaselineRootDirectory), retained.BaselineRootDirectory),
            (nameof(RetainedInvocationRecord.BaselineFwDataPath), retained.BaselineFwDataPath),
            (nameof(RetainedInvocationRecord.Assessor), retained.Assessor),
            (nameof(RetainedInvocationRecord.ScopeJson), retained.ScopeJson),
            (nameof(RetainedInvocationRecord.ScopeDigest), retained.ScopeDigest),
            (nameof(RetainedInvocationRecord.ArtifactInvocationId), retained.ArtifactInvocationId),
        };
        foreach (var (name, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"A retained invocation field '{name}' {failure}.");
        }
    }

    private static void ValidateMember(
        RetainedInvocationRecord retained, string baselineJson, NewAssessmentRecord assessment)
        => ValidateMember(retained, baselineJson, assessment.Invocation, assessment.BaselineToken,
            assessment.Assessor, assessment.ScopeJson, assessment.ScopeDigest, assessment.GrammarSourceSha256,
            assessment.Selection);

    private static void ValidateMember(
        RetainedInvocationRecord retained, string baselineJson, AssessmentRecord assessment)
        => ValidateMember(retained, baselineJson, assessment.Invocation, assessment.BaselineToken,
            assessment.Assessor, assessment.ScopeJson, assessment.ScopeDigest, assessment.GrammarSourceSha256,
            assessment.Selection);

    private static void ValidateMember(
        RetainedInvocationRecord retained, string baselineJson, BatchInvocationEvidence? invocation,
        string assessmentBaseline, string assessor, string scopeJson, string scopeDigest,
        string grammarSourceSha256, Selection selection)
    {
        if (invocation is null || invocation.InvocationId != retained.InvocationId ||
            !StringComparer.Ordinal.Equals(assessmentBaseline, baselineJson) ||
            !StringComparer.Ordinal.Equals(assessor, retained.Assessor) ||
            !StringComparer.Ordinal.Equals(scopeJson, retained.ScopeJson) ||
            !StringComparer.Ordinal.Equals(scopeDigest, retained.ScopeDigest) ||
            !StringComparer.Ordinal.Equals(grammarSourceSha256, invocation.SourceBytesSha256) ||
            !selection.Words.SequenceEqual(retained.Selection.ResolvedWords, StringComparer.Ordinal) ||
            !StringComparer.Ordinal.Equals(selection.Sha256, retained.Selection.ResolvedSha256))
            throw new InvalidDataException("Retained invocation members disagree about invocation provenance.");
    }

    private static void ValidateSelectionDescriptor(SelectionDescriptor selection, string selectionName)
    {
        if (selection.TextIds is null || selection.PastedWords is null || selection.ResolvedWords is null ||
            selection.SourceCounts is null || string.IsNullOrWhiteSpace(selection.ResolvedSha256) ||
            string.IsNullOrWhiteSpace(selection.DescriptorSha256))
            throw new InvalidDataException("A retained Selection descriptor is incomplete.");
        var canonicalTextIds = selection.TextIds
            .Distinct()
            .OrderBy(textId => textId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (!selection.TextIds.SequenceEqual(canonicalTextIds))
            throw new InvalidDataException("A retained Selection descriptor has non-canonical Text identities.");
        if (selection.PastedWords.Any(word => string.IsNullOrWhiteSpace(word) ||
            word != word.Trim() || word.Normalize(NormalizationForm.FormD) != word))
            throw new InvalidDataException("A retained Selection descriptor has non-canonical pasted words.");
        if (selection.ResolvedWords.Any(word => string.IsNullOrWhiteSpace(word) ||
            word != word.Trim() || word.Normalize(NormalizationForm.FormD) != word))
            throw new InvalidDataException("A retained Selection descriptor has non-canonical resolved words.");
        var resolved = Selection.Create(selectionName, selection.ResolvedWords);
        if (!selection.ResolvedWords.SequenceEqual(resolved.Words, StringComparer.Ordinal) ||
            !StringComparer.Ordinal.Equals(selection.ResolvedSha256, resolved.Sha256))
            throw new InvalidDataException("A retained Selection descriptor has invalid resolved words or hash.");
        if (!StringComparer.Ordinal.Equals(selection.DescriptorSha256, SelectionDescriptorDigest.Compute(selection)))
            throw new InvalidDataException("A retained Selection descriptor digest is invalid.");
    }

    private void ValidateRetrySource(SelectionDescriptor selection, string baselineJson)
    {
        if (selection.RetryFailed || selection.RetrySlowerThan is not null)
        {
            if (string.IsNullOrWhiteSpace(selection.RetrySourceAssessmentId))
                throw new InvalidDataException("A retry Selection must name its source Assessment.");
            ValidateRetrySourceIfPresent(selection.RetrySourceAssessmentId, baselineJson,
                new AssessmentRepository(_database));
        }
        else if (selection.RetrySourceAssessmentId is not null)
        {
            throw new InvalidDataException("A Selection without retry cannot name a retry source Assessment.");
        }
    }

    private static void ValidateRetrySourceIfPresent(
        string? assessmentId, string baselineJson, AssessmentRepository assessments)
    {
        if (assessmentId is null) return;
        AssessmentRecord source;
        try { source = assessments.GetHeader(assessmentId) ?? throw new KeyNotFoundException(); }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException("A retained invocation names a missing retry source Assessment.", exception);
        }
        if (source.ProposalId is not null || source.Kind != AssessmentKind.ParseTime.ToStoredKind() ||
            !StringComparer.Ordinal.Equals(source.BaselineToken, baselineJson))
            throw new InvalidDataException("A retained invocation names a retry source from another Baseline.");
    }

    private static void InsertAggregate(
        SqliteConnection connection, SqliteTransaction transaction, RetainedInvocationRecord retained)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO RetainedInvocations
                (InvocationId, ProjectKey, BaselineToken, BaselineRootDirectory, BaselineFwDataPath,
                 BaselineSourceLastWriteUtc, BaselinePublishedUtc, SavedUtc, SelectionDescriptorJson,
                 SelectionDescriptorSha256, ExpectedKindsJson, Assessor, ScopeJson, ScopeDigest, ArtifactInvocationId)
            VALUES
                ($invocation, $project, $baseline, $root, $fwdata, $sourceSaved, $published, $saved,
                 $selection, $selectionSha, $expectedKinds, $assessor, $scope, $scopeDigest, $artifact);
            """;
        command.Parameters.AddWithValue("$invocation", retained.InvocationId);
        command.Parameters.AddWithValue("$project", retained.ProjectKey);
        command.Parameters.AddWithValue("$baseline",
            JsonSerializer.Serialize(retained.BaselineToken, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$root", retained.BaselineRootDirectory);
        command.Parameters.AddWithValue("$fwdata", retained.BaselineFwDataPath);
        command.Parameters.AddWithValue("$sourceSaved", Utc(retained.BaselineSourceLastWriteUtc));
        command.Parameters.AddWithValue("$published", Utc(retained.BaselinePublishedUtc));
        command.Parameters.AddWithValue("$saved", Utc(retained.SavedUtc));
        command.Parameters.AddWithValue("$selection", JsonSerializer.Serialize(retained.Selection, JsonOptions));
        command.Parameters.AddWithValue("$selectionSha", retained.Selection.DescriptorSha256);
        command.Parameters.AddWithValue("$expectedKinds",
            JsonSerializer.Serialize(retained.Members.Select(member => member.Kind)
                .OrderBy(kind => kind, StringComparer.Ordinal), JsonOptions));
        command.Parameters.AddWithValue("$assessor", retained.Assessor);
        command.Parameters.AddWithValue("$scope", retained.ScopeJson);
        command.Parameters.AddWithValue("$scopeDigest", retained.ScopeDigest);
        command.Parameters.AddWithValue("$artifact", retained.ArtifactInvocationId);
        command.ExecuteNonQuery();
    }

    private static void InsertMembers(
        SqliteConnection connection, SqliteTransaction transaction, RetainedInvocationRecord retained)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO RetainedInvocationMembers (InvocationId, Kind, AssessmentId) " +
            "VALUES ($invocation, $kind, $assessment);";
        var invocation = command.Parameters.Add("$invocation", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        var assessment = command.Parameters.Add("$assessment", SqliteType.Text);
        foreach (var member in retained.Members)
        {
            invocation.Value = retained.InvocationId;
            kind.Value = member.Kind;
            assessment.Value = member.AssessmentId;
            command.ExecuteNonQuery();
        }
    }

    private static StoredAggregate? ReadAggregate(SqliteConnection connection, string invocationId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT InvocationId, ProjectKey, BaselineToken, BaselineRootDirectory, " +
            "BaselineFwDataPath, BaselineSourceLastWriteUtc, BaselinePublishedUtc, SavedUtc, " +
            "SelectionDescriptorJson, SelectionDescriptorSha256, ExpectedKindsJson, Assessor, ScopeJson, " +
            "ScopeDigest, ArtifactInvocationId " +
            "FROM RetainedInvocations WHERE InvocationId = $invocation;";
        command.Parameters.AddWithValue("$invocation", invocationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        try
        {
            var baselineJson = reader.GetString(2);
            var baseline = JsonSerializer.Deserialize<BaselineToken>(baselineJson, MotifJson.CreateOptions())
                ?? throw new JsonException("Missing Baseline token.");
            var selection = JsonSerializer.Deserialize<SelectionDescriptor>(reader.GetString(8), JsonOptions)
                ?? throw new JsonException("Missing Selection descriptor.");
            var selectionSha = reader.GetString(9);
            if (!StringComparer.Ordinal.Equals(selection.DescriptorSha256, selectionSha))
                throw new InvalidDataException("The retained Selection descriptor digest does not match its stored digest.");
            var record = new RetainedInvocationRecord(
                reader.GetString(0), reader.GetString(1), baseline, reader.GetString(3), reader.GetString(4),
                ParseUtc(reader.GetString(5)), ParseUtc(reader.GetString(6)), ParseUtc(reader.GetString(7)), selection,
                reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14), []);
            return new StoredAggregate(record, reader.GetString(10));
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or
            InvalidCastException or InvalidOperationException)
        {
            throw new InvalidDataException("The retained invocation row is malformed.", exception);
        }
    }

    private static IReadOnlyList<string> ReadExpectedKinds(string json)
    {
        try
        {
            var kinds = JsonSerializer.Deserialize<string[]>(json, JsonOptions)
                ?? throw new JsonException("Missing expected Assessment kinds.");
            if (kinds.Length == 0 || kinds.Distinct(StringComparer.Ordinal).Count() != kinds.Length)
                throw new InvalidDataException("A retained invocation has an invalid expected Assessment set.");
            return kinds.OrderBy(kind => kind, StringComparer.Ordinal).ToArray();
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The retained invocation expected Assessment set is malformed.", exception);
        }
    }

    private static IReadOnlyList<RetainedInvocationMember> ReadMembers(
        SqliteConnection connection, string invocationId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Kind, AssessmentId FROM RetainedInvocationMembers " +
            "WHERE InvocationId = $invocation ORDER BY Kind;";
        command.Parameters.AddWithValue("$invocation", invocationId);
        using var reader = command.ExecuteReader();
        var members = new List<RetainedInvocationMember>();
        while (reader.Read()) members.Add(new RetainedInvocationMember(reader.GetString(0), reader.GetString(1)));
        return members;
    }

    private static string Utc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("A UTC time is required.");
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        var result = DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        if (result.Offset != TimeSpan.Zero) throw new InvalidDataException("A retained invocation time is not UTC.");
        return result;
    }

    private sealed record StoredAggregate(RetainedInvocationRecord Record, string ExpectedKindsJson);
}
