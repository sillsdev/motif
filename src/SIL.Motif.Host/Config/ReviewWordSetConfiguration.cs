using System.Text.RegularExpressions;

namespace SIL.Motif.Host.Config;

/// <summary>A named project word set and its single source for Proposal review.</summary>
public sealed record ReviewWordSetConfiguration
{
    public ReviewWordSetConfiguration(string id, string name, string role, string? selection, string? manifest)
    {
        if (id is null || !Regex.IsMatch(id, "\\A[a-z][a-z0-9-]{0,63}\\z"))
            throw new ArgumentException("id must be a lowercase ASCII identifier", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name must not be blank", nameof(name));
        if (role is not ("sampled-tokens" or "paradigm-cells" or "reference" or "regression" or "negative"))
            throw new ArgumentException("unknown review word set role", nameof(role));
        if ((selection is null) == (manifest is null) ||
            (selection is not null && string.IsNullOrWhiteSpace(selection)) ||
            (manifest is not null && string.IsNullOrWhiteSpace(manifest)))
            throw new ArgumentException("exactly one nonblank selection or manifest is required");
        if (role is "sampled-tokens" or "paradigm-cells" && manifest is null)
            throw new ArgumentException("sampled tokens and paradigm cells require a manifest");
        Id = id;
        Name = name;
        Role = role;
        Selection = selection;
        Manifest = manifest;
    }

    /// <summary>Stable identity within the project.</summary>
    public string Id { get; }
    /// <summary>Human-readable name.</summary>
    public string Name { get; }
    /// <summary>The question this set contributes to review.</summary>
    public string Role { get; }
    /// <summary>Named Selection supplying membership, when present.</summary>
    public string? Selection { get; }
    /// <summary>Path to the membership manifest, when present.</summary>
    public string? Manifest { get; }
    /// <summary>Resolves the manifest relative to the project configuration.</summary>
    public string? ResolveManifestPath(string configurationPath) => Manifest is null ? null :
        Path.GetFullPath(Manifest, Path.GetDirectoryName(Path.GetFullPath(configurationPath))!);
}
