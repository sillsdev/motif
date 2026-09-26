using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.Worker.Store;

/// <summary>Computes the revision identifier for the exact JSON bytes stored for a Draft.</summary>
public static class DraftRevision
{
    /// <summary>Returns the stable hash identifier, or <c>none</c> when no Draft content is stored.</summary>
    public static string Compute(string? json) => json is null ? "none" :
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
}
