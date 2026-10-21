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

    /// <summary>The parser a job is pinned to, or null for a job with no pin.</summary>
    public static PinnedParser? Read(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(Property, out var pin) || pin.ValueKind != JsonValueKind.Object)
                return null;
            string? Text(string name) => pin.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            return Text("key") is { } key ? new PinnedParser(Text("version") ?? "unknown version", Text("path") ?? "", key) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether the pinned parser can still be started: its file exists and its identity is still the pin.</summary>
    public static bool IsStartable(PinnedParser pin) =>
        pin.Path.Length != 0 && string.Equals(ParserIdentity.KeyOf(pin.Path), pin.Key, StringComparison.Ordinal);

    /// <summary>The result document of a job failed because its pinned parser can no longer be started.</summary>
    public static string UnavailableResult(PinnedParser pin) => new JsonObject
    {
        ["code"] = SIL.Motif.Contract.Commands.RefusalCodes.JobParserUnavailable,
        ["message"] = "This job was queued for PanGloss " + pin.Version + " at '" + pin.Path +
            "', which is gone or was replaced, so no runner can run it. Run the Trial again.",
    }.ToJsonString();
}

/// <summary>The parser a job's pin names.</summary>
public sealed record PinnedParser(string Version, string Path, string Key);
