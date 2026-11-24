using System.Text;
using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// The <c>match</c> op of <c>pangloss serve</c>: HermitCrab pattern text applied over a list of words. Written as one
/// protocol line. Motif sends no <c>boundaryMarker</c>, because the words it asks about are surface forms.
/// </summary>
/// <param name="Pattern">The pattern text, with natural classes in brackets.</param>
/// <param name="Words">The words, in the order the reply returns them.</param>
/// <param name="MaxMatchesPerWord">Caps the spans kept for one word, or null for the parser's default.</param>
/// <param name="MaxWork">Lowers the matcher's work cap per word, or null.</param>
/// <param name="WordTimeoutMs">A wall-clock deadline per word, or null.</param>
public sealed record ServeMatchRequest(string Pattern, IReadOnlyList<string> Words, int? MaxMatchesPerWord = null,
    long? MaxWork = null, long? WordTimeoutMs = null)
{
    /// <summary>The protocol line for the request with the given id, without its newline.</summary>
    public string ToLine(string id)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("op", "match");
            writer.WriteString("pattern", Pattern);
            writer.WriteStartArray("words");
            foreach (var word in Words) writer.WriteStringValue(word);
            writer.WriteEndArray();
            if (MaxMatchesPerWord is { } cap) writer.WriteNumber("maxMatchesPerWord", cap);
            if (MaxWork is { } work) writer.WriteNumber("maxWork", work);
            if (WordTimeoutMs is { } timeout) writer.WriteNumber("wordTimeoutMs", timeout);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
