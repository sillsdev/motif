using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Contract.Retirement;

/// <summary>
/// A source-bound comparison pair for one explicit retirement Proposal. The original remains the
/// before oracle; the translated set changes only the exact authored Form roles and any separately
/// authored Notebook negative revision present in the Proposal's Dry Run.
/// </summary>
public sealed record RetirementExpectationTranslation(
    FrozenExpectationSet Original,
    FrozenExpectationSet Translated,
    IReadOnlyList<RetirementExpectationMapping> Mappings,
    IReadOnlyList<RetirementBundleTextEffect> BundleTextEffects,
    string MappingDigest,
    string ProposalIntentDigest,
    string DryRunFootprintDigest,
    string DryRunEffectDigest)
{
    /// <summary>The versioned Report contract for source and translated retirement expectations.</summary>
    public string Contract { get; init; } = RetirementExpectationTranslationCodec.ContractVersion;
}

/// <summary>An authored identity destination at one exact MSA, inflection type and expansion role.</summary>
public sealed record RetirementExpectationMapping(
    string RetiredForm, string Msa, string? InflType, string ExpansionRole, string ReplacementForm);

/// <summary>The native bundle text transition read back from a retirement Dry Run.</summary>
public sealed record RetirementBundleTextEffect(
    string Bundle, IReadOnlyDictionary<string, string> Before, IReadOnlyDictionary<string, string> After);

/// <summary>Validates and serializes the frozen comparison pair and its Proposal bindings.</summary>
public static class RetirementExpectationTranslationCodec
{
    public const string ContractVersion = "retirement-expectation-translation/v1";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 48,
    };

    /// <summary>Reads a closed pair and checks its frozen Baseline and mapping digest.</summary>
    public static RetirementExpectationTranslation Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            AllomorphRetirementCodec.CheckObject(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("contract", out _))
                throw new FormatException("Retirement expectation translation requires its contract version.");
            var value = JsonSerializer.Deserialize<RetirementExpectationTranslation>(json, Options)
                ?? throw new FormatException("Null retirement expectation translation.");
            Validate(value);
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid retirement expectation translation: " + error.Message, error);
        }
    }

    /// <summary>Serializes the pair as canonical JSON after validating its source and mapping bindings.</summary>
    public static string ToJson(RetirementExpectationTranslation value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        Parse(json);
        return CanonicalJson.Canonicalize(json);
    }

    /// <summary>Hashes the canonical authored role mappings independently of the surrounding Proposal.</summary>
    public static string ComputeMappingDigest(IEnumerable<RetirementExpectationMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var ordered = mappings.Select(item => item with
            {
                RetiredForm = CanonicalValue(item.RetiredForm),
                Msa = CanonicalValue(item.Msa),
                InflType = item.InflType is null ? null : CanonicalValue(item.InflType),
                ReplacementForm = CanonicalValue(item.ReplacementForm),
            })
            .OrderBy(item => item.RetiredForm, StringComparer.Ordinal)
            .ThenBy(item => item.Msa, StringComparer.Ordinal)
            .ThenBy(item => item.InflType, StringComparer.Ordinal)
            .ThenBy(item => item.ExpansionRole, StringComparer.Ordinal)
            .ThenBy(item => item.ReplacementForm, StringComparer.Ordinal).ToArray();
        var json = JsonSerializer.Serialize(ordered, Options);
        return IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(json));
    }

    private static void Validate(RetirementExpectationTranslation value)
    {
        Require(value.Contract == ContractVersion, "Unsupported retirement expectation translation contract.");
        var original = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(value.Original));
        var translated = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(value.Translated));
        Require(original.Baseline == translated.Baseline, "Translated expectations must retain the source Baseline.");
        var mappings = value.Mappings ?? throw new FormatException("At least one exact role mapping is required.");
        Require(mappings.Count > 0 && mappings.All(item => item is not null),
            "At least one exact role mapping is required.");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in mappings)
        {
            Id(item.RetiredForm); Id(item.Msa); OptionalId(item.InflType); Id(item.ReplacementForm);
            Text(item.ExpansionRole);
            Require(Identity(item.RetiredForm) != Identity(item.ReplacementForm),
                "A retirement role mapping must name a different replacement Form.");
            var key = string.Join('\0', Identity(item.RetiredForm), Identity(item.Msa),
                item.InflType is null ? "<null>" : Identity(item.InflType), item.ExpansionRole);
            Require(roles.Add(key), "A role mapping is repeated.");
        }
        Digest(value.MappingDigest); Digest(value.ProposalIntentDigest);
        Digest(value.DryRunFootprintDigest); Digest(value.DryRunEffectDigest);
        Require(value.MappingDigest == ComputeMappingDigest(mappings), "The role mapping digest does not match its mappings.");
        var effects = value.BundleTextEffects ?? throw new FormatException("Missing bundle text effects.");
        Require(effects.Count > 0 && effects.All(item => item is not null), "Missing bundle text effects.");
        var bundles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in effects)
        {
            Id(effect.Bundle);
            Require(bundles.Add(Identity(effect.Bundle)), "A bundle text effect is repeated.");
            Alternatives(effect.Before); Alternatives(effect.After);
        }
    }

    private static void Alternatives(IReadOnlyDictionary<string, string>? values)
    {
        var alternatives = values ?? throw new FormatException("A bundle text effect needs before and after alternatives.");
        foreach (var (writingSystem, text) in alternatives)
        {
            Text(writingSystem);
            Require(text is not null && text.Normalize(System.Text.NormalizationForm.FormD) == text,
                "Bundle text effects must preserve NFD text.");
        }
    }

    private static void Id(string value) => Require(SIL.Motif.Contract.Ids.CanonicalId.TryParse(value, out _),
        "A retirement mapping identity is not canonical.");
    private static string Identity(string value) => CanonicalId.FromGuid(CanonicalId.Parse(value).ToGuid()).Suffix;
    private static string CanonicalValue(string value) => CanonicalId.FromGuid(CanonicalId.Parse(value).ToGuid()).Value;
    private static void OptionalId(string? value) { if (value is not null) Id(value); }
    private static void Digest(string value) => Require(Sha256Value.IsCanonical(value), "A retirement binding digest is invalid.");
    private static void Text(string value) => Require(!string.IsNullOrWhiteSpace(value), "A retirement binding value is missing.");
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
