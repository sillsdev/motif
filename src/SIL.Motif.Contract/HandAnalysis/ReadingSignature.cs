using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.HandAnalysis;

/// <summary>Canonical identity of a Reading, independent of display spelling and project location.</summary>
/// <remarks>
/// The ordered morphemes are the complete identity payload. The summary comes from the only stem, or from the
/// only morpheme when there is no stem. Without that unique choice the summary has null IDs, unresolved sense
/// status and empty sets. The closed wire object has one member, <c>morphemes</c>. Its RFC 8785 canonical JSON
/// is the entire SHA-256 input. The derived summary and digest are available as properties in memory.
/// Use the prefix-free canonical encoding of each nonempty FieldWorks GUID. Morpheme and feature-path order
/// are significant. Sense candidates and values at the same feature role and path are ordinal-sorted sets.
/// Feature rows sort by declared role order, then ordinal path. Repeated rows at the same role and path merge.
/// </remarks>
[JsonConverter(typeof(ReadingSignature.WireConverter))]
public sealed record ReadingSignature
{
    private static readonly JsonSerializerOptions IdentityJson = MotifJson.CreateHandAnalysisReadOptions();
    private readonly ReadingMorpheme? head;
    private readonly byte[] canonicalBytes;

    /// <summary>Validates and normalizes the immutable morpheme facts before deriving the summary and digest.</summary>
    public ReadingSignature(ImmutableArray<ReadingMorpheme> morphemes)
    {
        RequireCollection(morphemes, nameof(morphemes));
        Morphemes = [.. morphemes.Select(NormalizeMorpheme)];
        var stems = Morphemes.Where(morpheme => morpheme.IsStem).Take(2).ToArray();
        head = stems.Length == 1 ? stems[0] : Morphemes.Length == 1 ? Morphemes[0] : null;
        canonicalBytes = CanonicalJson.CanonicalizeToUtf8(JsonSerializer.Serialize(new WireShape(Morphemes), IdentityJson));
        Id = IntentDigest.Sha256Of(canonicalBytes);
    }

    public ImmutableArray<ReadingMorpheme> Morphemes { get; }

    public string? EntryId => head?.EntryId;
    public string? SenseId => head?.SenseId;
    public ImmutableArray<string> SenseCandidates => head?.SenseCandidates ?? [];
    public ReadingSenseStatus SenseStatus => head?.SenseStatus ?? ReadingSenseStatus.Unresolved;
    public string? MsaId => head?.MsaId;
    public ImmutableArray<ReadingFeatureValue> FeatureValues => head?.FeatureValues ?? [];

    /// <summary>SHA-256 of the RFC 8785 JSON object containing the ordered morphemes.</summary>
    public string Id { get; }

    /// <summary>The same Reading with the sense dimension dropped from every morpheme, head and non-head alike.</summary>
    /// <remarks>
    /// Each morpheme keeps its entry, MSA, allomorph, inflection type, stem flag, derivation and features, and becomes
    /// sense-unresolved with no candidates. A parser cannot choose a sense, so this is the identity on which an approval
    /// and a parser reading are matched; <see cref="ApprovalKept"/> then checks the senses themselves.
    /// </remarks>
    public ReadingSignature SenseFree() =>
        new([.. Morphemes.Select(morpheme => morpheme with
        {
            SenseId = null, SenseCandidates = [], SenseStatus = ReadingSenseStatus.Unresolved
        })]);

    /// <summary>Whether a parser reading still yields an approved Reading: equal sense-free identity and compatible senses.</summary>
    /// <remarks>
    /// Per morpheme, an approval that resolved a sense is kept only when the parser reading resolves the same sense or lists
    /// it among its candidates. A parser reading that resolves a different sense, or none, loses the approval.
    /// An approval that resolved no sense places no constraint on that morpheme.
    /// </remarks>
    public static bool ApprovalKept(ReadingSignature approved, ReadingSignature parser)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(parser);
        if (approved.SenseFree() != parser.SenseFree()) return false;
        for (var index = 0; index < approved.Morphemes.Length; index++)
        {
            var sense = approved.Morphemes[index].SenseId;
            if (sense is null) continue;
            var other = parser.Morphemes[index];
            if (other.SenseId != sense && !other.SenseCandidates.Contains(sense, StringComparer.Ordinal)) return false;
        }

