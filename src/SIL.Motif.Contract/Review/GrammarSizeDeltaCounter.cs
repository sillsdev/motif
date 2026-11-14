using System.Text.Json;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Contract.Review;

/// <summary>Counts the grammar objects a Proposal's operations create, from the operations alone.</summary>
public static class GrammarSizeDeltaCounter
{
    private const string CreateEntries = "lexical/lexDb/createEntries";
    private const string CreateVariantRef = "lexical/lexEntry/createVariantRef";
    private const string CreateAlternateForms = "lexical/lexEntry/createAlternateForms";
    private const string CreatePossibilities = "lists/cmPossibilityList/createPossibilities";

    /// <summary>Counts what the operations add; operations that add no object are ignored.</summary>
    public static GrammarSizeDelta Count(IEnumerable<OperationEnvelope> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var all = operations.ToArray();
        var entries = all.Where(item => item.Kind == CreateEntries && item.EntityId is not null)
            .Select(item => item.EntityId!.Value.Value).ToHashSet(StringComparer.Ordinal);
        var variantEntries = all.Where(item => item.Kind == CreateVariantRef && item.Target is not null)
            .Select(item => item.Target!.Value.Value).Where(entries.Contains).ToHashSet(StringComparer.Ordinal);
        var hosts = all.Where(IsHost).ToArray();
        var hostRefs = hosts.Select(Strings).ToArray();
        var referenced = hostRefs.SelectMany(refs => refs).ToHashSet(StringComparer.Ordinal);
        var features = all.Where(item => item.Kind == CreatePossibilities && item.EntityId is not null &&
            referenced.Contains(item.EntityId!.Value.Value)).ToArray();
        var featureIds = features.Select(item => item.EntityId!.Value.Value).ToHashSet(StringComparer.Ordinal);
        var featureOps = features.Select(item => item.OperationId).ToHashSet();
        var featureHosts = hostRefs.Count(refs => refs.Any(featureIds.Contains));
        var countedVariants = new HashSet<string>(StringComparer.Ordinal);
        long entryCount = 0, senses = 0, msas = 0, allomorphs = 0, variants = 0, processes = 0, rules = 0,
            environments = 0, featureCount = 0, other = 0;
        foreach (var item in all)
        {
            var owned = item.Target is { } owner && entries.Contains(owner.Value);
            switch (item.Kind)
            {
                case CreateEntries when item.EntityId is { } id && !variantEntries.Contains(id.Value): entryCount++; break;
                case CreateVariantRef when item.Target is { } variant && !countedVariants.Add(variant.Value): break;
                case CreateVariantRef: variants++; break;
                case "lexical/lexEntry/createSenses" when !owned: senses++; break;
                case "lexical/lexEntry/createMorphoSyntaxAnalyses" when !owned: msas++; break;
                case CreateAlternateForms when IsProcess(item): processes++; break;
                case CreateAlternateForms or "lexical/lexEntry/createLexemeForm" when !owned: allomorphs++; break;
                case "grammar/phPhonData/createPhonRules": rules++; break;
                case "grammar/phPhonData/createEnvironments": environments++; break;
                case CreatePossibilities when featureOps.Contains(item.OperationId): featureCount++; break;
                case CreatePossibilities: other++; break;
            }
        }
        return new GrammarSizeDelta(entryCount, senses, msas, allomorphs, variants, processes, rules, environments,
            featureCount, featureHosts, other);
    }

    private static bool IsHost(OperationEnvelope item)
    {
        var last = item.Kind[(item.Kind.LastIndexOf('/') + 1)..];
        return last.StartsWith("addRef", StringComparison.Ordinal) && last.EndsWith("ProdRestrict", StringComparison.Ordinal);
    }

    private static bool IsProcess(OperationEnvelope item) => item.After is { ValueKind: JsonValueKind.Object } after &&
        after.TryGetProperty("class", out var kind) && kind.ValueKind == JsonValueKind.String &&
        kind.GetString() == "MoAffixProcess";

    private static List<string> Strings(OperationEnvelope item)
    {
        var found = new List<string>();
        if (item.After is { } after) Collect(after, found);
        return found;
    }

    private static void Collect(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String: found.Add(element.GetString()!); break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray()) Collect(child, found);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Collect(property.Value, found);
                break;
        }
    }
}
