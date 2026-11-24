using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>The <c>inventory</c> op of <c>pangloss serve</c>: the loaded grammar's natural classes and phonological features, as one protocol line.</summary>
public static class ServeInventoryRequest
{
    /// <summary>The protocol line for the request with the given id, without its newline.</summary>
    public static string ToLine(string id) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = id, ["op"] = "inventory" });
}
