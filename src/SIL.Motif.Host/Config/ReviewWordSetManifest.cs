using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Host.Config;

/// <summary>A source citation for one membership record.</summary>
public sealed record ReviewWordSetSource(string Citation, string? Locator);

/// <summary>A sampled position and its positive weight.</summary>
public sealed record ReviewTokenMembership(string OccurrenceId, string? TextId, string? ExternalSentenceLocator, int Position, int Weight);

/// <summary>One declared feature and value identity.</summary>
public sealed record ReviewCellFeature(string FeatureId, string ValueId);

/// <summary>A declared paradigm cell and one of its variants.</summary>
public sealed record ReviewCellMembership(string CellId, string LexemeId, IReadOnlyList<ReviewCellFeature> Features, string VariantId);

/// <summary>A validated membership with source references and its role-specific ledger.</summary>
public sealed record ReviewWordSetMember(string CaseId, string Word, string WritingSystemId, string? WordformId,
    ReviewWordSetSource Source, IReadOnlyList<string> ExpectationRefs, ReviewTokenMembership? Token,
    ReviewCellMembership? Cell);

/// <summary>The validated records and digest of the captured UTF-8 manifest bytes.</summary>
public sealed class ReviewWordSetManifest
{
    private readonly byte[] _sourceBytes;
    private readonly string _path;
    private readonly string? _role;

    internal ReviewWordSetManifest(string sourceDigest, byte[] sourceBytes, string path, string? role)
    {
        SourceDigest = sourceDigest;
        _sourceBytes = (byte[])sourceBytes.Clone();
        _path = path;
        _role = role;
    }

    /// <summary>SHA-256 of the exact source bytes.</summary>
    public string SourceDigest { get; }
    /// <summary>A copy of the captured source bytes.</summary>
    public byte[] SourceBytes => (byte[])_sourceBytes.Clone();
    /// <summary>The declared membership cases in source order.</summary>
    public IEnumerable<ReviewWordSetMember> Members => ReviewWordSetManifestReader.ReadMembers(_sourceBytes, _path, _role);

    /// <summary>Checks that the file still contains the bytes captured for this manifest.</summary>
    public void EnsureSourceUnchanged(string path)
    {
        if (!_sourceBytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)))
            throw new ProjectConfigurationException($"{path}: manifest changed during capture");
    }
}

/// <summary>Reads a review membership manifest without interpreting its expectations as judgments.</summary>
public static class ReviewWordSetManifestReader
{
    /// <summary>Reads a manifest and validates its schema and membership identities.</summary>
    public static ReviewWordSetManifest Read(string path) => Read(path, null);

    /// <summary>Also validates the role-specific token or cell ledger.</summary>
    public static ReviewWordSetManifest Read(string path, string? role) => Read(path, role, null);

