using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Contract.Query;

/// <summary>One sort key of a document: a column, optionally prefixed <c>baseline.</c> or <c>proposal.</c>.</summary>
/// <param name="Field">The column.</param>
/// <param name="Descending">Whether the key sorts high to low.</param>
public sealed record QuerySortKey(string Field, bool Descending);

/// <summary>A hypothesised change, as a document carries it.</summary>
/// <param name="Dropped">The letters the stem edge has today.</param>
/// <param name="Gained">The letters the hypothesis puts there; may be empty.</param>
/// <param name="At"><c>final</c> or <c>initial</c>.</param>
/// <param name="Affixes">Affixes the change fires before or after, without hyphens.</param>
/// <param name="WordSets">Attested word set ids the evidence looks forms up in.</param>
/// <param name="Discovery">Word sets the hypothesis was mined from.</param>
/// <param name="HeldOut">Held-out word sets.</param>
/// <param name="Controls">Control letters tried, 1 to 9.</param>
/// <param name="ControlLetters">Control letters to use instead of the chosen ones.</param>
public sealed record QueryChangeDocument(string Dropped, string Gained, string At, IReadOnlyList<string> Affixes,
    IReadOnlyList<string> WordSets, IReadOnlyList<string>? Discovery, IReadOnlyList<string>? HeldOut, int Controls,
    IReadOnlyList<string>? ControlLetters);

/// <summary>A <c>motif.query.v2</c> query: the canonical form every text and builder view is a rendering of.</summary>
public sealed record QueryDocument
{
    /// <summary>The version string a document carries.</summary>
    public const string Version = "motif.query.v2";

    /// <summary>The source: forms, stems, uses or objects.</summary>
    public required string From { get; init; }
    /// <summary>The row filter; null selects every row.</summary>
    public QueryExpr? Where { get; init; }
    /// <summary>The revision read as of: <c>baseline</c> or a Proposal id; null reads the latest Trial.</summary>
    public string? Side { get; init; }
    /// <summary>Both sides side by side: <c>both</c> or null.</summary>
    public string? Sides { get; init; }
    /// <summary>The order of an as-of run's rows.</summary>
    public IReadOnlyList<QuerySortKey>? Sort { get; init; }
    /// <summary>The most rows, groups or ids returned.</summary>
    public int? Limit { get; init; }
    /// <summary>The columns to group by.</summary>
    public IReadOnlyList<string> Group { get; init; } = [];
    /// <summary>The fewest rows a group needs.</summary>
    public int? MinCount { get; init; }
    /// <summary>The most examples per group, or rows of a scored query.</summary>
    public int? Examples { get; init; }
    /// <summary>Whether to return canonical ids.</summary>
    public bool Ids { get; init; }
    /// <summary>Whether to use characters instead of graphemes.</summary>
    public bool RawLetters { get; init; }
    /// <summary>The Trial id the rows must come from.</summary>
    public string? Trial { get; init; }
    /// <summary>The fold set of the <c>fold</c> column.</summary>
    public string? Fold { get; init; }
    /// <summary>A column of which a group needs two different values.</summary>
    public string? Distinct { get; init; }
    /// <summary>The scorers run over the whole selected universe.</summary>
    public IReadOnlyList<string>? Score { get; init; }
    /// <summary>The unit stem scorers count: <c>msa</c> or <c>stem</c>.</summary>
    public string? Unit { get; init; }
    /// <summary>Whether to return the one-screen evidence composite.</summary>
    public bool Evidence { get; init; }
    /// <summary>Row ids that are the deviants of split and size, or the grouping of evidence.</summary>
    public IReadOnlyList<string>? Members { get; init; }
    /// <summary>Predicates that mark a universe row an exception for tolerance; all must hold.</summary>
    public IReadOnlyList<QueryField>? Exceptions { get; init; }
    /// <summary>Predicates naming the context size weighs; all must hold.</summary>
    public IReadOnlyList<QueryField>? Context { get; init; }
    /// <summary>A hypothesised change over the stems source.</summary>
    public QueryChangeDocument? Change { get; init; }
    /// <summary>How <c>phon</c> and <c>env</c> test a segment: <c>engine</c> (the default) or <c>identity</c>; null is the default.</summary>
    public string? SegmentTest { get; init; }

