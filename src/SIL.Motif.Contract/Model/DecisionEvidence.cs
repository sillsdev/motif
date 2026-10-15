using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Contract.Model;

/// <summary>One source a source-validated decision rests on: a dictionary entry, a grammar section, a corpus count or an example.</summary>
/// <param name="Kind">One of <see cref="DecisionEvidence.SourceKinds"/>.</param>
/// <param name="Source">The work and version, 1 to 300 characters.</param>
/// <param name="Locator">The entry id, section, page or count, at most 120 characters; <c>null</c> when the whole source is meant.</param>
public sealed record DecisionEvidenceSource(string Kind, string Source, string? Locator);

/// <summary>
/// Why a program, not a person, decided: the tier of the decision and the sources and facts it rests on. It is
/// Motif's own record and never part of the FieldWorks project.
/// </summary>
/// <remarks>
/// It is stored as one entry of the operation's <c>provenance</c> array:
/// <c>{"type":"decisionEvidence","tier":"misspelled-typo","sources":[...],"detail":{...},"stagedBy":"..."}</c>.
/// Like a citation it is review metadata outside the intent digest, an operation carries at most one, and it never
/// names an author of the Proposal: <see cref="StagedBy"/> is free provenance text about the staging tool.
/// </remarks>
public sealed record DecisionEvidence
{
    /// <summary>The <c>type</c> value that marks a <c>provenance</c> entry as decision evidence.</summary>
    public const string ProvenanceType = "decisionEvidence";

    /// <summary>The tiers a source-validated decision can carry.</summary>
    public static readonly IReadOnlyList<string> Tiers =
    [
        "source-approved", "valid-attested", "valid-productive", "misspelled-typo", "ill-formed",
        "nonstandard-variant", "join-split", "name-foreign-number", "undecided",
    ];

    /// <summary>The kinds a <see cref="DecisionEvidenceSource"/> can have.</summary>
    public static readonly IReadOnlyList<string> SourceKinds = ["dictionary", "grammar", "corpus", "example"];

    /// <summary>The most sources one record holds.</summary>
    public const int MaxSources = 12;

    /// <summary>The longest <see cref="DecisionEvidenceSource.Source"/>, in UTF-16 code units.</summary>
    public const int MaxSourceLength = 300;

    /// <summary>The longest locator, detail text and <see cref="StagedBy"/>, in UTF-16 code units.</summary>
    public const int MaxTextLength = 120;

    private static readonly string[] DetailNames =
        ["intendedForm", "editType", "violatedRule", "standardForm", "corpusCount"];

    private DecisionEvidence(string tier, IReadOnlyList<DecisionEvidenceSource> sources,
        IReadOnlyDictionary<string, string> detail, string? stagedBy)
    {
        Tier = tier;
        Sources = sources;
        Detail = detail;
        StagedBy = stagedBy;
    }

    /// <summary>One of <see cref="Tiers"/>.</summary>
    public string Tier { get; }

    /// <summary>The sources, in the order given.</summary>
    public IReadOnlyList<DecisionEvidenceSource> Sources { get; }

    /// <summary>
    /// The tier's facts by name: <c>intendedForm</c>, <c>editType</c>, <c>violatedRule</c>, <c>standardForm</c> and
    /// <c>corpusCount</c> (a whole number, kept as its text).
    /// </summary>
    public IReadOnlyDictionary<string, string> Detail { get; }

    /// <summary>The staging tool and model, as provenance text only; <c>null</c> when not given.</summary>
    public string? StagedBy { get; }

