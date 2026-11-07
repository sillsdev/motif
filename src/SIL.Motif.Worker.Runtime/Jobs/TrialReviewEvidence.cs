using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>One attributed observation for a planned Trial case.</summary>
public sealed record TrialReviewObservation(string CaseKey, string Word, string Outcome, ParseWordEvidence? Morphology)
{
    /// <summary>Independent producer-attributed reasons that this case did not complete.</summary>
    public IReadOnlyList<string> StopReasons =>
        (Morphology?.TimedOut == true || Outcome == "timed-out" ? new[] { "time-limit" } : [])
        .Concat(Outcome == "capped" ? ["work-limit"] : [])
        .Concat(Outcome == "memory-capped" ? ["memory-limit"] : [])
        .Concat(Morphology?.Refusal is not null ? ["parser-refused"] : [])
        .Concat(Morphology?.InvalidShape == true ? ["invalid-evidence"] : []).ToArray();

    /// <summary>Distinguishes incomplete search and unavailable detail from a complete observation.</summary>
    public Completion Completion => Morphology is null || Outcome == "skipped" && !Morphology.InvalidShape ? Completion.Unavailable :
        StopReasons.Count != 0 || Morphology.Capped ? Completion.Stopped :
            Morphology.Unavailable.Count != 0 ? Completion.Unavailable : Completion.Complete;
}

/// <summary>A run-level interruption without a guessed per-word cause.</summary>
public sealed record TrialReviewInterruption(string Side, string Code, IReadOnlyList<string> CompletedCaseKeys,
    IReadOnlyList<string> StoppedCaseKeys, IReadOnlyList<string> PendingCaseKeys);

/// <summary>Retained before and after observations for the exact captured Trial scope.</summary>
public sealed record TrialReviewLedger(string JobId, ReviewInputReference Inputs,
    IReadOnlyList<TrialReviewObservation> Before, IReadOnlyList<TrialReviewObservation> After,
    IReadOnlyList<TrialReviewInterruption> Interruptions)
{
    /// <summary>Baseline diagnostics captured by the same parser invocation policy as the candidate.</summary>
    public IReadOnlyList<GrammarWarning>? BeforeWarnings { get; init; }
    /// <summary>Candidate diagnostics paired with the retained Baseline diagnostics.</summary>
    public IReadOnlyList<GrammarWarning>? AfterWarnings { get; init; }
    /// <summary>The saved candidate file's exact bytes used for diagnostic trace binding.</summary>
    public string? CandidateFileDigest { get; init; }
}

