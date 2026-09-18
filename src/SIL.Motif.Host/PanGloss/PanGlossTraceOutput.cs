using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Reads the two things <c>pangloss parse --trace --trace-format json</c> writes to standard output: the
/// <c>word\tsignature</c> parity line every <c>parse</c> call prints, then — unless the word's shape was
/// never even traceable — the trace tree PanGloss's own <c>trace_render::render_json</c> emits in one
/// write, after the whole derivation is built.
/// </summary>
internal static class PanGlossTraceOutput
{
    /// <summary>
    /// True with a signature and a possibly-null tree on success — <see langword="null"/> is a real PanGloss
    /// outcome (nothing was ever traced), not a parse failure. False means the trace text was not the shape
    /// this method requires; the caller must not read <paramref name="signature"/> or <paramref name="root"/>.
    /// </summary>
    internal static bool TryParse(string standardOutput, out string signature, out PanGlossTraceNode? root)
    {
        signature = string.Empty;
        root = null;
        var newline = standardOutput.IndexOf('\n');
        if (newline < 0) return false;
        var parity = standardOutput[..newline];
        var tab = parity.IndexOf('\t');
        if (tab < 0) return false;
        signature = parity[(tab + 1)..].TrimEnd('\r');

        var json = standardOutput[(newline + 1)..];
        if (string.IsNullOrWhiteSpace(json)) return true;

        try
        {
            using var document = JsonDocument.Parse(json);
            root = ParseNode(document.RootElement);
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or ArgumentException or
            KeyNotFoundException or FormatException or OverflowException or NotSupportedException)
        {
            root = null;
            return false;
        }
    }

    private static PanGlossTraceNode ParseNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("A trace node must be a JSON object.");
        if (!element.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            throw new JsonException("A trace node's \"type\" must be a string.");
        if (!element.TryGetProperty("children", out var childrenElement) || childrenElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("A trace node must have a \"children\" array.");

        var subrule = element.TryGetProperty("subrule", out var subruleElement) && subruleElement.ValueKind == JsonValueKind.Number
            ? subruleElement.GetInt32()
            : (int?)null;
        var children = new List<PanGlossTraceNode>(childrenElement.GetArrayLength());
        foreach (var child in childrenElement.EnumerateArray()) children.Add(ParseNode(child));

        return new PanGlossTraceNode(
            typeElement.GetString()!,
            OptionalString(element, "source"),
            subrule,
            OptionalString(element, "failureReason"),
            OptionalString(element, "outputShape"),
            OptionalString(element, "inputShape"),
            children);
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
