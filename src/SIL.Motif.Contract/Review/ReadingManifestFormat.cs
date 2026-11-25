using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Contract.Review;

/// <summary>
/// The expected-change manifest format the grammar lanes read and write: one JSON object per line holding a word,
/// the Reading's signature as an ordered array of morpheme objects, and a copy count. Keys are sorted at every level and
/// separators follow the lane writer (a comma and a colon each followed by a space), so a file written here equals the
/// one the lane's own writer produces for the same delta once the Proposal's created ids are aliased the same way.
/// </summary>
public static class ReadingManifestFormat
{
    /// <summary>The file name of the retired manifest.</summary>
    public const string RetiredFile = "expect-retired.jsonl";

    /// <summary>The file name of the added manifest.</summary>
    public const string AddedFile = "expect-added.jsonl";

    /// <summary>Whether every morpheme names its entry, MSA, allomorph and a resolved or ambiguous sense.</summary>
    /// <param name="signature">The Reading to test.</param>
    /// <returns>False when a manifest row could not state the Reading completely.</returns>
    public static bool IsComplete(ReadingSignature signature) =>
        signature.Morphemes.Length > 0 && signature.Morphemes.All(morpheme =>
            morpheme.SenseStatus != ReadingSenseStatus.Unresolved && morpheme.EntryId is not null &&
            morpheme.MsaId is not null && morpheme.AllomorphId is not null &&
            (morpheme.SenseStatus == ReadingSenseStatus.Ambiguous || morpheme.SenseId is not null));