    /// <summary>Builds a record, refusing an unknown tier or detail name, an over-long text, or a tier missing what it requires.</summary>
    /// <exception cref="ContractParseException">The record is not well formed.</exception>
    public static DecisionEvidence Create(string? tier, IEnumerable<DecisionEvidenceSource>? sources,
        IReadOnlyDictionary<string, string>? detail, string? stagedBy)
    {
        if (tier is null || !Tiers.Contains(tier, StringComparer.Ordinal))
            throw new ContractParseException($"Decision evidence 'tier' must be one of: {string.Join(", ", Tiers)}.");
        var sourceList = (sources ?? []).ToArray();
        if (sourceList.Length > MaxSources)
            throw new ContractParseException($"Decision evidence has at most {MaxSources} sources.");
        foreach (var source in sourceList)
        {
            if (!SourceKinds.Contains(source.Kind, StringComparer.Ordinal))
                throw new ContractParseException(
                    $"A decision evidence source 'kind' must be one of: {string.Join(", ", SourceKinds)}.");
            var name = source.Source?.Trim() ?? string.Empty;
            if (name.Length == 0 || name.Length > MaxSourceLength)
                throw new ContractParseException(
                    $"A decision evidence 'source' must be 1 to {MaxSourceLength} characters.");
            var locator = string.IsNullOrWhiteSpace(source.Locator) ? null : source.Locator.Trim();
            if (locator is { Length: > MaxTextLength })
                throw new ContractParseException(
                    $"A decision evidence 'locator' must be at most {MaxTextLength} characters.");
        }
        var cleaned = sourceList.Select(source => new DecisionEvidenceSource(
            source.Kind, source.Source!.Trim(), string.IsNullOrWhiteSpace(source.Locator) ? null : source.Locator.Trim())).ToArray();
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in detail ?? new Dictionary<string, string>())
        {
            if (!DetailNames.Contains(key, StringComparer.Ordinal))
                throw new ContractParseException(
                    $"Decision evidence 'detail' accepts only {string.Join(", ", DetailNames)}; '{key}' is not accepted.");
            var text = value?.Trim() ?? string.Empty;
            if (text.Length == 0 || text.Length > MaxTextLength)
                throw new ContractParseException($"Decision evidence detail '{key}' must be 1 to {MaxTextLength} characters.");
            if (key == "corpusCount" && !(int.TryParse(text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out _)))
                throw new ContractParseException("Decision evidence detail 'corpusCount' must be a whole number of at least 0.");
            facts[key] = text;
        }
        var staged = string.IsNullOrWhiteSpace(stagedBy) ? null : stagedBy.Trim();
        if (staged is { Length: > MaxTextLength })
            throw new ContractParseException($"Decision evidence 'stagedBy' must be at most {MaxTextLength} characters.");
        RequireTierContent(tier, cleaned, facts);
        return new DecisionEvidence(tier, cleaned, facts, staged);
    }

    /// <summary>Returns the operation's decision evidence, or <c>null</c> when no <c>provenance</c> entry is one.</summary>
    /// <exception cref="ContractParseException">An entry is malformed, or there is more than one.</exception>
    public static DecisionEvidence? FromProvenance(IEnumerable<JsonElement> provenance)
    {
        DecisionEvidence? found = null;
        foreach (var entry in provenance)
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != ProvenanceType) continue;
            if (found is not null)
                throw new ContractParseException("An operation carries at most one decision evidence in 'provenance'.");
            found = ParseEntry(entry);
        }
        return found;
    }

    /// <summary>The <c>provenance</c> entry that records this evidence.</summary>
    public JsonElement ToProvenanceEntry()
    {
        var entry = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = ProvenanceType,
            ["tier"] = Tier,
            ["sources"] = Sources.Select(source => source.Locator is null
                ? new Dictionary<string, string> { ["kind"] = source.Kind, ["source"] = source.Source }
                : new Dictionary<string, string>
                    { ["kind"] = source.Kind, ["source"] = source.Source, ["locator"] = source.Locator }).ToArray(),
        };
        if (Detail.Count > 0)
            entry["detail"] = Detail.ToDictionary(pair => pair.Key,
                pair => pair.Key == "corpusCount" ? (object)int.Parse(pair.Value, System.Globalization.CultureInfo.InvariantCulture) : pair.Value);
        if (StagedBy is not null) entry["stagedBy"] = StagedBy;
        return JsonSerializer.SerializeToElement(entry);
    }

    private static void RequireTierContent(string tier, DecisionEvidenceSource[] sources,
        IReadOnlyDictionary<string, string> facts)
    {
        void Need(string name)
        {
            if (!facts.ContainsKey(name))
                throw new ContractParseException($"Decision evidence tier '{tier}' requires detail '{name}'.");
        }
        switch (tier)
        {
            case "misspelled-typo":
                Need("intendedForm");
                Need("editType");
                break;
            case "ill-formed":
                Need("violatedRule");
                break;
            case "nonstandard-variant":
                Need("standardForm");
                break;
            case "source-approved":
                foreach (var kind in new[] { "dictionary", "grammar", "corpus" })
                    if (!sources.Any(source => source.Kind == kind))
                        throw new ContractParseException(
                            $"Decision evidence tier 'source-approved' requires a '{kind}' source.");
                break;
        }
    }

    private static DecisionEvidence ParseEntry(JsonElement entry)
    {
        string? tier = null, stagedBy = null;
        var sources = new List<DecisionEvidenceSource>();
        var detail = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in entry.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new ContractParseException($"Decision evidence repeats '{property.Name}'.");
            switch (property.Name)
            {
                case "type":
                    break;
                case "tier":
                    tier = Text(property.Value, "tier");
                    break;
                case "stagedBy":
                    stagedBy = Text(property.Value, "stagedBy");
                    break;
                case "sources":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        throw new ContractParseException("Decision evidence 'sources' must be an array.");
                    foreach (var item in property.Value.EnumerateArray()) sources.Add(ParseSource(item));
                    break;
                case "detail":
                    if (property.Value.ValueKind != JsonValueKind.Object)
                        throw new ContractParseException("Decision evidence 'detail' must be an object.");
                    foreach (var fact in property.Value.EnumerateObject())
                    {
                        if (detail.ContainsKey(fact.Name))
                            throw new ContractParseException($"Decision evidence detail repeats '{fact.Name}'.");
                        detail[fact.Name] = fact.Name == "corpusCount"
                            ? fact.Value.ValueKind == JsonValueKind.Number && fact.Value.TryGetInt32(out var count) && count >= 0
                                ? count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                : throw new ContractParseException("Decision evidence detail 'corpusCount' must be a whole number of at least 0.")
                            : Text(fact.Value, fact.Name);
                    }
                    break;
                default:
                    throw new ContractParseException(
                        $"Decision evidence has only tier, sources, detail and stagedBy; '{property.Name}' is not accepted.");
            }
        }
        return Create(tier, sources, detail, stagedBy);
    }

    private static DecisionEvidenceSource ParseSource(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new ContractParseException("A decision evidence source must be a JSON object.");
        string? kind = null, source = null, locator = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new ContractParseException($"A decision evidence source repeats '{property.Name}'.");
            switch (property.Name)
            {
                case "kind": kind = Text(property.Value, "kind"); break;
                case "source": source = Text(property.Value, "source"); break;
                case "locator": locator = Text(property.Value, "locator"); break;
                default:
                    throw new ContractParseException(
                        $"A decision evidence source has only kind, source and locator; '{property.Name}' is not accepted.");
            }
        }
        return new DecisionEvidenceSource(kind ?? string.Empty, source ?? string.Empty, locator);
    }

    private static string Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.String
        ? value.GetString()!
        : throw new ContractParseException($"Decision evidence '{name}' must be a string.");
}
