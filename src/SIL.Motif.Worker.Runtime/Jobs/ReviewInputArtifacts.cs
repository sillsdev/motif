using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Config;

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
