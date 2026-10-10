using System.Text.Json.Nodes;

namespace SIL.Motif.Mcp;

/// <summary>Builds the small JSON Schema objects a tool's input is declared with.</summary>
internal static class Schema
{
    public static JsonObject Object(params (string Name, JsonObject Schema, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in properties)
        {
            props[name] = schema;
            if (isRequired) required.Add(name);
        }
        var result = new JsonObject { ["type"] = "object", ["properties"] = props };
        if (required.Count > 0) result["required"] = required;
        result["additionalProperties"] = false;
        return result;
    }

    public static JsonObject String(string description, params string[] choices)
    {
        var schema = new JsonObject { ["type"] = "string", ["description"] = description };
        if (choices.Length > 0) schema["enum"] = new JsonArray(choices.Select(c => (JsonNode)c).ToArray());
        return schema;
    }

    public static JsonObject Integer(string description, int minimum, int maximum) => new()
    {
        ["type"] = "integer", ["description"] = description, ["minimum"] = minimum, ["maximum"] = maximum,
    };

    public static JsonObject Boolean(string description) => new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject Array(string description, JsonObject items) => new()
    {
        ["type"] = "array", ["items"] = items, ["description"] = description,
    };

    internal static JsonObject FeatureValues() => Array("Closed phonological feature/value pairs; copy ids from motif_grammar.",
        Object(("feature", String("Closed feature id in the phonological feature system."), true),
            ("value", String("Symbolic value belonging to that feature."), true)));

    internal static JsonObject Contexts() => Array("Context items in linguistic order, from the outer left to outer right. Each names exactly one reference.",
        Object(("phoneme", String("Phoneme id in the first phoneme set."), false),
            ("naturalClass", String("Natural class id with a unique abbreviation."), false),
            ("boundary", String("Word edge or declared morpheme boundary.", "word", "morpheme"), false),
            ("boundaryMarker", String("Stored boundary-marker id. Rule contexts preserve its literal identity, even when its code is #."), false)));

    public static JsonObject StringArray(string description) => new()
    {
        ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description,
    };

    /// <summary>The <c>detail</c> and <c>limit</c> properties every tool with a verbosity choice shares.</summary>
    public static (string, JsonObject, bool)[] Verbosity() =>
    [
        ("detail", String("'concise' (default) drops empty fields and long lists; 'detailed' returns everything.",
            "concise", "detailed"), false),
        ("limit", Integer("The most list items to return in concise mode. Default 25.", 1, 200), false),
    ];
}
