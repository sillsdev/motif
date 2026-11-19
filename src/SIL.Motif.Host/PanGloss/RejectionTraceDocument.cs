using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// One <c>pangloss.diagnostic.v1</c> capture for a single word, with every producer record kept as the original JSON
/// value so a later reader sees exactly what the parser wrote.
/// </summary>
/// <param name="Header">The header record.</param>
/// <param name="Records">Every record between the header and the footer, in the order written.</param>
/// <param name="Footer">The footer record.</param>
public sealed record RejectionTraceDocument(JsonElement Header, IReadOnlyList<JsonElement> Records, JsonElement Footer)
{
    /// <summary>The wire schema Motif reads.</summary>
    public const string SchemaVersion = PanGlossInterfaceVersions.RejectionTraceSchemaId;

    private static readonly HashSet<string> KnownTypes =
        ["header", "input", "path", "mapping", "event", "output", "word_end", "footer"];

    /// <summary>Records of one type, in the order written.</summary>
    public IEnumerable<JsonElement> Of(string type) =>
        Records.Where(record => record.GetProperty("type").GetString() == type);

    /// <summary>
    /// Reads JSON lines. An unknown schema version, an unknown record type, a missing header or footer, or a
    /// line that is not a JSON object fails visibly rather than being skipped.
    /// </summary>
    public static bool TryRead(string text, out RejectionTraceDocument? document, out string error)
    {
        document = null;
        var records = new List<JsonElement>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var element = JsonDocument.Parse(line).RootElement.Clone();
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                {
                    error = "A record has no type.";
                    return false;
                }
                if (!KnownTypes.Contains(type.GetString()!))
                {
                    error = $"The record type '{type.GetString()}' is not in {SchemaVersion}.";
                    return false;
                }
                records.Add(element);
            }
            catch (JsonException exception)
            {
                error = "A line is not JSON: " + exception.Message;
                return false;
            }
        }
        if (records.Count < 2 || records[0].GetProperty("type").GetString() != "header")
        {
            error = "The capture does not begin with a header.";
            return false;
        }
        if (records[^1].GetProperty("type").GetString() != "footer")
        {
            error = "The capture has no footer, so the parser did not finish writing it.";
            return false;
        }
        if (!records[0].TryGetProperty("schemaVersion", out var version) || version.GetString() != SchemaVersion)
        {
            error = $"The capture's schema is '{(version.ValueKind == JsonValueKind.String ? version.GetString() : "missing")}', not {SchemaVersion}.";
            return false;
        }
        document = new RejectionTraceDocument(records[0], records.Skip(1).Take(records.Count - 2).ToArray(), records[^1]);
        error = string.Empty;
        return true;
    }
}
