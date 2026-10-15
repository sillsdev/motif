using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Commands;

/// <summary>What an id-valued intent field means, which decides whether the engine treats it as a link.</summary>
internal enum IdRole
{
    /// <summary>Names an existing object the change reads or points at.</summary>
    Reference,

    /// <summary>Names the existing object the change edits.</summary>
    Target,

    /// <summary>Carries an id that identifies nothing in the project, such as a new entity's chosen id.</summary>
    Opaque,
}

/// <summary>The kind of value an intent field carries.</summary>
internal enum IntentFieldKind
{
    /// <summary>One id string.</summary>
    Id,

    /// <summary>An array of id strings.</summary>
    Ids,

    /// <summary>One string.</summary>
    Text,

    /// <summary>A boolean.</summary>
    Flag,

    /// <summary>One string out of a fixed set.</summary>
    Choice,

    /// <summary>A number.</summary>
    Number,

    /// <summary>An object, or an array of objects, with its own shape.</summary>
    Nested,
}

/// <summary>One property an authored intent may carry.</summary>
/// <param name="Name">The camelCase wire name.</param>
/// <param name="Kind">The kind of value.</param>
/// <param name="Required">Whether the schema lists the field as required.</param>
/// <param name="Role">For id fields, what the id means.</param>
/// <param name="Values">For a choice, the accepted strings.</param>
/// <param name="Shape">For a nested field, the shape of each object.</param>
/// <param name="Many">For a nested field, whether the value is an array of objects.</param>
/// <param name="Nullable">For an id field, whether JSON null is a meaningful value, such as "no class".</param>
internal sealed record IntentField(
    string Name,
    IntentFieldKind Kind,
    bool Required,
    IdRole Role,
    IReadOnlyList<string>? Values,
    IntentShape? Shape,
    bool Many,
    bool Nullable = false);

/// <summary>Builders for the fields of an <see cref="IntentShape"/>.</summary>
internal static class Field
{
    /// <summary>One id string, or JSON null when <paramref name="nullable"/> says null means "none".</summary>
    public static IntentField Id(
        string name, IdRole role = IdRole.Reference, bool required = false, bool nullable = false) =>
        new(name, IntentFieldKind.Id, required, role, null, null, false, nullable);

    /// <summary>An array of id strings.</summary>
    public static IntentField Ids(string name, IdRole role = IdRole.Reference, bool required = false) =>
        new(name, IntentFieldKind.Ids, required, role, null, null, false);

    /// <summary>One string.</summary>
    public static IntentField Text(string name, bool required = false) =>
        new(name, IntentFieldKind.Text, required, IdRole.Opaque, null, null, false);

    /// <summary>A boolean.</summary>
    public static IntentField Flag(string name, bool required = false) =>
        new(name, IntentFieldKind.Flag, required, IdRole.Opaque, null, null, false);

    /// <summary>A number.</summary>
    public static IntentField Number(string name, bool required = false) =>
        new(name, IntentFieldKind.Number, required, IdRole.Opaque, null, null, false);

    /// <summary>One string out of <paramref name="values"/>.</summary>
    public static IntentField Choice(string name, IReadOnlyList<string> values, bool required = false) =>
        new(name, IntentFieldKind.Choice, required, IdRole.Opaque, values, null, false);

    /// <summary>An object of <paramref name="shape"/>, or an array of them when <paramref name="many"/> is set.</summary>
    public static IntentField Nested(string name, IntentShape shape, bool required = false, bool many = false) =>
        new(name, IntentFieldKind.Nested, required, IdRole.Opaque, null, shape, many);
}

/// <summary>
/// The closed set of properties an authored intent may carry, with what each one is. One shape decides which
/// properties are allowed, which ids the intent links to, and what schema an AI tool advertises, so the three
/// cannot drift apart. It says nothing about what the values mean; a parser still enforces that.
/// </summary>
internal sealed class IntentShape
{
    private static readonly Regex CamelCase = new("^[a-z][a-zA-Z0-9]*$", RegexOptions.Compiled);

    private readonly Dictionary<string, IntentField> _byName;