        return true;
    }

    /// <summary>Returns a defensive copy of the canonical identity bytes for exact content comparison.</summary>
    public byte[] GetCanonicalBytes() => (byte[])canonicalBytes.Clone();

    public bool Equals(ReadingSignature? other) =>
        ReferenceEquals(this, other) || other is not null && canonicalBytes.AsSpan().SequenceEqual(other.canonicalBytes);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record WireShape([property: JsonRequired] ImmutableArray<ReadingMorpheme> Morphemes);

    internal sealed class WireConverter : JsonConverter<ReadingSignature>
    {
        public override ReadingSignature Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var wire = JsonSerializer.Deserialize<WireShape>(ref reader, IdentityJson)!;
            try
            {
                return new(wire.Morphemes);
            }
            catch (ArgumentException error)
            {
                throw new JsonException(error.Message, error);
            }
        }

        public override void Write(Utf8JsonWriter writer, ReadingSignature value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new WireShape(value.Morphemes), IdentityJson);
    }

    private static ReadingMorpheme NormalizeMorpheme(ReadingMorpheme morpheme)
    {
        RequireId(morpheme.EntryId, nameof(morpheme.EntryId));
        RequireId(morpheme.SenseId, nameof(morpheme.SenseId));
        RequireId(morpheme.MsaId, nameof(morpheme.MsaId));
        RequireId(morpheme.AllomorphId, nameof(morpheme.AllomorphId));
        RequireId(morpheme.InflectionTypeId, nameof(morpheme.InflectionTypeId));
        if (morpheme.Derivation is { } derivation)
        {
            RequireId(derivation.FromCategoryId, nameof(derivation.FromCategoryId));
            RequireId(derivation.ToCategoryId, nameof(derivation.ToCategoryId));
        }

        RequireIds(morpheme.SenseCandidates, nameof(morpheme.SenseCandidates));
        var candidates = OrderedSet(morpheme.SenseCandidates);
        var validSense = morpheme.SenseStatus switch
        {
            ReadingSenseStatus.Resolved => morpheme.SenseId is not null && candidates.IsEmpty,
            ReadingSenseStatus.Ambiguous => morpheme.SenseId is null && candidates.Length > 1,
            ReadingSenseStatus.Unresolved => morpheme.SenseId is null && candidates.IsEmpty,
            _ => false
        };
        if (!validSense)
            throw new ArgumentException("Sense ID and candidates must agree with the stated sense status.", nameof(morpheme));

        return morpheme with { SenseCandidates = candidates, FeatureValues = NormalizeFeatures(morpheme.FeatureValues) };
    }

    private static ImmutableArray<ReadingFeatureValue> NormalizeFeatures(ImmutableArray<ReadingFeatureValue> features)
    {
        RequireCollection(features, nameof(features));
        foreach (var feature in features)
        {
            RequireIds(feature.FeaturePath, nameof(feature.FeaturePath));
            RequireIds(feature.ValueIds, nameof(feature.ValueIds));
            if (!Enum.IsDefined(feature.Role) || feature.FeaturePath.IsEmpty || feature.ValueIds.IsEmpty)
                throw new ArgumentException("Each feature needs an authored role, a nonempty path and symbolic values.", nameof(features));
        }

        return [.. features.GroupBy(feature => (feature.Role, Path: string.Join("/", feature.FeaturePath)))
            .OrderBy(group => group.Key.Role).ThenBy(group => group.Key.Path, StringComparer.Ordinal)
            .Select(group => group.First() with { ValueIds = OrderedSet(group.SelectMany(feature => feature.ValueIds)) })];
    }

    private static ImmutableArray<string> OrderedSet(IEnumerable<string> ids) =>
        [.. ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static void RequireIds(ImmutableArray<string> ids, string name)
    {
        RequireCollection(ids, name);
        foreach (var id in ids)
            RequireId(id, name);
    }

    private static void RequireCollection<T>(ImmutableArray<T> values, string name) where T : class
    {
        if (values.IsDefault || values.Any(value => value is null))
            throw new ArgumentException("An initialized collection with non-null elements is required.", name);
    }

    private static void RequireId(string? id, string name)
    {
        if (id is null)
            return;
        if (CanonicalId.TryParse(id, out var parsed) && parsed.Prefix.Length == 0)
        {
            var guid = parsed.ToGuid();
            if (guid != Guid.Empty && id == CanonicalId.FromGuid(guid).Value)
                return;
        }

        throw new ArgumentException("A prefix-free canonical ID for a nonempty native GUID is required.", name);
    }
}

/// <summary>One morpheme in bundle or parser path order. IsStem records the authored stem MSA class.</summary>
/// <remarks>
/// Resolve the native entry through ownership and linkage. AllomorphId identifies the native form.
/// Resolved uses a proved explicit or unique SenseId and no candidates. Ambiguous uses no SenseId and the
/// complete set of at least two distinct proved candidates for the MSA. Unresolved uses neither.
/// An absent optional link differs from a link whose identity or absence cannot be proved.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadingMorpheme(
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? EntryId,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SenseId,
    [property: JsonRequired] ImmutableArray<string> SenseCandidates,
    [property: JsonRequired] ReadingSenseStatus SenseStatus,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? MsaId,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? AllomorphId,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? InflectionTypeId,
    [property: JsonRequired] bool IsStem,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] ReadingDerivation? Derivation,
    [property: JsonRequired] ImmutableArray<ReadingFeatureValue> FeatureValues);

/// <summary>Whether the evidence identifies one sense, leaves multiple candidates, or cannot resolve the sense.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<ReadingSenseStatus>))]
public enum ReadingSenseStatus
{
    [JsonStringEnumMemberName("resolved")] Resolved,
    [JsonStringEnumMemberName("ambiguous")] Ambiguous,
    [JsonStringEnumMemberName("unresolved")] Unresolved
}

/// <summary>Directed category transition authored on a derivational morpheme.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadingDerivation(
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FromCategoryId,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ToCategoryId);

/// <summary>Symbolic values at an authored feature path, ordered from outermost feature to leaf.</summary>
/// <remarks>Follow owned complex structures to closed symbolic leaves, preserving each authored feature GUID.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadingFeatureValue([property: JsonRequired] ReadingFeatureRole Role,
    [property: JsonRequired] ImmutableArray<string> FeaturePath, [property: JsonRequired] ImmutableArray<string> ValueIds);

/// <summary>Where the feature value is authored on the morpheme's MSA.</summary>
/// <remarks>
/// Keep MsFeatures on stem and derivational-step MSAs distinct. InflFeats is inflection.
/// FromMsFeatures and ToMsFeatures are derivation input and output, respectively.
/// </remarks>
[JsonConverter(typeof(StrictStringEnumConverter<ReadingFeatureRole>))]
public enum ReadingFeatureRole
{
    [JsonStringEnumMemberName("stem")] Stem,
    [JsonStringEnumMemberName("inflection")] Inflection,
    [JsonStringEnumMemberName("derivation-input")] DerivationInput,
    [JsonStringEnumMemberName("derivation-output")] DerivationOutput,
    [JsonStringEnumMemberName("derivational-step")] DerivationalStep
}
