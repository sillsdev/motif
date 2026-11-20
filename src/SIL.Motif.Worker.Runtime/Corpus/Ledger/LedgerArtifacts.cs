using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Worker.Corpus.Ledger;

/// <summary>The digests the Done reader and the artifact writers must agree on.</summary>
internal static class LedgerArtifactDigests
{
    public static string Selection(IEnumerable<string> occurrenceIds) =>
        Hex(string.Concat(occurrenceIds.Select(id => id + "\n")));

    public static string ProtectedSet(IEnumerable<(string Type, string Reading)> sortedPairs) =>
        Hex(string.Concat(sortedPairs.Select(pair => pair.Type + "\t" + pair.Reading + "\n")));

    public static IReadOnlyList<(string Type, string Reading)> SortPairs(IEnumerable<(string Type, string Reading)> pairs) =>
        pairs.OrderBy(pair => pair.Type, StringComparer.Ordinal).ThenBy(pair => pair.Reading, StringComparer.Ordinal).ToArray();

    private static string Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>The pins a frozen audit sample carries; the selection digest is computed, never supplied.</summary>
internal sealed record AuditManifest(
    string MembershipDigest, string FrequencySha256, string TokenizerSha256, string SamplingMethod, long Seed,
    string ScorerId, string ScoringPolicyVersion, string ReferenceRevision, string ReferenceDigest,
    IReadOnlyList<string> Sources);

internal sealed record AuditOutcome(string OccurrenceId, string Outcome);

/// <summary>Writes the audit sample files <see cref="DoneVerdict"/> reads, with the same bytes for the same inputs.</summary>
internal static class AuditSampleWriter
{
    /// <summary>Writes manifest.json, selection.jsonl and outcomes.jsonl into the directory and returns the selection digest.</summary>
    public static string Write(string directory, AuditManifest manifest, IReadOnlyList<string> selection,
        string grammarRevision, IReadOnlyList<AuditOutcome> outcomes)
    {
        if (selection.Count != selection.Distinct(StringComparer.Ordinal).Count() || selection.Any(string.IsNullOrEmpty))
            throw new ArgumentException("The selection must hold distinct, non-empty occurrence ids.", nameof(selection));
        if (manifest.Sources.Count == 0) throw new ArgumentException("The manifest needs at least one source.", nameof(manifest));
        var chosen = selection.ToHashSet(StringComparer.Ordinal);
        if (outcomes.Any(item => !chosen.Contains(item.OccurrenceId)))
            throw new ArgumentException("An outcome names an occurrence outside the selection.", nameof(outcomes));
        var digest = LedgerArtifactDigests.Selection(selection);
        Directory.CreateDirectory(directory);
        WriteAtomically(Path.Combine(directory, "selection.jsonl"),
            Lines(selection.Select(id => new Dictionary<string, object> { ["occurrenceId"] = id })));
        WriteAtomically(Path.Combine(directory, "outcomes.jsonl"), Lines(outcomes.Select(item => new Dictionary<string, object>
        {
            ["occurrenceId"] = item.OccurrenceId, ["selectionDigest"] = digest, ["referenceDigest"] = manifest.ReferenceDigest,
            ["grammarRevision"] = grammarRevision, ["outcome"] = item.Outcome,
        })));
        // The manifest goes last: a sample without one is absent, never half written.
        WriteAtomically(Path.Combine(directory, "manifest.json"), Lines([ManifestRow(manifest, digest)]));
        return digest;
    }

    internal static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Dictionary<string, object> ManifestRow(AuditManifest manifest, string digest) => new()
    {
        ["frequencySha256"] = manifest.FrequencySha256, ["membershipDigest"] = manifest.MembershipDigest,
        ["referenceDigest"] = manifest.ReferenceDigest, ["referenceRevision"] = manifest.ReferenceRevision,
        ["samplingMethod"] = manifest.SamplingMethod, ["scorerId"] = manifest.ScorerId,
        ["scoringPolicyVersion"] = manifest.ScoringPolicyVersion, ["seed"] = manifest.Seed, ["selectionDigest"] = digest,
        ["sources"] = manifest.Sources.ToArray(), ["tokenizerSha256"] = manifest.TokenizerSha256,
    };

    private static byte[] Lines(IEnumerable<Dictionary<string, object>> rows) =>
        Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => JsonSerializer.Serialize(row) + "\n")));
}

/// <summary>Writes the protected-set pairs file <see cref="DoneVerdict.Stable"/> reads, sorted and digested.</summary>
internal static class ProtectedSetWriter
{
    /// <summary>Writes the file and returns its digest. Types are normalized as the join does; duplicates are refused.</summary>
    public static string Write(string path, string grammarRevision, IEnumerable<(string Type, string Reading)> pairs)
    {
        var sorted = LedgerArtifactDigests.SortPairs(pairs.Select(pair => (LedgerJoiner.Key(pair.Type), pair.Reading)));
        if (sorted.Count == 0) throw new ArgumentException("A protected set needs at least one pair.", nameof(pairs));
        if (sorted.Distinct().Count() != sorted.Count) throw new ArgumentException("The protected set holds a duplicate pair.", nameof(pairs));
        var digest = LedgerArtifactDigests.ProtectedSet(sorted);
        var document = new Dictionary<string, object>
        {
            ["digest"] = digest, ["grammarRevision"] = grammarRevision,
            ["pairs"] = sorted.Select(pair => new Dictionary<string, object> { ["reading"] = pair.Reading, ["type"] = pair.Type }).ToArray(),
        };
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        AuditSampleWriter.WriteAtomically(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document) + "\n"));
        return digest;
    }
}
