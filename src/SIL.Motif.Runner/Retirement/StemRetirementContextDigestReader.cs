using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Runner.Retirement;

/// <summary>Hashes ordered semantic evidence without loading or persisting a project.</summary>
public static class StemRetirementContextDigestReader
{
    /// <summary>Returns a stable digest for context facts and their exact unavailable interpretations.</summary>
    public static string ComputeDigest(IEnumerable<string> facts, IEnumerable<string> unavailable)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(unavailable);

        var orderedFacts = facts.Order(StringComparer.Ordinal).ToArray();
        var unavailableReasons = unavailable.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var payload = JsonSerializer.Serialize(new { version = 1, facts = orderedFacts, unavailable = unavailableReasons });
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
