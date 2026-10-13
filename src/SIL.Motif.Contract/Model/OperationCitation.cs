using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Contract.Model;

/// <summary>
/// The source an operation was taken from, such as a section of a published grammar, a dictionary entry, or a
/// corpus passage. It records where a change came from, never who authored the change.
/// </summary>
/// <remarks>
/// A citation is stored as one entry of the operation's <c>provenance</c> array:
/// <c>{"type":"citation","source":"Steere 1870","locator":"p. 142"}</c>. The array is review metadata outside the
/// intent digest, so citing a source, or citing a different one, never changes what a Proposal does or its digest.
/// An operation carries at most one citation; other <c>provenance</c> entries stay open.
/// </remarks>
public sealed record OperationCitation
{
    /// <summary>The <c>type</c> value that marks a <c>provenance</c> entry as a citation.</summary>
    public const string ProvenanceType = "citation";

    /// <summary>The longest <see cref="Source"/> accepted, in UTF-16 code units after trimming.</summary>
    public const int MaxSourceLength = 300;

    /// <summary>The longest <see cref="Locator"/> accepted, in UTF-16 code units after trimming.</summary>
    public const int MaxLocatorLength = 120;

    private OperationCitation(string source, string? locator)
    {
        Source = source;
        Locator = locator;
    }

    /// <summary>The work cited, such as <c>Steere 1870, A Handbook of the Swahili Language</c>.</summary>
    public string Source { get; }

    /// <summary>Where in the source, such as a section, page or verse; <c>null</c> when the whole source is cited.</summary>
    public string? Locator { get; }

    /// <summary>
    /// Builds a citation from caller text, trimming both parts; a blank locator counts as absent.
    /// </summary>
    /// <exception cref="ContractParseException">The source is blank, or either part is too long.</exception>
    public static OperationCitation Create(string? source, string? locator = null)
    {
        var trimmedSource = source?.Trim() ?? string.Empty;
        if (trimmedSource.Length == 0)
            throw new ContractParseException("A citation needs a non-empty 'source'.");
        if (trimmedSource.Length > MaxSourceLength)
            throw new ContractParseException($"A citation 'source' must be at most {MaxSourceLength} characters.");
        var trimmedLocator = string.IsNullOrWhiteSpace(locator) ? null : locator.Trim();
        if (trimmedLocator is { Length: > MaxLocatorLength })
            throw new ContractParseException($"A citation 'locator' must be at most {MaxLocatorLength} characters.");
        return new OperationCitation(trimmedSource, trimmedLocator);
    }

    /// <summary>
    /// Reads a caller-supplied citation object, <c>{"source": "...", "locator": "..."}</c>, refusing any other
    /// property.
    /// </summary>
    /// <exception cref="ContractParseException">The value is not a well-formed citation object.</exception>
    public static OperationCitation Parse(JsonElement value) => ParseObject(value, allowType: false);

    /// <summary>
    /// Returns the operation's citation, or <c>null</c> when no <c>provenance</c> entry is a citation.
    /// </summary>
    /// <exception cref="ContractParseException">A citation entry is malformed, or there is more than one.</exception>
    public static OperationCitation? FromProvenance(IEnumerable<JsonElement> provenance)
    {
        OperationCitation? found = null;
        foreach (var entry in provenance)
        {
            if (!IsCitationEntry(entry)) continue;
            if (found is not null)
                throw new ContractParseException("An operation carries at most one citation in 'provenance'.");
            found = ParseObject(entry, allowType: true);
        }
        return found;
    }

    /// <summary>
    /// Returns <paramref name="provenance"/> with its citation replaced by <paramref name="citation"/>, or removed
    /// when that is <c>null</c>, keeping every other entry in place.
    /// </summary>
    public static IReadOnlyList<JsonElement> Replace(IEnumerable<JsonElement> provenance, OperationCitation? citation)
    {
        var result = new List<JsonElement>();
        foreach (var entry in provenance)
            if (!IsCitationEntry(entry))
                result.Add(entry);
        if (citation is not null) result.Add(citation.ToProvenanceEntry());
        return result;
    }

    /// <summary>The <c>provenance</c> entry that records this citation.</summary>
    public JsonElement ToProvenanceEntry()
    {
        var entry = new Dictionary<string, string> { ["type"] = ProvenanceType, ["source"] = Source };
        if (Locator is not null) entry["locator"] = Locator;
        return JsonSerializer.SerializeToElement(entry);
    }

    /// <summary>The source followed by its locator, for one line of text.</summary>
    public override string ToString() => Locator is null ? Source : Source + ", " + Locator;

    private static bool IsCitationEntry(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String && type.GetString() == ProvenanceType;

    private static OperationCitation ParseObject(JsonElement value, bool allowType)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ContractParseException("A citation must be a JSON object.");
        string? source = null;
        string? locator = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new ContractParseException($"A citation repeats '{property.Name}'.");
            switch (property.Name)
            {
                case "type" when allowType:
                    break;
                case "source":
                    source = StringValue(property);
                    break;
                case "locator":
                    locator = StringValue(property);
                    break;
                default:
                    throw new ContractParseException(
                        $"A citation has only 'source' and 'locator'; '{property.Name}' is not accepted.");
            }
        }
        return Create(source, locator);
    }

    private static string StringValue(JsonProperty property) => property.Value.ValueKind == JsonValueKind.String
        ? property.Value.GetString()!
        : throw new ContractParseException($"A citation '{property.Name}' must be a string.");
}