/// <summary>Persists a Trial's attributed evidence independently of its terminal status.</summary>
public static class TrialReviewEvidence
{
    /// <summary>Stores partial before rows by content before the Trial job is queued.</summary>
    public static string StoreBefore(MotifDatabase database, IReadOnlyList<TrialReviewObservation> observations)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(observations, MotifJson.CreateOptions());
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var directory = Path.GetFullPath(database.FullPath) + ".review-before";
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, digest + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Before evidence digest mismatch.");
        return digest;
    }

    /// <summary>Reads content-bound partial before rows.</summary>
    public static IReadOnlyList<TrialReviewObservation> ReadBefore(MotifDatabase database, string digest)
    {
        if (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Invalid before evidence reference.");
        var bytes = File.ReadAllBytes(Path.Combine(Path.GetFullPath(database.FullPath) + ".review-before", digest + ".json"));
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != digest)
            throw new InvalidDataException("Before evidence digest mismatch.");
        return JsonSerializer.Deserialize<TrialReviewObservation[]>(bytes, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("Before evidence is empty.");
    }
    /// <summary>Reads a Trial ledger, including before evidence when the worker never started.</summary>
    public static TrialReviewLedger? ReadJob(MotifDatabase database, string jobId)
    {
        var job = new JobRepository(database).Get(jobId) ?? throw new KeyNotFoundException("The Trial job is missing.");
        var input = TrialJobInput.Parse(job.InputJson);
        if (input.ReviewInputs is not { } reference) return null;
        var inputs = ReviewInputArtifacts.Read(database, reference).Inputs;
        var ledger = Open(database, jobId, reference);
        if (input.ReviewBeforePartial is { } partial)
            ledger = ledger with { Before = ReadBefore(database, partial) };
        var repository = new AssessmentRepository(database);
        foreach (var partition in input.ReviewBaselines ?? [])
        {
            var words = partition.AssessmentIds.SelectMany(id => repository.Get(id).Words ?? []);
            ledger = ledger with { Before = ledger.Before.Concat(Observe(inputs, partition.CaseKeys, words))
                .GroupBy(item => item.CaseKey, StringComparer.Ordinal).Select(group => group.Last()).ToArray() };
        }
        if (input.ReviewBeforeStopCode is { } stop && !ledger.Interruptions.Any(item => item.Side == "before" && item.Code == stop))
            ledger = Interrupt(ledger, inputs, "before", stop);
        return ledger;
    }

    /// <summary>Creates or reads the ledger while preserving an earlier attempt's evidence.</summary>
    public static TrialReviewLedger Open(MotifDatabase database, string jobId, ReviewInputReference inputs)
    {
        var path = PathFor(database, jobId);
        if (!File.Exists(path)) return new(jobId, inputs, [], [], []);
        var ledger = JsonSerializer.Deserialize<TrialReviewLedger>(File.ReadAllBytes(path), MotifJson.CreateOptions())
            ?? throw new InvalidDataException("The Trial review ledger is empty.");
        if (ledger.JobId != jobId || ledger.Inputs != inputs)
            throw new InvalidDataException("The Trial review ledger does not match its job.");
        return ledger;
    }

    /// <summary>Atomically publishes a complete ledger snapshot for a job.</summary>
    public static void Save(MotifDatabase database, TrialReviewLedger ledger)
    {
        var path = PathFor(database, ledger.JobId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(ledger, MotifJson.CreateOptions()));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void SaveForClaim(MotifDatabase database, TrialReviewLedger ledger, string? claimToken)
    {
        if (string.IsNullOrWhiteSpace(claimToken))
            throw new OperationCanceledException("The Trial has no active claim for review evidence publication.");
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Jobs WHERE JobId = $id AND ClaimToken IS $token;";
        command.Parameters.AddWithValue("$id", ledger.JobId);
        command.Parameters.AddWithValue("$token", (object?)claimToken ?? DBNull.Value);
        if (Convert.ToInt64(command.ExecuteScalar()) != 1)
            throw new OperationCanceledException("The Trial claim no longer owns review evidence publication.");
        Save(database, ledger);
        transaction.Commit();
    }

    /// <summary>Maps only producer-attributed word rows to the planned cases of one partition.</summary>
    public static IReadOnlyList<TrialReviewObservation> Observe(FrozenReviewInputs inputs,
        IReadOnlyCollection<string> caseKeys, IEnumerable<AssessedWord> words)
    {
        var selected = caseKeys.ToHashSet(StringComparer.Ordinal);
        var rows = words.GroupBy(word => word.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return inputs.Cases.Where(item => selected.Contains(item.CaseKey) && rows.ContainsKey(item.Word))
            .Select(item => new TrialReviewObservation(item.CaseKey, item.Word, rows[item.Word].Outcome,
                rows[item.Word].Morphology)).ToArray();
    }

    /// <summary>Records an interruption without assigning it to undispatched or unattributed cases.</summary>
    public static TrialReviewLedger Interrupt(TrialReviewLedger ledger, FrozenReviewInputs inputs, string side, string code)
    {
        var observations = side == "before" ? ledger.Before : ledger.After;
        var completed = observations.Where(item => item.Completion == Completion.Complete)
            .Select(item => item.CaseKey).ToHashSet(StringComparer.Ordinal);
        var stopped = observations.Where(item => item.Completion == Completion.Stopped)
            .Select(item => item.CaseKey).ToHashSet(StringComparer.Ordinal);
        var interruption = new TrialReviewInterruption(side, code, completed.Order(StringComparer.Ordinal).ToArray(),
            stopped.Order(StringComparer.Ordinal).ToArray(), inputs.Cases.Select(item => item.CaseKey)
                .Where(key => !completed.Contains(key) && !stopped.Contains(key)).Order(StringComparer.Ordinal).ToArray());
        return ledger with { Interruptions = [.. ledger.Interruptions, interruption] };
    }

    private static string PathFor(MotifDatabase database, string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Invalid job identity.", nameof(jobId));
        var key = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(jobId)));
        return Path.Combine(Path.GetFullPath(database.FullPath) + ".review-evidence", key + ".json");
    }
}
