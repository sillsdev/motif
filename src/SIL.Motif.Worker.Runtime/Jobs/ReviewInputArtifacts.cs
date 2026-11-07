using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>A content-addressed review input artifact beside a project's managed store.</summary>
public sealed record ReviewInputReference(string Sha256);

/// <summary>The frozen inputs and exact source bytes used to produce them.</summary>
public sealed record ReviewInputArtifact(FrozenReviewInputs Inputs, IReadOnlyDictionary<string, byte[]> Sources)
{
    /// <summary>The Baseline whose project and decisions were captured.</summary>
    public BaselineToken? Baseline { get; init; }
    /// <summary>The captured Assessment scope used for the candidate.</summary>
    public AssessmentScopeConfiguration? Scope { get; init; }
}

/// <summary>Stores and verifies a Trial's immutable review input artifact.</summary>
public static class ReviewInputArtifacts
{
    internal static WorkerMutexOwner Acquire(string databasePath)
    {
        var owner = NewOwner(databasePath);
        try
        {
            while (!owner.TryAcquire()) Thread.Sleep(50);
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    private static WorkerMutexOwner NewOwner(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) path = path.ToUpperInvariant();
        var key = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)));
        return WorkerMutexOwner.ForRoot("Global\\Motif.ReviewInputs." + key);
    }

    internal static void Sweep(MotifDatabase database)
    {
        using var lease = Acquire(database.FullPath);
        SweepUnderLease(database);
    }

    internal static void TrySweep(MotifDatabase database)
    {
        using var lease = NewOwner(database.FullPath);
        if (lease.TryAcquire()) SweepUnderLease(database);
    }

    internal static void SweepUnderLease(MotifDatabase database)
    {
        var directory = Path.GetFullPath(database.FullPath) + ".review-inputs";
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        var retained = new HashSet<string>(StringComparer.Ordinal);
        var protectedJobs = new HashSet<string>(StringComparer.Ordinal);
        var seenJobs = new HashSet<string>(StringComparer.Ordinal);
        using var connection = database.OpenConnection();
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='ReviewReports';";
            if (exists.ExecuteScalar() is not null)
            {
                using var reports = connection.CreateCommand();
                reports.CommandText = "SELECT TrialJobId, InputReferencesJson FROM ReviewReports " +
                    "WHERE DetailAvailable=1 OR InputReferencesJson != '[]';";
                using var reader = reports.ExecuteReader();
                while (reader.Read())
                {
                    protectedJobs.Add(reader.GetString(0));
                    foreach (var reference in JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? [])
                    {
                        if (reference is null) throw new InvalidDataException("A retained report input reference is invalid.");
                        retained.Add(reference.StartsWith("sha256:", StringComparison.Ordinal) ? reference[7..] : reference);
                    }
                }
            }
        }
        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var proposals = connection.CreateCommand())
        {
            proposals.CommandText = "SELECT ProposalId, Status FROM Proposals;";
            using var reader = proposals.ExecuteReader();
            while (reader.Read()) statuses[reader.GetString(0)] = reader.GetString(1);
        }
        using (var jobs = connection.CreateCommand())
        {
            jobs.CommandText = "SELECT JobId, Status, InputJson FROM Jobs WHERE Kind = $kind;";
            jobs.Parameters.AddWithValue("$kind", TrialJobHandler.TrialKind);
            using var reader = jobs.ExecuteReader();
            while (reader.Read())
            {
                seenJobs.Add(reader.GetString(0));
                var input = TrialJobInput.Parse(reader.GetString(2));
                if (input.ReviewInputs is not { } reference) continue;
                var proposalId = ProposalJsonParser.Parse(input.ProposalJson).ProposalId.Value;
                var active = !JobStateMachine.IsTerminal(JobStatusJson.Parse(reader.GetString(1)));
                if (active || protectedJobs.Contains(reader.GetString(0)) ||
                    statuses.TryGetValue(proposalId, out var status) && ProposalDifferenceRetention.KeepsWordDetail(status))
                    retained.Add(reference.Sha256);
            }
        }
        if (protectedJobs.Except(seenJobs, StringComparer.Ordinal).Any()) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var digest = Path.GetFileNameWithoutExtension(path);
            if (digest.Length == 64 && digest.All(Uri.IsHexDigit) && !retained.Contains(digest)) File.Delete(path);
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*.tmp"))
        {
            var parts = Path.GetFileName(path).Split('.');
            if (parts.Length == 3 && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit) &&
                Guid.TryParseExact(parts[1], "N", out _)) File.Delete(path);
        }
    }

    /// <summary>Atomically stores the captured inputs and returns their digest.</summary>
    public static ReviewInputReference Store(MotifDatabase database, ReviewInputArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(artifact);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, MotifJson.CreateOptions());
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var directory = Path.GetFullPath(database.FullPath) + ".review-inputs";
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, digest + ".json");
        if (!File.Exists(path))
        {
            var temporary = Path.Combine(directory, $"{digest}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                try { File.Move(temporary, path, overwrite: false); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            throw new InvalidOperationException("The stored review input artifact does not match its digest.");
        return new ReviewInputReference(digest);
    }

    /// <summary>Reads the artifact only if its bytes match the captured digest.</summary>
    public static ReviewInputArtifact Read(MotifDatabase database, ReviewInputReference reference)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Sha256.Length != 64 || reference.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("The review input reference is invalid.");
        var path = Path.Combine(Path.GetFullPath(database.FullPath) + ".review-inputs", reference.Sha256 + ".json");
        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), reference.Sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The review input artifact no longer matches its digest.");
        return JsonSerializer.Deserialize<ReviewInputArtifact>(bytes, MotifJson.CreateOptions())
            ?? throw new InvalidOperationException("The review input artifact is invalid.");
    }
}
