using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Observed input and artifact bytes for one completed batch invocation.</summary>
public sealed record BatchInvocationEvidence(
    string InvocationId,
    string SourcePath,
    string SourceBytesSha256,
    string ExecutableBytesSha256,
    string WordsPath,
    string WordsSha256,
    string TsvPath,
    string TsvSha256,
    string StandardErrorPath,
    string StandardErrorSha256,
    int PerWordTimeoutMs,
    int PerWordStepLimit,
    int Threads,
    bool CollectStatistics)
{
    public string? AnalysesPath { get; init; }
    public string? AnalysesSha256 { get; init; }

    /// <summary>
    /// What the parser reported about the grammar it loaded, newline-joined, or null when it reported nothing.
    /// </summary>
    /// <remarks>
    /// A grammar the parser could only partly compile still parses: it silently drops what it could not read and
    /// returns an ordinary empty analysis for every word that needed it. The count of findings cannot distinguish
    /// that from a grammar that genuinely does not describe the word, so the reasons travel with the evidence.
    /// Joined rather than listed because a record's value equality is what lets one invocation be recorded once per
    /// kind and compared for agreement; a collection member would compare by reference and refuse the second write.
    /// </remarks>
    public string? GrammarWarnings { get; init; }

    /// <summary>The recorded grammar findings in order, empty when none were retained.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> GrammarWarningLines => string.IsNullOrEmpty(GrammarWarnings)
        ? []
        : GrammarWarnings.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Hashes a file's bytes, independently of parser semantic identities.</summary>
    public static string DigestFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
