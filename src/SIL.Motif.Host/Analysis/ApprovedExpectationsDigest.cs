using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Analysis;

/// <summary>Identifies the approved morphology used to compute batch correctness.</summary>
public static class ApprovedExpectationsDigest
{
    /// <summary>Hashes words and approved readings, independent of their enumeration order.</summary>
    public static string Compute(IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> expectations)
    {
        ArgumentNullException.ThrowIfNull(expectations);
        var canonical = expectations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new
            {
                Word = pair.Key,
                Analyses = pair.Value.Select(value => JsonSerializer.Serialize(value)).Order(StringComparer.Ordinal).ToArray(),
            }).ToArray();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical));
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
