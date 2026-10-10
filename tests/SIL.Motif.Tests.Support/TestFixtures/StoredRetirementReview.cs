using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// The complete review the shared retirement-review query returns for the seeded owner project, kept as a
/// checked-in JSON file so the window's captures show a real stored review rather than a hand-made one.
/// </summary>
/// <remarks>
/// Every run mints fresh identities, so the stored file is one real run and the live response is compared
/// to it after <see cref="Normalize"/>. Set <see cref="UpdateVariable"/> to <c>1</c> to rewrite the file
/// from the live response, as the other goldens are refreshed.
/// </remarks>
internal static partial class StoredRetirementReview
{
    /// <summary>The variable that makes <see cref="Compare"/> rewrite the file instead of comparing.</summary>
    internal const string UpdateVariable = "MOTIF_UPDATE_GOLDEN";

    private const string FileName = "stored-retirement-review.json";

    /// <summary>Loads the stored review from the test binaries' copy of the fixture.</summary>
    internal static RetirementReviewQueryResponse Load() =>
        JsonSerializer.Deserialize<RetirementReviewQueryResponse>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", FileName)),
            MotifJson.CreateOptions())!;

    /// <summary>
    /// Rewrites the stored file when <see cref="UpdateVariable"/> is <c>1</c>; otherwise returns the stored and
    /// the live review, both normalised, for the caller to compare.
    /// </summary>
    internal static (string Stored, string Live) Compare(RetirementReviewQueryResponse live)
    {
        var liveJson = JsonSerializer.Serialize(live, MotifJson.CreateOptions());
        var path = Path.Combine(SourceDirectory(), FileName);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            // The scratch path would put a machine's directory into a checked-in file.
            liveJson = BaselineNote().Replace(liveJson, "(published Baseline directory <dir>; $1 target object(s): <ids>)");
            File.WriteAllText(path, JsonNode.Parse(liveJson)!.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return (string.Empty, string.Empty);
        }
        return (Normalize(File.ReadAllText(path)), Normalize(liveJson));
    }

    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;

    /// <summary>
    /// Masks what a run mints and puts unordered lists in a fixed order.
    /// </summary>
    /// <remarks>
    /// Masked: every 22-character entity or job id, every <c>sha256:</c> digest and bare 64-hex digest
    /// (they hash ids and run identity), GUIDs, the published Baseline directory and target-object list in
    /// the dry run's note (a path under the run's scratch root, and ids), the base64 body of the
    /// <c>motif-human-judgment</c> marker (it embeds ids and digests), and the order of the alternates
    /// named in a judgment's caption (it follows id order). Sorted: every list except the review's parts,
    /// whose order is the window's reading order, because effect and operation order follows id order.
    /// </remarks>
    internal static string Normalize(string json) =>
        Rebuild(JsonNode.Parse(json), null)!.ToJsonString();

    private static JsonNode? Rebuild(JsonNode? node, string? parent)
    {
        switch (node)
        {
            case JsonObject obj:
                var rebuilt = new JsonObject();
                foreach (var (key, value) in obj) rebuilt[key] = Rebuild(value, key);
                return rebuilt;
            case JsonArray array:
                var items = array.Select(item => Rebuild(item, null)).ToList();
                if (parent != "Parts") items = [.. items.OrderBy(item => item?.ToJsonString(), StringComparer.Ordinal)];
                return new JsonArray(items.ToArray());
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(MaskText(text));
            default:
                return node?.DeepClone();
        }
    }

    private static string MaskText(string text)
    {
        text = BaselineNote().Replace(text, "(published Baseline directory <dir>; $1 target object(s): <ids>)");
        text = AlternatesCaption().Replace(text, "$1<alternates>");
        text = JudgmentBody().Replace(text, "$1<encoded>]");
        return RunMinted().Replace(text, "<id>");
    }

    [GeneratedRegex(@"\(published Baseline directory [^;]*; (\d+) target object\(s\): [^)]*\)")]
    private static partial Regex BaselineNote();

    [GeneratedRegex("(alternation family: )[^”\"]*")]
    private static partial Regex AlternatesCaption();

    [GeneratedRegex(@"(\[motif-human-judgment:v1:)[A-Za-z0-9_=-]+\]")]
    private static partial Regex JudgmentBody();

    [GeneratedRegex(@"sha256:[0-9a-f]{64}|[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}" +
        @"|(?<![A-Za-z0-9_.-])(?=[A-Za-z0-9_-]*[0-9_A-Z-])[A-Za-z0-9_-]{22}(?![A-Za-z0-9_.-])" +
        @"|(?<![0-9a-zA-Z])[0-9a-f]{64}(?![0-9a-zA-Z])")]
    private static partial Regex RunMinted();
}