    /// <summary>Parses a document, refusing a malformed one with <see cref="RefusalCodes.QueryDocumentInvalid"/> and a reserved kind with <see cref="RefusalCodes.QueryNotYetAvailable"/>.</summary>
    public static bool TryParse(string json, [NotNullWhen(true)] out QueryDocument? document, [NotNullWhen(false)] out Refusal? refusal)
    {
        try
        {
            document = QueryDocumentJson.Read(json);
            refusal = null;
            return true;
        }
        catch (QueryRefusalException error)
        {
            document = null;
            refusal = error.Refusal;
            return false;
        }
    }

    /// <summary>Reads a document from a JSON object a caller already parsed, with the same refusals as <see cref="TryParse(string, out QueryDocument?, out Refusal?)"/>.</summary>
    public static bool TryParse(JsonObject node, [NotNullWhen(true)] out QueryDocument? document, [NotNullWhen(false)] out Refusal? refusal)
    {
        try
        {
            document = QueryDocumentJson.ReadObject(node);
            refusal = null;
            return true;
        }
        catch (QueryRefusalException error)
        {
            document = null;
            refusal = error.Refusal;
            return false;
        }
    }

    /// <summary>The canonical JSON of the document: properties in the schema's order, defaults omitted.</summary>
    public string ToJson() => QueryDocumentJson.Write(this);

    /// <summary>Lowers the document to the record the engine runs; a reserved kind is refused here.</summary>
    public QueryRecord ToRecord()
    {
        QueryDocumentJson.RejectReserved(Where);
        var asOf = Side;
        var filter = Where;
        var where = new List<QueryCondition>();
        if (asOf is not null && filter is not null && QueryExprTools.TryLower(filter, out var lowered))
        {
            where = lowered;
            filter = null;
        }
        if (Sort is { Count: > 1 })
            throw QueryRefusalException.DocumentInvalid("The engine sorts by one key; give one entry in sort.", "sort");
        var sort = Sort is { Count: 1 } ? Sort[0].Field + (Sort[0].Descending ? ":desc" : "") : null;
        static List<QueryCondition> Conditions(IReadOnlyList<QueryField>? fields, string path) => [.. (fields ?? []).Select(field =>
            QueryExprTools.ToCondition(field) ?? throw QueryRefusalException.DocumentInvalid(
                "This list takes only eq, ne, in, has, lt, gt and contains predicates.", path))];
        var exceptions = Conditions(Exceptions, "exceptions");
        var context = Conditions(Context, "context");
        QueryHypothesis? hypothesis = Change is null ? null : new QueryHypothesis(Change.Dropped, Change.Gained, Change.At, Change.Affixes,
            Change.WordSets, Change.Discovery, Change.HeldOut, Change.Controls, Change.ControlLetters);
        return new QueryRecord(From, where, Group, MinCount ?? 1, Examples ?? 5, Ids, RawLetters, Trial, Fold, Distinct, hypothesis,
            Score, exceptions.Count == 0 ? null : exceptions, Unit, Members, context.Count == 0 ? null : context, Evidence, asOf,
            Sides, sort, filter, Limit, SegmentTest);
    }
}

internal static class QueryDocumentJson
{
    private static readonly string[] Names =
    [
        "version", "from", "where", "side", "sides", "sort", "limit", "group", "minCount", "examples", "ids", "rawLetters", "trial",
        "fold", "distinct", "score", "unit", "evidence", "members", "exceptions", "context", "change", "segmentTest",
    ];

    private static readonly string[] ChangeNames =
        ["dropped", "gained", "at", "affixes", "wordSets", "discovery", "heldOut", "controls", "controlLetters"];

