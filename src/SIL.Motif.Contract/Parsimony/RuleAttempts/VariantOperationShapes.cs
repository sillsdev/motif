using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Contract.Parsimony.RuleAttempts;

/// <summary>Reads the variants a Proposal's operations add, as shapes, from the operations alone.</summary>
/// <remarks>Cost is O(ops): one pass to index entries, forms and environments, one pass over the variant links.</remarks>
public static class VariantOperationShapes
{
    private const string CreateEntries = "lexical/lexDb/createEntries";
    private const string CreateLexemeForm = "lexical/lexEntry/createLexemeForm";
    private const string CreateVariantRef = "lexical/lexEntry/createVariantRef";

    /// <summary>One shape per <c>createVariantRef</c> whose main and variant forms are both known.</summary>
    /// <param name="operations">The Proposal's operations.</param>
    /// <param name="formOf">The lexeme form of an entry or sense already in the project, by canonical suffix, or null when unknown.</param>
    public static IReadOnlyList<AddedObjectShape> FromOperations(IEnumerable<OperationEnvelope> operations,
        Func<string, string?> formOf)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(formOf);
        var all = operations.ToArray();
        var formsByEntry = new Dictionary<string, string>(StringComparer.Ordinal);
        var entryOfForm = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in all.Where(o => o.Kind == CreateLexemeForm && o.Target is not null))
        {
            var entry = Key(item.Target!.Value);
            if (item.After is { ValueKind: JsonValueKind.Object } after && after.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String) formsByEntry[entry] = text.GetString()!;
            if (item.EntityId is { } formId) entryOfForm[Key(formId)] = entry;
        }
        var environments = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var item in all.Where(o => o.Kind.EndsWith("addRefPhoneEnv", StringComparison.Ordinal) && o.Target is not null))
        {
            if (!entryOfForm.TryGetValue(Key(item.Target!.Value), out var entry)) continue;
            if (item.After is { ValueKind: JsonValueKind.Object } after && after.TryGetProperty("member", out var member) &&
                member.ValueKind == JsonValueKind.String)
                (environments.TryGetValue(entry, out var list) ? list : environments[entry] = []).Add(Key(member.GetString()!));
        }
        string? Form(string id) => formsByEntry.TryGetValue(id, out var found) ? found : formOf(id);
        var shapes = new List<AddedObjectShape>();
        foreach (var item in all.Where(o => o.Kind == CreateVariantRef && o.Target is not null))
        {
            if (item.After is not { ValueKind: JsonValueKind.Object } after) continue;
            var component = Text(after, "component") is { } c ? Key(c) : null;
            var variantType = Text(after, "variantType");
            var variant = Key(item.Target!.Value);
            if (component is null || variantType is null) continue;
            var mainForm = Form(component);
            var variantForm = Form(variant);
            if (mainForm is null || variantForm is null) continue;
            shapes.Add(new AddedObjectShape(variant, variantType,
                environments.TryGetValue(variant, out var env) ? env : [], [],
                StringCorrespondence.Of(mainForm, variantForm)));
        }
        return shapes;
    }

    private static string Key(CanonicalId id) => id.CanonicalSuffix;

    private static string Key(string text) => CanonicalId.TryParse(text, out var id) ? id.CanonicalSuffix : text;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
