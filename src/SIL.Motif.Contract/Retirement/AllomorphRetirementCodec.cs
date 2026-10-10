using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;

namespace SIL.Motif.Contract.Retirement;

/// <summary>
/// Closed, pure authoring codec. Structural validation cannot replace the authoritative LibLCM census,
/// semantic prohibition check, frozen expectation translation or atomic Apply of ADR 0057.
/// </summary>
public static class AllomorphRetirementCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter<AllomorphRetirementScope>(JsonNamingPolicy.CamelCase, false) }
    };

    /// <summary>Parses strict complete input; null InflType, anchor and member must be explicit.</summary>
    public static ReplaceListedAllomorphsWithRuleIntent Parse(string json)
    {
        try
        {
            AllomorphRetirementValidation.Require(Encoding.UTF8.GetByteCount(json) <= 16 * 1024 * 1024,
                "Retirement envelope exceeds 16 MiB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            CheckObject(document.RootElement);
            AllomorphRetirementValidation.Require(document.RootElement.TryGetProperty("format", out _) &&
                document.RootElement.TryGetProperty("version", out _), "Missing format/version.");
            var value = JsonSerializer.Deserialize<ReplaceListedAllomorphsWithRuleIntent>(json, Options)
                ?? throw new FormatException("Null retirement intent.");
            AllomorphRetirementValidation.Validate(value);
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid closed allomorph retirement: " + error.Message, error);
        }
    }

    /// <summary>Emits RFC 8785 authoring JSON; graph array order has no authority over execution.</summary>
    public static string ToJson(ReplaceListedAllomorphsWithRuleIntent value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        Parse(json);
        return CanonicalJson.Canonicalize(json);
    }

    /// <summary>
    /// Hashes semantic authoring input, excluding only Display. Set-like arrays are sorted by canonical
    /// content; rule/context sequences retain their linguistic order. This is not the finalized Proposal digest.
    /// </summary>
    public static string IntentDigest(ReplaceListedAllomorphsWithRuleIntent value)
    {
        var node = JsonNode.Parse(ToJson(value))!.AsObject();
        node.Remove("display");
        Normalize(node);
        return Canonicalization.IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(node.ToJsonString()));
    }

    private static readonly HashSet<string> Unordered = new(StringComparer.Ordinal)
    { "members", "retirements", "retiredForms", "roleReplacements", "bundles", "adhocReplacements", "operations", "dependsOn" };
    private static readonly HashSet<string> IdentityProperties = new(StringComparer.Ordinal)
    { "id", "entry", "retiredForm", "msa", "inflType", "bundle", "analysis", "wordform", "operationId", "target", "member", "anchor", "stemName" };

    private static void Normalize(JsonNode node, string? property = null)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (obj[key] is not { } child) continue;
                if (child is JsonValue text && text.TryGetValue<string>(out var content))
                    obj[key] = IdentityProperties.Contains(key) ? AllomorphRetirementValidation.Key(content)
                        : content.Normalize(NormalizationForm.FormD);
                else Normalize(child, key);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null) Normalize(item, property);
            if (property is "members" or "input" or "output" or "dependsOn")
                for (var index = 0; index < array.Count; index++)
                    array[index] = AllomorphRetirementValidation.Key(array[index]!.GetValue<string>());
            if (property is not null && Unordered.Contains(property))
            {
                var sorted = array.Select(item => item!.DeepClone()).OrderBy(
                    item => CanonicalJson.Canonicalize("[" + item.ToJsonString() + "]"), StringComparer.Ordinal).ToArray();
                array.Clear();
                foreach (var item in sorted) array.Add(item);
            }
        }
    }

    internal static void CheckObject(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                AllomorphRetirementValidation.Require(names.Add(property.Name), "Duplicate JSON property.");
                if (property.Name == "scope")
                    AllomorphRetirementValidation.Require(property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is "affix" or "stem", "Unsupported retirement scope.");
                CheckObject(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckObject(item);
    }
}
