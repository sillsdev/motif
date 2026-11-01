using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>
/// The inputs a Proposal's automatic Parsimony run depends on, as short digests. Two runs with equal inputs measure
/// the same candidate, so the record is both the idempotency key and what a status diffs to say what went stale.
/// </summary>
/// <param name="ProposalId">The Proposal measured.</param>
/// <param name="Baseline">The Baseline the Trial ran against.</param>
/// <param name="Proposal">The Proposal's intent and content.</param>
/// <param name="Prerequisites">The prerequisite revisions applied before it, in order.</param>
/// <param name="Parser">The PanGloss executable the Trial was pinned to.</param>
/// <param name="Schema">The facts and evidence file layouts.</param>
/// <param name="Measures">The measure catalog version and the measures the Grammar group runs.</param>
public sealed record ParsimonyAutoInputs(string ProposalId, string Baseline, string Proposal, string Prerequisites,
    string Parser, string Schema, string Measures)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The inputs a Trial's own input and Baseline imply.</summary>
    /// <param name="trialInputJson">The Trial job's input.</param>
    /// <param name="baseline">The Baseline the Trial measured.</param>
    public static ParsimonyAutoInputs For(string trialInputJson, BaselineToken baseline)
    {
        var trial = TrialJobInput.Parse(trialInputJson);
        var proposal = FrozenProposalRevision.Create(trial.ProposalJson);
        return new ParsimonyAutoInputs(proposal.ProposalId,
            Digest(EvidenceBundlePipeline.TokenJson(baseline)),
            Digest(proposal.IntentDigest + "|" + proposal.ContentSha256),
            Digest(string.Join(';', (trial.Prerequisites ?? []).Select(item => item.IntentDigest + "|" + item.ContentSha256))),
            JobParserPin.KeyOf(trialInputJson) ?? "none",
            $"{PanGlossInterfaceVersions.FactsSchemaVersion}/{EvidenceSchema.Version}",
            $"{MeasureCatalog.MeasureSetVersion}:{string.Join(',', ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar))}");
    }

    /// <summary>The key stored on the candidate job.</summary>
    public string ToKey() => JsonSerializer.Serialize(this, Web);

    /// <summary>Reads a stored key, or null when it is not one.</summary>
    /// <param name="key">The key a job carries.</param>
    public static ParsimonyAutoInputs? FromKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        try { return JsonSerializer.Deserialize<ParsimonyAutoInputs>(key, Web); }
        catch (JsonException) { return null; }
    }

    private static string Digest(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}

/// <summary>
/// Queues the advisory Grammar-group Parsimony comparison for a Proposal after its Trial commits. The run is a job of
/// its own: its failure, timeout or cancellation cannot reach the Trial, the Assessments or Apply.
/// </summary>
public static class ParsimonyAutoRun
{
    private const string TrialKind = "trial";
    private static readonly TimeSpan ReconcileWindow = TimeSpan.FromHours(24);
    private const int ReconcileLimit = 50;

    /// <summary>Queues the comparison for one committed Trial unless one for the same inputs already exists.</summary>
    /// <param name="database">The project's store.</param>
    /// <param name="trial">The committed Trial job.</param>
    /// <param name="baseline">The Baseline the Trial measured.</param>
    /// <param name="retryFailed">Whether an earlier failed or cancelled job for the same inputs is run again.</param>
    /// <returns>The new job's id, or null when none was needed.</returns>
    public static string? Enqueue(MotifDatabase database, JobRecord trial, BaselineToken baseline, bool retryFailed)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(baseline);
        var inputs = ParsimonyAutoInputs.For(trial.InputJson, baseline);
        var key = inputs.ToKey();
        var jobs = new JobRepository(database);
        if (Existing(database, trial.ProjectKey, key) is { } existing &&
            (!retryFailed || existing is not (JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted)))
            return null;
        var ids = ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar);
        var input = JsonSerializer.Serialize(new ParsimonyCandidateJobInput(
            ParsimonyCandidateJobInput.CurrentSchemaVersion, trial.JobId, ids[0],
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), MeasureIds: ids,
            InputsKey: key), SIL.Motif.Contract.MotifJson.CreateOptions());
        var created = jobs.Create(CanonicalId.Mint("job/").Value, trial.ProjectKey,
            ParsimonyCandidateEvidenceBuilder.JobKind, JobParserPin.Copy(trial.InputJson, input),
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return created.JobId;
    }

    /// <summary>
    /// Queues the comparison for recent committed Trials whose Baseline is still current and that have none, the
    /// newest Trial per Proposal. A runner that stopped between a Trial's commit and its enqueue catches up here.
    /// </summary>
    /// <param name="database">The project's store.</param>
    /// <param name="projectKey">The project's workspace key.</param>
    /// <param name="current">The project's current published Baseline token, or null.</param>
    /// <param name="now">The time the recent window is counted back from.</param>
    /// <returns>The ids of the jobs queued.</returns>
    public static IReadOnlyList<string> Reconcile(MotifDatabase database, string projectKey, BaselineToken? current,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (current is null) return [];
        var since = now.Subtract(ReconcileWindow).ToString("O", CultureInfo.InvariantCulture);
        var trialIds = new List<string>();
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT JobId FROM Jobs WHERE ProjectKey = $project AND Kind = $kind AND " +
                "Status = 'completed' AND UpdatedUtc >= $since " +
                "ORDER BY UpdatedUtc DESC, JobId LIMIT $limit;";
            command.Parameters.AddWithValue("$project", projectKey);
            command.Parameters.AddWithValue("$kind", TrialKind);
            command.Parameters.AddWithValue("$since", since);
            command.Parameters.AddWithValue("$limit", ReconcileLimit);
            using var reader = command.ExecuteReader();
            while (reader.Read()) trialIds.Add(reader.GetString(0));
        }
        var jobs = new JobRepository(database);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queued = new List<string>();
        foreach (var trialId in trialIds)
        {
            var trial = jobs.Get(trialId);
            if (trial?.ResultJson is null || !trial.DryRunPublished) continue;
            try
            {
                var measured = TrialBaseline(trial);
                if (measured is null || measured != current) continue;
                var proposalId = ParsimonyAutoInputs.For(trial.InputJson, measured).ProposalId;
                if (!seen.Add(proposalId)) continue;
                if (Enqueue(database, trial, measured, retryFailed: false) is { } queuedId) queued.Add(queuedId);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or
                                                  InvalidOperationException or ArgumentException)
            {
                // A Trial whose input cannot be read has nothing to compare; the others still get their run.
            }
        }
        return queued;
    }

    /// <summary>The Baseline a committed Trial's result names, or null when it names none.</summary>
    /// <param name="trial">The Trial job.</param>
    public static BaselineToken? TrialBaseline(JobRecord trial)
    {
        ArgumentNullException.ThrowIfNull(trial);
        if (trial.ResultJson is null) return null;
        using var document = JsonDocument.Parse(trial.ResultJson);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("baselineToken", out var token) &&
            token.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<BaselineToken>(token.GetRawText(), SIL.Motif.Contract.MotifJson.CreateOptions())
                : null;
    }

    // The status of the newest candidate job carrying the key, or null when none exists.
    private static JobStatus? Existing(MotifDatabase database, string projectKey, string key)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId FROM Jobs WHERE ProjectKey = $project AND Kind = $kind AND " +
            "json_extract(InputJson, '$.inputsKey') = $key " +
            "ORDER BY CreatedUtc DESC, JobId DESC LIMIT 1;";
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$kind", ParsimonyCandidateEvidenceBuilder.JobKind);
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is string id ? new JobRepository(database).Get(id)?.Status : null;
    }
}
