using System.Text.Json;

namespace SIL.Motif.Contract.Parsimony.RuleAttempts;

/// <summary>Reads the variants a package's intents add, as shapes. Other intent verbs add no variant shape.</summary>
public static class VariantIntentShapes
{
    /// <summary>
    /// Shapes for every <c>compose-author-variant-entry</c> and <c>compose-make-variant-of</c> intent.
    /// </summary>
    /// <param name="intents">The package's intents as <c>{ "verb", "intent" }</c> objects, as in a plan file.</param>
    /// <param name="formOf">Gives the lexeme form of an entry or sense component id, or null when unknown.</param>
    public static IReadOnlyList<AddedObjectShape> FromIntents(IEnumerable<JsonElement> intents,
        Func<string, string?> formOf)
    {
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(formOf);
        var shapes = new List<AddedObjectShape>();
        var index = 0;
        foreach (var item in intents)
        {
            index++;
            var verb = item.TryGetProperty("verb", out var v) ? v.GetString() : null;
            if (!item.TryGetProperty("intent", out var intent)) continue;
            var component = Text(intent, "component");
            var variantType = Text(intent, "variantType");
            if (component is null || variantType is null) continue;
            var mainForm = formOf(component);
            string? variantForm, objectId;
            switch (verb)
            {
                case "compose-author-variant-entry":
                    variantForm = Text(intent, "form");
                    objectId = $"intent-{index}:{variantForm}";
                    break;
                case "compose-make-variant-of":
                    objectId = Text(intent, "entry");
                    variantForm = objectId is null ? null : formOf(objectId);
                    break;
                default:
                    continue;
            }
            if (mainForm is null || variantForm is null || objectId is null) continue;
            shapes.Add(new AddedObjectShape(objectId, variantType, Strings(intent, "environments"),
                Strings(intent, "features"), StringCorrespondence.Of(mainForm, variantForm)));
        }
        return shapes;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
            ? [.. p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];
}