    public static QueryDocument Read(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = false }) as JsonObject
            ?? throw QueryRefusalException.DocumentInvalid("A query is one JSON object."); }
        catch (JsonException error) { throw QueryRefusalException.DocumentInvalid("The query is not valid JSON: " + error.Message); }
        return ReadObject(root);
    }

    public static QueryDocument ReadObject(JsonObject root)
    {
        Known(root, Names, "");
        if (!root.TryGetPropertyValue("version", out var version) || version?.GetValueKind() != JsonValueKind.String ||
            version.GetValue<string>() != QueryDocument.Version)
            throw QueryRefusalException.DocumentInvalid($"A query's version is \"{QueryDocument.Version}\".", "version");
        var from = Text(root, "from") ?? throw QueryRefusalException.DocumentInvalid("A query names its source in from.", "from");
        if (!QuerySources.All.Contains(from))
            throw QueryRefusalException.DocumentInvalid("from is forms, stems, uses or objects.", "from");
        QueryExpr? where = null;
        if (root.TryGetPropertyValue("where", out var whereNode) && whereNode is not null) where = ReadExpr(whereNode, "where");
        List<QuerySortKey>? sort = null;
        if (root.TryGetPropertyValue("sort", out var sortNode) && sortNode is not null)
        {
            if (sortNode is not JsonArray keys) throw QueryRefusalException.DocumentInvalid("sort is a list of {field, dir}.", "sort");
            sort = [];
            for (var i = 0; i < keys.Count; i++)
            {
                var path = $"sort[{i}]";
                if (keys[i] is not JsonObject key) throw QueryRefusalException.DocumentInvalid("A sort key is {field, dir}.", path);
                Known(key, ["field", "dir"], path);
                var dir = Text(key, "dir", path) ?? "asc";
                if (dir is not ("asc" or "desc")) throw QueryRefusalException.DocumentInvalid("dir is asc or desc.", path + ".dir");
                sort.Add(new QuerySortKey(Text(key, "field", path) ?? throw QueryRefusalException.DocumentInvalid("A sort key needs a field.", path), dir == "desc"));
            }
        }
        QueryChangeDocument? change = null;
        if (root.TryGetPropertyValue("change", out var changeNode) && changeNode is not null)
        {
            if (changeNode is not JsonObject changeObject) throw QueryRefusalException.DocumentInvalid("change is an object.", "change");
            Known(changeObject, ChangeNames, "change");
            change = new QueryChangeDocument(Text(changeObject, "dropped", "change") ?? throw Missing("change", "dropped"),
                Text(changeObject, "gained", "change") ?? "", Text(changeObject, "at", "change") ?? "final",
                Strings(changeObject, "affixes", "change") ?? throw Missing("change", "affixes"),
                Strings(changeObject, "wordSets", "change") ?? throw Missing("change", "wordSets"),
                Strings(changeObject, "discovery", "change"), Strings(changeObject, "heldOut", "change"),
                Integer(changeObject, "controls", "change", 1, 9) ?? 5, Strings(changeObject, "controlLetters", "change"));
        }
        return new QueryDocument
        {
            From = from,
            Where = where,
            Side = Text(root, "side"),
            Sides = Text(root, "sides"),
            Sort = sort,
            Limit = Integer(root, "limit", "", 1, int.MaxValue),
            Group = Strings(root, "group", "") ?? [],
            MinCount = Integer(root, "minCount", "", 1, int.MaxValue),
            Examples = Integer(root, "examples", "", 0, 1000),
            Ids = Bool(root, "ids"),
            RawLetters = Bool(root, "rawLetters"),
            Trial = Text(root, "trial"),
            Fold = Text(root, "fold"),
            Distinct = Text(root, "distinct"),
            Score = Strings(root, "score", ""),
            Unit = Text(root, "unit"),
            Evidence = Bool(root, "evidence"),
            Members = Strings(root, "members", ""),
            Exceptions = Fields(root, "exceptions"),
            Context = Fields(root, "context"),
            Change = change,
            SegmentTest = SegmentTestOf(root),
        };
    }

    private static string? SegmentTestOf(JsonObject root)
    {
        var text = Text(root, "segmentTest");
        if (text is not null) PhonSegmentTest.Normalize(text);
        return text;
    }

    public static void RejectReserved(QueryExpr? expr)
    {
        switch (expr)
        {
            case QueryReserved reserved: throw QueryRefusalException.NotYetAvailable(reserved.Kind);
            case QueryAnd and: foreach (var item in and.Items) RejectReserved(item); break;
            case QueryOr or: foreach (var item in or.Items) RejectReserved(item); break;
            case QueryNot not: RejectReserved(not.Item); break;
        }
    }

    private static QueryRefusalException Missing(string parent, string name) =>
        QueryRefusalException.DocumentInvalid("This property is required.", parent + "." + name);

    private static void Known(JsonObject node, string[] names, string path)
    {
        foreach (var (name, _) in node)
            if (!names.Contains(name))
                throw QueryRefusalException.DocumentInvalid($"Unknown property '{name}'.", path.Length == 0 ? name : path + "." + name);
    }

    private static string? Text(JsonObject node, string name, string path = "")
    {
        if (!node.TryGetPropertyValue(name, out var value) || value is null) return null;
        return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>()
            : throw QueryRefusalException.DocumentInvalid("This property is a string.", Path(path, name));
    }

    private static string Path(string path, string name) => path.Length == 0 ? name : path + "." + name;

    private static bool Bool(JsonObject node, string name)
    {
        if (!node.TryGetPropertyValue(name, out var value) || value is null) return false;
        return value.GetValueKind() is var kind && kind is JsonValueKind.True or JsonValueKind.False ? value.GetValue<bool>()
            : throw QueryRefusalException.DocumentInvalid("This property is true or false.", name);
    }

    private static int? Integer(JsonObject node, string name, string path, int min, int max)
    {
        if (!node.TryGetPropertyValue(name, out var value) || value is null) return null;
        if (value.GetValueKind() != JsonValueKind.Number || !value.AsValue().TryGetValue<long>(out var number) || number < min || number > max)
            throw QueryRefusalException.DocumentInvalid($"This property is a whole number from {min} to {max}.", Path(path, name));
        return (int)number;
    }

    private static List<string>? Strings(JsonObject node, string name, string path)
    {
        if (!node.TryGetPropertyValue(name, out var value) || value is null) return null;
        if (value is not JsonArray array || array.Any(item => item is null || item.GetValueKind() != JsonValueKind.String))
            throw QueryRefusalException.DocumentInvalid("This property is a list of strings.", Path(path, name));
        return [.. array.Select(item => item!.GetValue<string>())];
    }

    private static List<QueryField>? Fields(JsonObject node, string name)
    {
        if (!node.TryGetPropertyValue(name, out var value) || value is null) return null;
        if (value is not JsonArray array) throw QueryRefusalException.DocumentInvalid("This property is a list of field predicates.", name);
        var result = new List<QueryField>();
        for (var i = 0; i < array.Count; i++)
            result.Add(ReadExpr(array[i], $"{name}[{i}]") as QueryField ??
                throw QueryRefusalException.DocumentInvalid("Only field predicates are allowed here.", $"{name}[{i}]"));
        return result;
    }

    public static QueryExpr ReadExpr(JsonNode? node, string path)
    {
        if (node is not JsonObject obj) throw QueryRefusalException.DocumentInvalid("An expression is an object.", path);
        var keys = obj.Select(pair => pair.Key).ToArray();
        if (keys.Length == 0) throw QueryRefusalException.DocumentInvalid("An expression is empty.", path);
        if (keys.Contains("field"))
        {
            Known(obj, ["field", "op", "value"], path);
            var field = Text(obj, "field", path) ?? "";
            var op = Text(obj, "op", path) ?? throw QueryRefusalException.DocumentInvalid("A field predicate needs an op.", path + ".op");
            if (!QueryOps.All.Contains(op))
                throw QueryRefusalException.DocumentInvalid($"op is one of {string.Join(", ", QueryOps.All)}.", path + ".op");
            obj.TryGetPropertyValue("value", out var valueNode);
            return new QueryField(field, op, ReadValue(op, valueNode, path + ".value"));
        }
        if (keys.Contains("phon"))
        {
            Known(obj, ["phon"], path);
            var pattern = Text(obj, "phon", path) ?? throw QueryRefusalException.DocumentInvalid("phon is a pattern string.", path + ".phon");
            PhonPattern.CompilePhon(pattern);
            return new QueryPhon(pattern.Trim());
        }
        if (keys.Length != 1)
            throw QueryRefusalException.DocumentInvalid("An expression has exactly one of and, or, not, a field predicate, phon, env or a reserved kind.", path);
        var key = keys[0];
        var inner = obj[key];
        switch (key)
        {
            case "and" or "or":
                if (inner is not JsonArray items || items.Count == 0)
                    throw QueryRefusalException.DocumentInvalid($"{key} is a non-empty list of expressions.", path + "." + key);
                var children = items.Select((item, i) => ReadExpr(item, $"{path}.{key}[{i}]")).ToList();
                if (children.Count == 1) return children[0];
                return key == "and" ? new QueryAnd(children) : new QueryOr(children);
            case "not":
                return new QueryNot(ReadExpr(inner, path + ".not"));
            case "realizes":
                return ReadRealizes(inner, path + ".realizes");
            case "only-via":
                return ReadOnlyVia(inner, path + ".only-via");
            case "morph":
                if (inner is not JsonValue || inner.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(inner.GetValue<string>()))
                    throw QueryRefusalException.DocumentInvalid("morph is an item string such as entry:<guid>, root:<form> or CAUS.", path + ".morph");
                return new QueryMorphIs(inner.GetValue<string>().Trim());
            case "seq":
                return ReadSeq(inner, path + ".seq");
            case "env":
                if (inner is not JsonValue || inner.GetValueKind() != JsonValueKind.String)
                    throw QueryRefusalException.DocumentInvalid("env is an environment string, <target> / <left> _ <right>.", path + ".env");
                var environment = inner.GetValue<string>();
                PhonPattern.CompileEnv(environment);
                return new QueryEnv(environment.Trim());
            default:
                if (QueryOps.Reserved.Contains(key)) return new QueryReserved(key, inner?.ToJsonString() ?? "null");
                throw QueryRefusalException.DocumentInvalid($"'{key}' is not an expression kind.", path);
        }
    }

    private static QueryMorphSeq ReadSeq(JsonNode? node, string path)
    {
        const string shape = "seq is {items: [<item>, <item>, ...], adjacent?: bool}: at least two items, each a string as for morph.";
        if (node is not JsonObject body) throw QueryRefusalException.DocumentInvalid(shape, path);
        Known(body, ["items", "adjacent"], path);
        if (body["items"] is not JsonArray array || array.Count < 2 ||
            array.Any(item => item is null || item.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetValue<string>())))
            throw QueryRefusalException.DocumentInvalid(shape, path + ".items");
        var adjacent = false;
        if (body["adjacent"] is { } flag)
        {
            if (flag.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) throw QueryRefusalException.DocumentInvalid("adjacent is true or false.", path + ".adjacent");
            adjacent = flag.GetValue<bool>();
        }
        return new QueryMorphSeq([.. array.Select(item => item!.GetValue<string>().Trim())], adjacent);
    }

    private static QueryOnlyVia ReadOnlyVia(JsonNode? node, string path)
    {
        const string shape = "only-via is {entry: <guid>}, {msa: <guid>} or {form: <guid>}: the words whose every reading uses that morpheme.";
        if (node is not JsonObject body || body.Count != 1) throw QueryRefusalException.DocumentInvalid(shape, path);
        var kind = body.First().Key;
        var id = Text(body, kind, path);
        if (!Readings.ReadingTarget.TryParse(kind + ":" + id, out var target)) throw QueryRefusalException.DocumentInvalid(shape, path + "." + kind);
        return new QueryOnlyVia(target.Kind, target.Id);
    }

    private static QueryRealizes ReadRealizes(JsonNode? node, string path)
    {
        const string shape = "realizes is {morpheme, tier}: the morpheme as entry:msa, an id or a form, and one of attested, possible, generated.";
        if (node is not JsonObject body) throw QueryRefusalException.DocumentInvalid(shape, path);
        Known(body, ["morpheme", "tier"], path);
        var morpheme = Text(body, "morpheme", path);
        var tier = Text(body, "tier", path);
        if (string.IsNullOrWhiteSpace(morpheme)) throw QueryRefusalException.DocumentInvalid(shape, path + ".morpheme");
        if (tier is null || !RealizesTiers.All.Contains(tier))
            throw QueryRefusalException.DocumentInvalid($"tier is one of {string.Join(", ", RealizesTiers.All)}.", path + ".tier");
        return new QueryRealizes(morpheme.Trim(), tier);
    }

    private static object ReadValue(string op, JsonNode? node, string path)
    {
        if (op == QueryOps.Exists)
        {
            if (node is null) return true;
            return node.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? node.GetValue<bool>()
                : throw QueryRefusalException.DocumentInvalid("exists takes true or false.", path);
        }
        if (node is null) throw QueryRefusalException.DocumentInvalid($"The {op} op needs a value.", path);
        if (QueryOps.TakesList(op))
        {
            if (node is not JsonArray array || array.Count == 0 || array.Any(item => item is null || item.GetValueKind() != JsonValueKind.String))
                throw QueryRefusalException.DocumentInvalid($"The {op} op takes a non-empty list of strings.", path);
            return array.Select(item => item!.GetValue<string>()).ToArray();
        }
        if (op is QueryOps.Lt or QueryOps.Gt)
        {
            if (node.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<long>(out var number)) return number;
            throw QueryRefusalException.DocumentInvalid($"The {op} op takes a whole number.", path);
        }
        return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>()
            : throw QueryRefusalException.DocumentInvalid($"The {op} op takes a string.", path);
    }

    public static JsonNode WriteExpr(QueryExpr expr) => expr switch
    {
        QueryAnd and => new JsonObject { ["and"] = new JsonArray([.. and.Items.Select(WriteExpr)]) },
        QueryOr or => new JsonObject { ["or"] = new JsonArray([.. or.Items.Select(WriteExpr)]) },
        QueryNot not => new JsonObject { ["not"] = WriteExpr(not.Item) },
        QueryReserved reserved => new JsonObject { [reserved.Kind] = JsonNode.Parse(reserved.Body) },
        QueryRealizes realizes => new JsonObject { ["realizes"] = new JsonObject { ["morpheme"] = realizes.Morpheme, ["tier"] = realizes.Tier } },
        QueryOnlyVia via => new JsonObject { ["only-via"] = new JsonObject { [via.Kind] = via.Id } },
        QueryMorphIs morph => new JsonObject { ["morph"] = morph.Item },
        QueryMorphSeq seq => new JsonObject
        {
            ["seq"] = seq.Adjacent
                ? new JsonObject { ["items"] = new JsonArray([.. seq.Items.Select(item => (JsonNode?)JsonValue.Create(item))]), ["adjacent"] = true }
                : new JsonObject { ["items"] = new JsonArray([.. seq.Items.Select(item => (JsonNode?)JsonValue.Create(item))]) },
        },
        QueryPhon phon => new JsonObject { ["phon"] = phon.Pattern },
        QueryEnv env => new JsonObject { ["env"] = env.Environment },
        QueryField field => WriteField(field),
        _ => throw new ArgumentException("Unknown query expression."),
    };

    private static JsonObject WriteField(QueryField field)
    {
        var obj = new JsonObject { ["field"] = field.Field, ["op"] = field.Op };
        obj["value"] = field.Value switch
        {
            string text => JsonValue.Create(text),
            long number => JsonValue.Create(number),
            bool flag => JsonValue.Create(flag),
            IReadOnlyList<string> list => new JsonArray([.. list.Select(item => (JsonNode?)JsonValue.Create(item))]),
            _ => throw new ArgumentException("Unknown predicate value."),
        };
        return obj;
    }

    public static string Write(QueryDocument document)
    {
        var root = new JsonObject { ["version"] = QueryDocument.Version, ["from"] = document.From };
        if (document.Where is not null) root["where"] = WriteExpr(document.Where);
        if (document.Side is not null) root["side"] = document.Side;
        if (document.Sides is not null) root["sides"] = document.Sides;
        if (document.Sort is not null)
            root["sort"] = new JsonArray([.. document.Sort.Select(key => (JsonNode?)new JsonObject
                { ["field"] = key.Field, ["dir"] = key.Descending ? "desc" : "asc" })]);
        if (document.Limit is { } limit) root["limit"] = limit;
        if (document.Group.Count > 0) root["group"] = StringArray(document.Group);
        if (document.MinCount is { } min) root["minCount"] = min;
        if (document.Examples is { } examples) root["examples"] = examples;
        if (document.Ids) root["ids"] = true;
        if (document.RawLetters) root["rawLetters"] = true;
        if (document.Trial is not null) root["trial"] = document.Trial;
        if (document.Fold is not null) root["fold"] = document.Fold;
        if (document.Distinct is not null) root["distinct"] = document.Distinct;
        if (document.Score is not null) root["score"] = StringArray(document.Score);
        if (document.Unit is not null) root["unit"] = document.Unit;
        if (document.Evidence) root["evidence"] = true;
        if (document.Members is not null) root["members"] = StringArray(document.Members);
        if (document.Exceptions is not null) root["exceptions"] = new JsonArray([.. document.Exceptions.Select(WriteExpr)]);
        if (document.Context is not null) root["context"] = new JsonArray([.. document.Context.Select(WriteExpr)]);
        if (document.SegmentTest is not null) root["segmentTest"] = document.SegmentTest;
        if (document.Change is { } change)
        {
            var obj = new JsonObject { ["dropped"] = change.Dropped, ["gained"] = change.Gained, ["at"] = change.At,
                ["affixes"] = StringArray(change.Affixes), ["wordSets"] = StringArray(change.WordSets) };
            if (change.Discovery is not null) obj["discovery"] = StringArray(change.Discovery);
            if (change.HeldOut is not null) obj["heldOut"] = StringArray(change.HeldOut);
            obj["controls"] = change.Controls;
            if (change.ControlLetters is not null) obj["controlLetters"] = StringArray(change.ControlLetters);
            root["change"] = obj;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static JsonArray StringArray(IEnumerable<string> items) => new([.. items.Select(item => (JsonNode?)JsonValue.Create(item))]);
}
