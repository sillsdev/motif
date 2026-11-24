using System.Text;
using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>The morpheme an <c>effects</c> request asks about, addressed by its FieldWorks identity.</summary>
/// <param name="EntryGuid">The lexical entry's GUID in lowercase textual form.</param>
/// <param name="MsaGuid">The morphosyntactic analysis's GUID in lowercase textual form.</param>
/// <param name="AllomorphId">The compiled allomorph id <c>generate</c> reports, or null for every allomorph of the morpheme.</param>
public sealed record EffectsMorpheme(string EntryGuid, string MsaGuid, int? AllomorphId = null);

/// <summary>
/// The <c>effects</c> op of <c>pangloss serve</c> (v0.31.0): which phonological rules may change a morpheme's allomorphs at
/// each of its edges. Written as one protocol line.
/// </summary>
/// <param name="Morpheme">The morpheme to scan.</param>
/// <param name="Edges">Which edges to scan, <c>left</c> and <c>right</c>; null for both.</param>
/// <param name="RootsPerEnvironment">PanGloss echoes it in <c>limits</c> and does nothing else with it in 0.31.0; Motif plans test roots itself and sends none.</param>
/// <param name="MaxWork">Lowers the scan's work cap, counted in allomorph-against-subrule units, or null.</param>
/// <param name="WordTimeoutMs">A wall-clock deadline for the scan, or null.</param>
public sealed record ServeEffectsRequest(EffectsMorpheme Morpheme, IReadOnlyList<string>? Edges = null,
    int? RootsPerEnvironment = null, long? MaxWork = null, long? WordTimeoutMs = null)
{
    /// <summary>The edges the op knows.</summary>
    public static IReadOnlyList<string> KnownEdges { get; } = ["left", "right"];

    /// <summary>The protocol line for the request with the given id, without its newline.</summary>
    public string ToLine(string id)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("op", "effects");
            writer.WriteStartObject("morpheme");
            writer.WriteString("entryGuid", Morpheme.EntryGuid);
            writer.WriteString("msaGuid", Morpheme.MsaGuid);
            if (Morpheme.AllomorphId is { } allomorph) writer.WriteNumber("allomorphId", allomorph);
            writer.WriteEndObject();
            if (Edges is not null)
            {
                writer.WriteStartArray("edges");
                foreach (var edge in Edges) writer.WriteStringValue(edge);
                writer.WriteEndArray();
            }
            if (RootsPerEnvironment is { } roots) writer.WriteNumber("rootsPerEnvironment", roots);
            if (MaxWork is { } work) writer.WriteNumber("maxWork", work);
            if (WordTimeoutMs is { } timeout) writer.WriteNumber("wordTimeoutMs", timeout);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>How long the whole request may take: its own deadline (PanGloss's default is 10 s) plus slack, never less than <paramref name="floor"/> and never more than an hour.</summary>
    public TimeSpan Timeout(TimeSpan floor)
    {
        var total = TimeSpan.FromMilliseconds(WordTimeoutMs ?? 10_000) + TimeSpan.FromSeconds(30);
        return TimeSpan.FromTicks(Math.Min(Math.Max(floor.Ticks, total.Ticks), TimeSpan.FromHours(1).Ticks));
    }
}
