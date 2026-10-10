using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Help;

/// <summary>A stable key and full citation for a source used by a Parsimony recipe.</summary>
/// <param name="Key">The closed citation key used by the measure catalog.</param>
/// <param name="Citation">The complete author, year, title, venue, and DOI or stable URL citation.</param>
/// <param name="Url">The stable source URL exposed to Help and MCP clients.</param>
public sealed record ParsimonyReference(string Key, string Citation, string Url);

/// <summary>Loads the closed bibliography used by Parsimony recipe pages and MCP resources.</summary>
public sealed class ParsimonyReferenceCatalog
{
    private const string ResourceName = "help/en/guide/parsimony/references.json";
    private readonly IReadOnlyDictionary<string, ParsimonyReference> _byKey;

    private ParsimonyReferenceCatalog(IReadOnlyList<ParsimonyReference> references)
    {
        References = references;
        _byKey = references.ToDictionary(reference => reference.Key, StringComparer.Ordinal);
    }

    /// <summary>All verified references in stable key order.</summary>
    public IReadOnlyList<ParsimonyReference> References { get; }

    /// <summary>Loads and validates the embedded reference resource.</summary>
    public static ParsimonyReferenceCatalog Load()
    {
        using var stream = ManifestResourceLookup.GetStream(
                typeof(ParsimonyReferenceCatalog).Assembly, ResourceName)
            ?? throw new InvalidDataException($"Missing embedded Parsimony reference resource '{ResourceName}'.");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var manifest = JsonSerializer.Deserialize<ReferenceManifest>(stream, options)
            ?? throw new InvalidDataException("The embedded Parsimony reference resource is empty.");
        if (manifest.FormatVersion != 1)
            throw new InvalidDataException($"Unsupported Parsimony reference format version {manifest.FormatVersion}.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in manifest.References)
        {
            if (string.IsNullOrWhiteSpace(reference.Key) || !IsKey(reference.Key) || !keys.Add(reference.Key))
                throw new InvalidDataException($"The Parsimony reference key '{reference.Key}' is invalid or duplicated.");
            if (string.IsNullOrWhiteSpace(reference.Citation))
                throw new InvalidDataException($"Parsimony reference '{reference.Key}' has no full citation.");
            if (!Uri.TryCreate(reference.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"Parsimony reference '{reference.Key}' needs a stable HTTPS URL.");
        }

        if (manifest.References.Count == 0)
            throw new InvalidDataException("The Parsimony reference resource must contain at least one citation.");
        return new ParsimonyReferenceCatalog(Array.AsReadOnly(
            manifest.References.OrderBy(reference => reference.Key, StringComparer.Ordinal).ToArray()));
    }

    /// <summary>Finds a reference by its exact stable citation key.</summary>
    /// <param name="key">The exact key recorded by a measure.</param>
    public ParsimonyReference? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>Renders the same complete citation records for the MCP guide resource.</summary>
    public string ToMarkdown()
    {
        var text = new StringBuilder("# Parsimony references\n\n");
        foreach (var reference in References)
            text.Append("- **").Append(reference.Key).Append("** — ").Append(reference.Citation)
                .Append(" [Source](").Append(reference.Url).AppendLine(").");
        return text.ToString();
    }

    private static bool IsKey(string key) => key.All(character =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private sealed class ReferenceManifest
    {
        public required int FormatVersion { get; init; }
        public required IReadOnlyList<ParsimonyReference> References { get; init; }
    }
}
