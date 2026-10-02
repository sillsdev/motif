namespace SIL.Motif.Contract.Responses;

/// <summary>
/// An object address independent of its label. Authored GUIDs compare in canonical textual order; opaque keys
/// compare exactly. Grammar-local ordinals and event occurrences require the saved grammar or document scope.
/// Kind and identity domain are part of equality, so an occurrence never becomes an authored object.
/// </summary>
public sealed record ObjectIdentity
{
    private ObjectIdentity(string kind, string key, string domain, string? scope) =>
        (Kind, Key, Domain, Scope) = (kind, key, domain, scope);

    public string Kind { get; }
    public string Key { get; }
    public string Domain { get; }
    public string? Scope { get; }

    /// <summary>Creates an address, or null for a missing key/kind or an unscoped local identity.</summary>
    public static ObjectIdentity? Create(string? kind, string? key, string? quality = "authored", string? scope = null)
    {
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(key)) return null;
        if (quality is "grammar-local" or "occurrence")
            return string.IsNullOrEmpty(scope) ? null : new ObjectIdentity(kind, key, quality, scope);
        return quality == "authored" && Guid.TryParse(key, out var guid)
            ? new ObjectIdentity(kind, guid.ToString("D"), "authored", null)
            : new ObjectIdentity(kind, key, "opaque", null);
    }

    /// <summary>Compares complete addresses; two unresolved addresses do not identify an object.</summary>
    public static bool Same(ObjectIdentity? left, ObjectIdentity? right) => left is not null && left == right;

    /// <summary>Canonical authored text or the exact opaque key, without inventing a local scope.</summary>
    public static string? CanonicalKey(string? key, string? quality = "authored") =>
        quality == "authored" && Guid.TryParse(key, out var guid) ? guid.ToString("D") : key;
}
