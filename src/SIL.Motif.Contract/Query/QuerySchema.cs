using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIL.Motif.Contract.Query;

/// <summary>The JSON Schema of <c>motif.query.v2</c>, generated from <see cref="QueryDocument"/> and the op and source lists.</summary>
public static class QuerySchema
{
    /// <summary>The schema's <c>$id</c>.</summary>
    public const string Id = "https://motif.sil.org/schemas/query.schema.json";

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["version"] = "The query language version.",
        ["from"] = "The source the rows come from.",
        ["where"] = "The row filter; omit to select every row.",
        ["side"] = "Read the forms source as of the Baseline (baseline) or a Proposal's Trial copy (a Proposal id) instead of the latest Trial.",
        ["sides"] = "both returns the Baseline and the Proposal named by side, side by side.",
        ["sort"] = "Order of an as-of run's rows; one key.",
        ["limit"] = "The most rows, groups or ids returned.",
        ["group"] = "Columns to group by.",
        ["minCount"] = "The fewest rows a group needs.",
        ["examples"] = "The most examples per group.",
        ["ids"] = "Return canonical ids.",
        ["rawLetters"] = "Use characters instead of the project's graphemes.",
        ["trial"] = "The Trial id the rows must come from; only the latest Trial has query rows.",
        ["fold"] = "Fold set of the fold column: comma-separated nfc, case, iy~y, o~u, e~i, c~k, from>to or project.",
        ["distinct"] = "A column of which a group needs two different values.",
        ["score"] = "Scorers run over the whole selected universe: tolerance, reliability, control, split, size.",
        ["unit"] = "The unit stem scorers count.",
        ["evidence"] = "Return the one-screen evidence composite for the grouping where or members names.",
        ["members"] = "Row ids that are the deviants of split and size, or the grouping of evidence.",
        ["exceptions"] = "Predicates that mark a universe row an exception for tolerance; all must hold.",
        ["context"] = "Predicates naming the context size weighs; all must hold.",
        ["change"] = "A hypothesised boundary change over the stems source.",
    };

    private static readonly Dictionary<string, (int Min, int Max)> Ranges = new(StringComparer.Ordinal)
    {
        ["limit"] = (1, int.MaxValue), ["minCount"] = (1, int.MaxValue), ["examples"] = (0, 1000), ["controls"] = (1, 9),
    };

    private static readonly Dictionary<string, string[]> Enums = new(StringComparer.Ordinal)
    {
        ["from"] = [.. QuerySources.All], ["sides"] = ["both"], ["unit"] = ["msa", "stem"], ["at"] = ["final", "initial"],
        ["dir"] = ["asc", "desc"],
    };

    /// <summary>The schema as indented JSON; stable across runs.</summary>
    public static string Generate()
    {
        var properties = new JsonObject
        {
            ["version"] = new JsonObject { ["const"] = QueryDocument.Version, ["description"] = Descriptions["version"] },
        };
        var required = new JsonArray("version");
        foreach (var property in typeof(QueryDocument).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var name = CamelCase(property.Name);
            properties[name] = Describe(property.PropertyType, name);
            if (property.GetCustomAttributes().Any(a => a.GetType().Name == "RequiredMemberAttribute")) required.Add(name);
        }
        var columns = new JsonObject();
        foreach (var source in QuerySources.All)
            columns[source] = new JsonArray([.. QuerySources.Columns(source).Select(c => (JsonNode?)new JsonObject { ["name"] = c.Name, ["type"] = c.Type })]);
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = Id,
            ["title"] = QueryDocument.Version,
            ["description"] = "A Motif query: a source, a boolean where over its columns, and the options motif query takes.",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties,
            ["$defs"] = Definitions(),
            ["x-columns"] = columns,
            ["x-reserved"] = new JsonArray([.. QueryOps.ReservedKinds.Select(kind => (JsonNode?)kind)]),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static JsonObject Describe(Type type, string name)
    {
        var node = Shape(Nullable.GetUnderlyingType(type) ?? type, name);
        if (Descriptions.TryGetValue(name, out var description)) node["description"] = description;
        return node;
    }

    private static JsonObject Shape(Type type, string name)
    {
        if (type == typeof(QueryExpr)) return new JsonObject { ["$ref"] = "#/$defs/expr" };
        if (type == typeof(string))
        {
            var node = new JsonObject { ["type"] = "string" };
            if (Enums.TryGetValue(name, out var values)) node["enum"] = new JsonArray([.. values.Select(v => (JsonNode?)v)]);
            return node;
        }
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(int))
        {
            var node = new JsonObject { ["type"] = "integer" };
            if (Ranges.TryGetValue(name, out var range))
            {
                node["minimum"] = range.Min;
                if (range.Max != int.MaxValue) node["maximum"] = range.Max;
            }
            return node;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var item = type.GetGenericArguments()[0];
            if (item == typeof(QueryField)) return new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/$defs/field" } };
            return new JsonObject { ["type"] = "array", ["items"] = Shape(item, name) };
        }
        if (type == typeof(QuerySortKey))
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("field"),
                ["properties"] = new JsonObject
                {
                    ["field"] = new JsonObject { ["type"] = "string", ["description"] = "A column, optionally prefixed baseline. or proposal." },
                    ["dir"] = new JsonObject { ["enum"] = new JsonArray("asc", "desc"), ["default"] = "asc" },
                },
            };
        return Record(type);
    }

    // The change object's parameters are its properties; a gained, at or controls left out take their defaults.
    private static JsonObject Record(Type type)
    {
        var properties = new JsonObject();
        foreach (var parameter in type.GetConstructors().Single().GetParameters())
        {
            var name = CamelCase(parameter.Name!);
            properties[name] = Describe(parameter.ParameterType, name);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("dropped", "affixes", "wordSets"),
            ["properties"] = properties,
        };
    }

    private static JsonObject Definitions()
    {
        var ops = new JsonArray([.. QueryOps.All.Select(op => (JsonNode?)op)]);
        JsonObject When(string[] names, JsonObject value, bool required)
        {
            var then = new JsonObject { ["properties"] = new JsonObject { ["value"] = value } };
            if (required) then["required"] = new JsonArray("value");
            return new JsonObject
            {
                ["if"] = new JsonObject
                {
                    ["required"] = new JsonArray("op"),
                    ["properties"] = new JsonObject { ["op"] = new JsonObject { ["enum"] = new JsonArray([.. names.Select(n => (JsonNode?)n)]) } },
                },
                ["then"] = then,
            };
        }
        var text = new JsonObject { ["type"] = "string" };
        var list = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["minItems"] = 1 };
        var field = new JsonObject
        {
            ["description"] = "A predicate on one column; the op must fit the column's type (x-columns): lt and gt need numeric, has needs set, regex and contains need text or set.",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("field", "op"),
            ["properties"] = new JsonObject
            {
                ["field"] = new JsonObject { ["type"] = "string", ["description"] = "A column of the source." },
                ["op"] = new JsonObject { ["enum"] = ops, ["description"] = "regex is anchored at both ends; contains searches anywhere; both are NonBacktracking and at most 256 characters." },
                ["value"] = new JsonObject { ["description"] = "A string, an integer (lt, gt), a list of strings (in, nin, has) or a boolean (exists, default true)." },
            },
            ["allOf"] = new JsonArray(
                When([QueryOps.Eq, QueryOps.Ne, QueryOps.Regex, QueryOps.Contains], text, true),
                When([QueryOps.In, QueryOps.Nin, QueryOps.Has], list, true),
                When([QueryOps.Lt, QueryOps.Gt], new JsonObject { ["type"] = "integer" }, true),
                When([QueryOps.Exists], new JsonObject { ["type"] = "boolean" }, false)),
        };
        var expr = new JsonObject
        {
            ["description"] = "One of: and, or, not, a field predicate, phon, env, or a reserved kind (parsed, refused as not yet available).",
            ["oneOf"] = new JsonArray(
                Wrapper("and", new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/$defs/expr" }, ["minItems"] = 1 }),
                Wrapper("or", new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["$ref"] = "#/$defs/expr" }, ["minItems"] = 1 }),
                Wrapper("not", new JsonObject { ["$ref"] = "#/$defs/expr" }),
                new JsonObject { ["$ref"] = "#/$defs/field" },
                Wrapper("phon", new JsonObject { ["type"] = "string", ["description"] = PatternSyntax }),
                Wrapper("env", new JsonObject { ["type"] = "string", ["description"] = "target / left _ right: the target pattern must occur between the left and right patterns, either of which may be empty. Each part uses the phon pattern syntax. The match spans the target. " + PatternSyntax }),
                new JsonObject
                {
                    ["description"] = "Reserved: " + string.Join(", ", QueryOps.ReservedKinds) + ".",
                    ["type"] = "object",
                    ["minProperties"] = 1,
                    ["maxProperties"] = 1,
                    ["propertyNames"] = new JsonObject { ["enum"] = new JsonArray([.. QueryOps.ReservedKinds.Select(k => (JsonNode?)k)]) },
                }),
        };
        return new JsonObject { ["expr"] = expr, ["field"] = field };
    }

    /// <summary>The pattern language of <c>phon</c> and <c>env</c>, as PanGloss's match op reads it.</summary>
    public const string PatternSyntax =
        "Pattern syntax, items separated by spaces: a natural-class token such as C or V (the first column of motif query --values classes); a name with spaces is written in brackets, [Nasal Stop]; " +
        "a feature bundle [+voice -nasal]; an alpha variable [αvoice]; a literal segment /p/ (a bare letter is not a segment); " +
        "( ) groups, ( )* repeats a group, # is a word edge, + is a morpheme boundary. " +
        "The grammar's surface character table segments each word, so a pattern sees the word as the grammar does; a word it cannot segment is reported as unsegmentable and matches nothing, even under not. " +
        "One alpha letter on two different features ([αvoice] _ [αson]) is refused as unsupported_construct (alphaAcrossFeatures). " +
        "A literal or a segment-list class is tested by identity until PanGloss tests it as a rule does, so spans may under-report: the result says matchMode identity and mayUnderReport, and not over such a pattern warns that absence is not proven. " +
        "+ needs words with marked boundaries, which Motif does not send yet, so it is refused on ordinary words as boundary_needs_marked_input.";

    private static JsonObject Wrapper(string key, JsonObject inner, JsonObject? extra = null)
    {
        var properties = new JsonObject { [key] = inner };
        foreach (var (name, value) in extra ?? [])
            properties[name] = value?.DeepClone();
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(key),
            ["properties"] = properties,
        };
    }
}
