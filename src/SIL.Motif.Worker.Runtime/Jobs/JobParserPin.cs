using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Worker.Jobs;

/// <summary>
/// The parser an enqueuer expects a job to run on, carried in the job's own input document as
/// <c>parserIdentity</c> so the queue table is unchanged.
/// </summary>
/// <remarks>
/// A Worker claims a pinned job only when its own <see cref="ParserIdentity.Key"/> equals the pin's. A job with
/// no pin (every job queued before this existed, and any queued with no parser to name) is claimable by any
/// Worker: refusing it would strand work no enqueuer can re-pin, and it ran before under whichever parser
/// the Worker had. Pinned by `ALegacyJobWithNoParserIdentityIsClaimedByAnyWorker`.
/// </remarks>
public static class JobParserPin
{
    /// <summary>The property of a job's input document that holds the expected parser.</summary>
    public const string Property = "parserIdentity";

    /// <summary>The SQLite path of the pin's key within <c>InputJson</c>.</summary>
    internal const string KeyPath = "$.parserIdentity.key";

    /// <summary>Adds the identity of the parser at <paramref name="parserPath"/> to an object input document.</summary>
    /// <returns>The document unchanged when no parser is named or found, or the document is not an object.</returns>
    public static string Stamp(string inputJson, string? parserPath)
    {
        if (ParserIdentity.Read(parserPath) is not { } identity) return inputJson;
        if (JsonNode.Parse(inputJson) is not JsonObject root) return inputJson;
        root[Property] = new JsonObject
        {
            ["version"] = identity.Version,
            ["sha256"] = identity.Sha256,
            ["path"] = identity.Path,
            ["key"] = identity.Key,
        };
        return root.ToJsonString();
    }

    /// <summary>The pinned key of a job's input document, or null for a job with no pin.</summary>
    public static string? KeyOf(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(Property, out var pin) &&
                pin.ValueKind == JsonValueKind.Object && pin.TryGetProperty("key", out var key) &&
                key.ValueKind == JsonValueKind.String ? key.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether a Worker whose parser key is <paramref name="workerKey"/> may claim the job.</summary>
    public static bool Matches(string inputJson, string? workerKey) =>
        KeyOf(inputJson) is not { } pinned || string.Equals(pinned, workerKey, StringComparison.Ordinal);
}
