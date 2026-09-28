using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace SIL.Motif.Tests.Samples;

internal static class SampleJsonSchemaValidator
{
    public static void AssertValid(JsonElement instance, JsonElement schema, JsonElement? rootSchema = null)
    {
        var errors = Validate(instance, schema, rootSchema);
        if (errors.Count > 0)
            throw new XunitException(string.Join(Environment.NewLine, errors));
    }

    public static IReadOnlyList<string> Validate(JsonElement instance, JsonElement schema, JsonElement? rootSchema = null)
    {
        var errors = new List<string>();
        Validate(instance, schema, rootSchema ?? schema, "$", errors);
        return errors;
    }

    private static void Validate(
        JsonElement instance, JsonElement schema, JsonElement rootSchema, string path, List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            Validate(instance, Resolve(rootSchema, reference.GetString()!), rootSchema, path, errors);
            return;
        }

        if (schema.TryGetProperty("oneOf", out var alternatives))
        {
            var matches = alternatives.EnumerateArray()
                .Count(alternative => Validate(instance, alternative, rootSchema).Count == 0);
            if (matches != 1)
                errors.Add($"{path}: expected exactly one matching schema, found {matches}");
            return;
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(instance, type.GetString()!))
        {
            errors.Add($"{path}: expected {type.GetString()}");
            return;
        }

        if (schema.TryGetProperty("const", out var constant) && !SameValue(instance, constant))
            errors.Add($"{path}: expected {constant.GetRawText()}");

        if (schema.TryGetProperty("enum", out var choices) &&
            !choices.EnumerateArray().Any(choice => SameValue(instance, choice)))
            errors.Add($"{path}: value is not in the allowed set");

        if (instance.ValueKind == JsonValueKind.Object)
            ValidateObject(instance, schema, rootSchema, path, errors);
        if (instance.ValueKind == JsonValueKind.Array)
            ValidateArray(instance, schema, rootSchema, path, errors);
        if (instance.ValueKind == JsonValueKind.String)
            ValidateString(instance, schema, path, errors);
        if (instance.ValueKind == JsonValueKind.Number)
            ValidateNumber(instance, schema, path, errors);
    }

    private static void ValidateObject(
        JsonElement instance, JsonElement schema, JsonElement rootSchema, string path, List<string> errors)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(value => value.GetString()!))
            {
                if (!instance.TryGetProperty(name, out _))
                    errors.Add($"{path}: missing required property '{name}'");
            }
        }

        var properties = schema.TryGetProperty("properties", out var definedProperties)
            ? definedProperties
            : default;
        foreach (var property in instance.EnumerateObject())
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                Validate(property.Value, propertySchema, rootSchema, Join(path, property.Name), errors);
            }
            else if (schema.TryGetProperty("additionalProperties", out var additional) &&
                     additional.ValueKind == JsonValueKind.False)
            {
                errors.Add($"{Join(path, property.Name)}: additional property is not allowed");
            }
        }
    }

    private static void ValidateArray(
        JsonElement instance, JsonElement schema, JsonElement rootSchema, string path, List<string> errors)
    {
        var items = instance.EnumerateArray().ToArray();
        if (schema.TryGetProperty("minItems", out var minimum) && items.Length < minimum.GetInt32())
            errors.Add($"{path}: expected at least {minimum.GetInt32()} items");
        if (schema.TryGetProperty("items", out var itemSchema))
        {
            for (var index = 0; index < items.Length; index++)
                Validate(items[index], itemSchema, rootSchema, $"{path}/{index}", errors);
        }
    }

    private static void ValidateString(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        var value = instance.GetString()!;
        if (schema.TryGetProperty("minLength", out var minimum) && value.Length < minimum.GetInt32())
            errors.Add($"{path}: expected at least {minimum.GetInt32()} characters");
        if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(value, pattern.GetString()!))
            errors.Add($"{path}: value does not match the required pattern");
    }

    private static void ValidateNumber(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        var value = instance.GetDouble();
        if (schema.TryGetProperty("minimum", out var minimum) && value < minimum.GetDouble())
            errors.Add($"{path}: value is below the minimum");
        if (schema.TryGetProperty("maximum", out var maximum) && value > maximum.GetDouble())
            errors.Add($"{path}: value is above the maximum");
    }

    private static bool MatchesType(JsonElement instance, string type) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "number" => instance.ValueKind == JsonValueKind.Number,
        _ => false
    };

    private static bool SameValue(JsonElement left, JsonElement right) =>
        left.ValueKind == right.ValueKind && left.GetRawText() == right.GetRawText();

    private static string Join(string path, string property) =>
        $"{path}/{property.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        if (!reference.StartsWith("#", StringComparison.Ordinal))
            throw new InvalidOperationException($"Only local schema references are supported: {reference}");

        var current = root;
        foreach (var segment in reference[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var property = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (!current.TryGetProperty(property, out var next))
                throw new InvalidOperationException($"Schema reference '{reference}' cannot resolve '{property}'.");
            current = next;
        }
        return current;
    }
}