    /// <summary>
    /// The aliases the lane gate gives ids a Proposal creates: each operation whose kind contains "create" names one
    /// entity, numbered <c>created:N</c> in first-appearance order, under its canonical, lower-case GUID and upper-case GUID spellings.
    /// Every kind of object the Proposal creates is named this way, so a sense or an alternate form's allomorph is
    /// <c>created:N</c> as an entry and an MSA are, and the same Proposal written twice with different minted ids gives
    /// byte-identical rows. This is deliberately a superset of the rig's manifest writer, which in the Tamil q29 run left
    /// senses and the allomorphs of new alternate forms as minted ids; the parity fixture still pins entries and MSAs.
    /// </summary>
    /// <param name="operations">The Proposal's operations, in order.</param>
    /// <returns>Each created id mapped to its alias.</returns>
    public static IReadOnlyDictionary<string, string> CreatedAliases(IEnumerable<OperationEnvelope> operations)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in operations)
            if (operation.EntityId is { } entity && operation.Kind.Contains("create", StringComparison.OrdinalIgnoreCase) &&
                !aliases.ContainsKey(entity.Value))
                aliases[entity.Value] = "created:" + aliases.Count;
        foreach (var (id, label) in aliases.ToArray())
        {
            var identity = CanonicalId.Parse(id).ToGuid();
            var guid = identity.ToString("D", CultureInfo.InvariantCulture);
            aliases[guid] = aliases[guid.ToUpperInvariant()] = aliases[CanonicalId.FromGuid(identity).Value] = label;
        }
        return aliases;
    }

    /// <summary>The identity two signatures share when they are the same manifest Reading: compact, key-sorted JSON.</summary>
    /// <param name="signature">A complete signature.</param>
    /// <param name="aliases">Ids to rename one at a time, or null to keep real ids.</param>
    /// <returns>The compact encoding of the morpheme array.</returns>
    public static string Key(ReadingSignature signature, IReadOnlyDictionary<string, string>? aliases = null) =>
        Encode(Lane(signature, aliases), compact: true);

    /// <summary>The signature as a manifest row writes it, for a reader that wants the array and not the line.</summary>
    /// <param name="signature">A complete signature.</param>
    /// <returns>The array of morpheme objects.</returns>
    public static JsonElement ToElement(ReadingSignature signature)
    {
        using var document = JsonDocument.Parse(Encode(Lane(signature), compact: true));
        return document.RootElement.Clone();
    }

    /// <summary>One manifest line, without its newline.</summary>
    /// <param name="word">The measured spelling.</param>
    /// <param name="signature">A complete signature.</param>
    /// <param name="count">The copies the manifest expects.</param>
    /// <param name="aliases">Ids to rename one at a time, or null to keep real ids.</param>
    /// <returns>The row text.</returns>
    public static string Row(string word, ReadingSignature signature, long count,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        var text = new StringBuilder("{\"word\": ");
        Quote(text, word);
        return text.Append(", \"signature\": ").Append(Encode(Lane(signature, aliases), compact: false))
            .Append(", \"count\": ").Append(count.ToString(CultureInfo.InvariantCulture)).Append('}').ToString();
    }

    /// <summary>Reads one manifest line into the word, the Reading's <see cref="Key"/> and its copy count.</summary>
    /// <param name="line">A manifest line.</param>
    /// <param name="word">The word, in NFC.</param>
    /// <param name="key">The compact, key-sorted encoding of the signature; equal to <see cref="Key"/> of the same Reading.</param>
    /// <param name="count">The copies, one when the row omits it.</param>
    /// <returns>False when the line is not a valid row.</returns>
    public static bool TryParseRow(string line, out string word, out string key, out long count)
    {
        (word, key, count) = ("", "", 0);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("word", out var wordValue) || wordValue.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.Array ||
                signature.GetArrayLength() == 0 ||
                root.EnumerateObject().Any(property => property.Name is not ("word" or "signature" or "count")))
                return false;
            count = 1;
            if (root.TryGetProperty("count", out var copies) && (!copies.TryGetInt64(out count) || count < 1)) return false;
            word = wordValue.GetString()!.Normalize(NormalizationForm.FormC);
            key = Encode(Canonical(signature), compact: true);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static List<object?> Lane(ReadingSignature signature, IReadOnlyDictionary<string, string>? aliases = null)
    {
        if (!IsComplete(signature)) throw new ArgumentException("The Reading has no complete identity.", nameof(signature));
        return [.. signature.Morphemes.Select(morpheme => Morph(morpheme, aliases))];
    }

    private static SortedDictionary<string, object?> Morph(ReadingMorpheme morpheme, IReadOnlyDictionary<string, string>? aliases)
    {
        string A(string id) => aliases is not null && aliases.TryGetValue(id, out var label) ? label : id;
        var features = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var feature in morpheme.FeatureValues)
            features[RoleName(feature.Role) + ":" + string.Join("/", feature.FeaturePath.Select(A))] =
                string.Join(",", feature.ValueIds.Select(A).Order(StringComparer.Ordinal));
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["allomorphId"] = A(morpheme.AllomorphId!), ["entryId"] = A(morpheme.EntryId!), ["features"] = features,
            ["isStem"] = morpheme.IsStem, ["msaId"] = A(morpheme.MsaId!),
        };
        if (morpheme.SenseStatus == ReadingSenseStatus.Ambiguous)
            result["senseCandidates"] = morpheme.SenseCandidates.Select(A).Order(StringComparer.Ordinal).Cast<object?>().ToList();
        else result["senseId"] = A(morpheme.SenseId!);
        if (morpheme.InflectionTypeId is not null) result["inflectionTypeId"] = A(morpheme.InflectionTypeId);
        if (morpheme.Derivation is { } derivation)
            result["derivation"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["fromCategoryId"] = derivation.FromCategoryId is null ? null : A(derivation.FromCategoryId),
                ["toCategoryId"] = derivation.ToCategoryId is null ? null : A(derivation.ToCategoryId),
            };
        return result;
    }

    private static string RoleName(ReadingFeatureRole role) => role switch
    {
        ReadingFeatureRole.Stem => "stem",
        ReadingFeatureRole.Inflection => "inflection",
        ReadingFeatureRole.DerivationInput => "derivation-input",
        ReadingFeatureRole.DerivationOutput => "derivation-output",
        _ => "derivational-step",
    };

    private static object? Canonical(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var map = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject()) map[property.Name] = Canonical(property.Value);
                if (map.TryGetValue("senseCandidates", out var candidates) && candidates is List<object?> list)
                    map["senseCandidates"] = list.Cast<string>().Order(StringComparer.Ordinal).Cast<object?>().ToList();
                return map;
            case JsonValueKind.Array: return value.EnumerateArray().Select(Canonical).ToList();
            case JsonValueKind.String: return value.GetString();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return value.GetInt64();
            default: return null;
        }
    }

    private static string Encode(object? value, bool compact)
    {
        var text = new StringBuilder();
        Append(text, value, compact);
        return text.ToString();
    }

    private static void Append(StringBuilder text, object? value, bool compact)
    {
        switch (value)
        {
            case null: text.Append("null"); break;
            case bool flag: text.Append(flag ? "true" : "false"); break;
            case long number: text.Append(number.ToString(CultureInfo.InvariantCulture)); break;
            case string content: Quote(text, content); break;
            case IReadOnlyDictionary<string, object?> map:
                text.Append('{');
                var first = true;
                foreach (var (name, item) in map)
                {
                    if (!first) text.Append(compact ? "," : ", ");
                    first = false;
                    Quote(text, name);
                    text.Append(compact ? ":" : ": ");
                    Append(text, item, compact);
                }
                text.Append('}');
                break;
            case IEnumerable<object?> items:
                text.Append('[');
                var separator = "";
                foreach (var item in items)
                {
                    text.Append(separator);
                    separator = compact ? "," : ", ";
                    Append(text, item, compact);
                }
                text.Append(']');
                break;
            default: throw new InvalidOperationException("Unsupported manifest value.");
        }
    }

    // Escapes only the quote, the backslash and control characters, leaving other text as written.
    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case < ' ': text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: text.Append(character); break;
            }
        }
        text.Append('"');
    }
}