    private IntentShape(IReadOnlyList<IntentField> fields, IReadOnlyList<IReadOnlyList<string>> groups)
    {
        Fields = fields;
        TogetherGroups = groups;
        _byName = new Dictionary<string, IntentField>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!CamelCase.IsMatch(field.Name))
                throw new ArgumentException($"Intent field '{field.Name}' must be camelCase.", nameof(fields));
            if (!_byName.TryAdd(field.Name, field))
                throw new ArgumentException($"Intent field '{field.Name}' is declared twice.", nameof(fields));
        }
        foreach (var group in groups)
            foreach (var name in group)
                if (!_byName.ContainsKey(name))
                    throw new ArgumentException($"Together names the unknown field '{name}'.", nameof(groups));
    }

    /// <summary>The fields, in declaration order.</summary>
    public IReadOnlyList<IntentField> Fields { get; }

    /// <summary>Groups of field names that are given together or not at all.</summary>
    public IReadOnlyList<IReadOnlyList<string>> TogetherGroups { get; }

    /// <summary>A shape of exactly these fields.</summary>
    public static IntentShape Of(params IntentField[] fields) => new(fields, []);

    /// <summary>The same shape, with the named fields required to appear together or not at all.</summary>
    public IntentShape Together(params string[] names) => new(Fields, [.. TogetherGroups, names]);

    /// <summary>
    /// Throws <see cref="ContractParseException"/> for a property the shape does not declare, at any depth, and
    /// for a partly given Together group.
    /// Values are not checked here; the parser that follows does that.
    /// </summary>
    public void RequireClosed(JsonElement authored, string composer) => RequireClosed(authored, composer, composer);

    private void RequireClosed(JsonElement authored, string composer, string path)
    {
        if (authored.ValueKind != JsonValueKind.Object)
            return;
        foreach (var property in authored.EnumerateObject())
            if (!_byName.ContainsKey(property.Name))
                throw new ContractParseException($"'{path}': unknown property '{property.Name}'.");
        foreach (var group in TogetherGroups)
        {
            var present = group.Where(name => authored.TryGetProperty(name, out _)).ToArray();
            if (present.Length != 0 && present.Length != group.Count)
                throw new ContractParseException(
                    $"'{path}': {string.Join(", ", group.Select(name => $"'{name}'"))} go together.");
        }
        foreach (var field in Fields)
        {
            if (field.Kind != IntentFieldKind.Nested || !authored.TryGetProperty(field.Name, out var value))
                continue;
            if (field.Many && value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray())
                    field.Shape!.RequireClosed(item, composer, $"{path}.{field.Name}");
            else if (!field.Many)
                field.Shape!.RequireClosed(value, composer, $"{path}.{field.Name}");
        }
    }

    /// <summary>
    /// The ids the intent links to: every well-formed id in a Reference or Target field, at any depth. An id
    /// that does not parse is skipped, because the intent's parser reports it with its own message.
    /// </summary>
    public IEnumerable<CanonicalId> References(JsonElement authored)
    {
        if (authored.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (var field in Fields)
        {
            if (!authored.TryGetProperty(field.Name, out var value))
                continue;
            switch (field.Kind)
            {
                case IntentFieldKind.Id when field.Role != IdRole.Opaque:
                    if (value.ValueKind == JsonValueKind.String && CanonicalId.TryParse(value.GetString(), out var id))
                        yield return id;
                    break;
                case IntentFieldKind.Ids when field.Role != IdRole.Opaque && value.ValueKind == JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String && CanonicalId.TryParse(item.GetString(), out var each))
                            yield return each;
                    break;
                case IntentFieldKind.Nested:
                    var items = field.Many && value.ValueKind == JsonValueKind.Array
                        ? value.EnumerateArray().ToArray()
                        : [value];
                    foreach (var item in items)
                        foreach (var nested in field.Shape!.References(item))
                            yield return nested;
                    break;
            }
        }
    }

    /// <summary>The JSON Schema an AI tool advertises: closed at every depth, with required fields listed.</summary>
    public JsonObject ToJsonSchema()
    {
        var properties = new JsonObject();
        foreach (var field in Fields)
            properties[field.Name] = FieldSchema(field);
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        var required = Fields.Where(field => field.Required).Select(field => (JsonNode)field.Name).ToArray();
        if (required.Length > 0)
            schema["required"] = new JsonArray(required);
        if (TogetherGroups.Count > 0)
        {
            var dependent = new JsonObject();
            foreach (var group in TogetherGroups)
                foreach (var name in group)
                    dependent[name] = new JsonArray(group.Where(other => other != name)
                        .Select(other => (JsonNode)other).ToArray());
            schema["dependentRequired"] = dependent;
        }
        schema["additionalProperties"] = false;
        return schema;
    }

    private static JsonNode FieldSchema(IntentField field) => field.Kind switch
    {
        IntentFieldKind.Id when field.Nullable => new JsonObject { ["type"] = new JsonArray("string", "null") },
        IntentFieldKind.Id or IntentFieldKind.Text => new JsonObject { ["type"] = "string" },
        IntentFieldKind.Ids => new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        IntentFieldKind.Flag => new JsonObject { ["type"] = "boolean" },
        IntentFieldKind.Number => new JsonObject { ["type"] = "number" },
        IntentFieldKind.Choice => new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray(field.Values!.Select(value => (JsonNode)value).ToArray()),
        },
        _ => field.Many
            ? new JsonObject { ["type"] = "array", ["items"] = field.Shape!.ToJsonSchema() }
            : field.Shape!.ToJsonSchema(),
    };
}
