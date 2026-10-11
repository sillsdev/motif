using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Pins what FieldWorks reads from <c>motif apply --all-pending --json</c> at its save boundary: the exit code, which
/// stream carries the document, every property name, and the stable values (<c>ok</c>, <c>applied</c>,
/// <c>summary</c>, failure <c>code</c> and <c>reason</c>). Identities, digests, times and paths are replaced by their
/// kind, so a golden written before a change to the Apply path must match after it, byte for byte.
/// </summary>
internal static class ApplyAllPendingContract
{
    private static readonly HashSet<string> StableValues =
        new(StringComparer.Ordinal) { "ok", "applied", "summary", "code", "reason" };

    /// <summary>Compares one CLI outcome to its golden, or rewrites the golden when MOTIF_UPDATE_GOLDEN is 1.</summary>
    public static void Match(string caseName, int exitCode, string output, string error)
    {
        var shape = new JsonObject
        {
            ["exitCode"] = exitCode,
            ["stdout"] = Shape(output),
            ["stderr"] = Shape(error),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n";
        var path = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")),
            "tests", "SIL.Motif.Tests.Cli", "Golden", $"apply-all-pending.{caseName}.json");
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, shape);
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), shape);
    }

    private static JsonNode? Shape(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : Shape(null, JsonNode.Parse(text));

    private static JsonNode? Shape(string? name, JsonNode? node) => node switch
    {
        null => null,
        JsonObject value => new JsonObject(value.OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Shape(property.Key, property.Value)))),
        JsonArray value => new JsonArray(value.Select(item => Shape(name, item)).ToArray()),
        JsonValue value when name is not null && StableValues.Contains(name) => value.DeepClone(),
        JsonValue value => JsonValue.Create("<" + value.GetValueKind().ToString().ToLowerInvariant() + ">"),
        _ => throw new InvalidOperationException("Unexpected JSON node."),
    };
}