    internal static ReviewWordSetManifest Read(string path, string? role, Action? afterRead)
    {
        var bytes = File.ReadAllBytes(path);
        afterRead?.Invoke();
        try { _ = new UTF8Encoding(false, true).GetCharCount(bytes); }
        catch (DecoderFallbackException e) { throw new ProjectConfigurationException($"{path}: invalid UTF-8: {e.Message}"); }
        var result = new ReviewWordSetManifest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes, path, role);
        foreach (var member in result.Members) { _ = member; }
        result.EnsureSourceUnchanged(path);
        return result;
    }

    internal static IEnumerable<ReviewWordSetMember> ReadMembers(byte[] bytes, string path, string? role)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var occurrences = new HashSet<string>(StringComparer.Ordinal);
        var variants = new HashSet<(string Cell, string Variant)>();
        var i = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) throw Error(path, i + 1, "empty record");
            ReviewWordSetMember? member = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw Error(path, i + 1, "record must be an object");
                if (i == 0)
                {
                    if (Required(root, "schema", path, i) != "motif.review-word-set" ||
                        !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var n) || n != 1)
                        throw Error(path, i + 1, "unsupported schema");
                    i++;
                    continue;
                }
                var id = Required(root, "caseId", path, i);
                if (!ids.Add(id)) throw Error(path, i + 1, $"duplicate caseId '{id}'");
                var word = Required(root, "word", path, i);
                var writingSystem = Required(root, "writingSystemId", path, i);
                var wordformId = root.TryGetProperty("wordformId", out _) ? Required(root, "wordformId", path, i) : null;
                if (!root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
                    throw Error(path, i + 1, "source must be an object");
                var citation = Required(source, "citation", path, i);
                var locator = source.TryGetProperty("locator", out _) ? Required(source, "locator", path, i) : null;
                if (!root.TryGetProperty("expectationRefs", out var refs) || refs.ValueKind != JsonValueKind.Array)
                    throw Error(path, i + 1, "expectationRefs must be an array");
                var expectationRefs = new List<string>();
                var referenceIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var reference in refs.EnumerateArray())
                {
                    if (reference.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reference.GetString()))
                        throw Error(path, i + 1, "expectationRefs entries must be nonblank identifiers");
                    var referenceId = reference.GetString()!;
                    if (!referenceIds.Add(referenceId)) throw Error(path, i + 1, $"duplicate expectation reference '{referenceId}'");
                    expectationRefs.Add(referenceId);
                }
                ReviewTokenMembership? token = null;
                ReviewCellMembership? cellMembership = null;
                var isToken = role == "sampled-tokens" || role is null &&
                    (root.TryGetProperty("occurrenceId", out _) || root.TryGetProperty("position", out _) ||
                     root.TryGetProperty("weight", out _) || root.TryGetProperty("textId", out _) ||
                     root.TryGetProperty("externalSentenceLocator", out _));
                var isCell = role == "paradigm-cells" || role is null &&
                    (root.TryGetProperty("cellId", out _) || root.TryGetProperty("lexemeId", out _) ||
                     root.TryGetProperty("variantId", out _) || root.TryGetProperty("features", out _));
                if (isToken && isCell) throw Error(path, i + 1, "a membership cannot be both a token and a cell");
                if (isToken)
                {
                    var occurrence = Required(root, "occurrenceId", path, i);
                    if (!occurrences.Add(occurrence)) throw Error(path, i + 1, $"duplicate occurrenceId '{occurrence}'");
                    if (!root.TryGetProperty("textId", out _) && !root.TryGetProperty("externalSentenceLocator", out _))
                        throw Error(path, i + 1, "token requires textId or externalSentenceLocator");
                    if (!root.TryGetProperty("position", out var position) || position.ValueKind != JsonValueKind.Number || !position.TryGetInt32(out var p) || p < 0 ||
                        !root.TryGetProperty("weight", out var weight) || weight.ValueKind != JsonValueKind.Number || !weight.TryGetInt32(out var w) || w <= 0)
                        throw Error(path, i + 1, "token position and positive weight are required");
                    var textId = root.TryGetProperty("textId", out _) ? Required(root, "textId", path, i) : null;
                    var external = root.TryGetProperty("externalSentenceLocator", out _) ? Required(root, "externalSentenceLocator", path, i) : null;
                    token = new ReviewTokenMembership(occurrence, textId, external, p, w);
                }
                if (isCell)
                {
                    var cell = Required(root, "cellId", path, i);
                    var lexemeId = Required(root, "lexemeId", path, i);
                    var variant = Required(root, "variantId", path, i);
                    if (!variants.Add((cell, variant))) throw Error(path, i + 1, $"duplicate variantId '{variant}' in cell '{cell}'");
                    if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                        throw Error(path, i + 1, "cell features must be an array");
                    var featureIds = new HashSet<string>(StringComparer.Ordinal);
                    var featureValues = new List<ReviewCellFeature>();
                    foreach (var feature in features.EnumerateArray())
                    {
                        if (feature.ValueKind != JsonValueKind.Object) throw Error(path, i + 1, "feature must be an object");
                        var featureId = Required(feature, "featureId", path, i);
                        var valueId = Required(feature, "valueId", path, i);
                        if (!featureIds.Add(featureId)) throw Error(path, i + 1, $"duplicate featureId '{featureId}'");
                        featureValues.Add(new ReviewCellFeature(featureId, valueId));
                    }
                    cellMembership = new ReviewCellMembership(cell, lexemeId, featureValues, variant);
                }
                member = new ReviewWordSetMember(id, word, writingSystem, wordformId,
                    new ReviewWordSetSource(citation, locator), expectationRefs,
                    token, cellMembership);
            }
            catch (JsonException e) { throw Error(path, i + 1, e.Message); }
            i++;
            yield return member!;
        }
        if (i == 0) throw Error(path, 1, "missing schema");
    }

    private static string Required(JsonElement root, string key, string path, int index)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString())) throw Error(path, index + 1, $"'{key}' must be nonblank");
        return value.GetString()!;
    }

    private static ProjectConfigurationException Error(string path, int line, string reason) =>
        new($"{path}, line {line}: {reason}.");
}
