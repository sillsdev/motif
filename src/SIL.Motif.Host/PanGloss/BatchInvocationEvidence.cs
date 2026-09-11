using System.Security.Cryptography;

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

    /// <summary>Hashes a file's bytes, independently of parser semantic identities.</summary>
    public static string DigestFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
