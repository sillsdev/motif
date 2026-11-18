using System.Globalization;
using System.Text.Json;

namespace SIL.ListCrdt;

/// <summary>Canonical JSON for the list operation domain, with decimal-string 64-bit stamps. Schema version 1.</summary>
public static class OpJson
{
    public const int SchemaVersion = 1;

    static object?[]? Node(NodeId? id) => id is { } n
        ? [n.Replica, n.Counter.ToString(CultureInfo.InvariantCulture)] : null;

    public static string ToJson(this ListOp operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Validate();
        return CanonicalJson.Write(new Dictionary<string, object?>
        {
            ["v"] = SchemaVersion,
            ["kind"] = operation.Kind switch
            {
                ListOpKind.Insert => "insert", ListOpKind.Move => "move", _ => "delete"
            },
            ["node"] = Node(operation.Id),
            ["lamport"] = operation.Lamport.ToString(CultureInfo.InvariantCulture),
            ["element"] = operation.Element,
            ["left"] = Node(operation.LeftOrigin),
            ["right"] = Node(operation.RightOrigin)
        });
    }

    public static string ToJson(IEnumerable<ListOp> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return CanonicalJson.Write(operations.Select(op => (object?)new RawJson(op.ToJson())).ToArray());
    }

    /// <summary>Parses one operation; text longer than <see cref="ListLimits.MaxOperationJsonLength"/> is refused before parsing.</summary>
    public static ListOp FromJson(string json, ListLimits? limits = null) => Parse(json, limits, false, Read);

    /// <summary>Parses an operation array; an array longer than the batch limit is refused and must be split.</summary>
    public static ListOp[] ListFromJson(string json, ListLimits? limits = null) => Parse(json, limits, true, root =>
    {
        if (root.ValueKind != JsonValueKind.Array) throw new FormatException("Operations must be an array.");
        int max = (limits ?? ListLimits.Default).MaxBatchOperations;
        if (root.GetArrayLength() > max)
            throw new ListLimitException(nameof(ListLimits.MaxBatchOperations), max,
                $"The array holds {root.GetArrayLength()} operations; send batches of at most {max}.");
        return root.EnumerateArray().Select(Read).ToArray();
    });

    static T Parse<T>(string json, ListLimits? limits, bool batch, Func<JsonElement, T> read)
    {
        ArgumentNullException.ThrowIfNull(json);
        limits ??= ListLimits.Default;
        limits.CheckJson(json, batch);
        try
        {
            using var doc = CanonicalJson.Parse(json);
            return read(doc.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException ||
            (exception is ArgumentException && exception is not ListLimitException))
        {
            throw new FormatException("Invalid list operation JSON.", exception);
        }
    }

    static ListOp Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException("Operation must be an object.");
        string[] known = ["v", "kind", "node", "lamport", "element", "left", "right"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(known, property.Name) < 0) throw new FormatException("Unknown member: " + property.Name);
            if (!seen.Add(property.Name)) throw new FormatException("Duplicate member: " + property.Name);
        }
        if (!element.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out int number) || number != SchemaVersion)
            throw new FormatException("Unsupported schema version.");
        var kind = Str(element, "kind") switch
        {
            "insert" => ListOpKind.Insert, "move" => ListOpKind.Move, "delete" => ListOpKind.Delete,
            _ => throw new FormatException("Unknown kind.")
        };
        var node = ReadNode(element, "node") ?? throw new FormatException("Node is required.");
        var operation = new ListOp(kind, node, ParseLong(Str(element, "lamport")), Str(element, "element"),
            ReadNode(element, "left"), ReadNode(element, "right"));
        operation.Validate();
        return operation;
    }

    static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()! : throw new FormatException(name + " must be a string.");

    static long ParseLong(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ||
            number.ToString(CultureInfo.InvariantCulture) != value)
            throw new FormatException("Not a canonical decimal integer: " + value);
        return number;
    }

    static NodeId? ReadNode(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)) throw new FormatException(name + " is missing.");
        if (property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.Array || property.GetArrayLength() != 2 ||
            property[0].ValueKind != JsonValueKind.String || property[1].ValueKind != JsonValueKind.String)
            throw new FormatException(name + " must be [replica, decimalCounter] or null.");
        return new NodeId(property[0].GetString()!, ParseLong(property[1].GetString()!));
    }
}
