using System.Text;
using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>One morpheme of a <c>generate</c> request, addressed by its FieldWorks identity.</summary>
/// <param name="EntryGuid">The lexical entry's GUID in lowercase textual form.</param>
/// <param name="MsaGuid">The morphosyntactic analysis's GUID in lowercase textual form.</param>
/// <param name="MsaClass">The LibLCM class name of the analysis, such as <c>MoStemMsa</c> or <c>MoInflAffMsa</c>.</param>
public sealed record GenerateMorpheme(string EntryGuid, string MsaGuid, string MsaClass);

/// <summary>
/// The <c>generate</c> op of <c>pangloss serve</c>: the root and the morphemes to add to it, with the optional caps the op
/// reads. Written as one protocol line.
/// </summary>
/// <param name="Root">The stem to generate from.</param>
/// <param name="Morphemes">The morphemes added to it, in the order they are requested.</param>
/// <param name="Trace">Whether each surface returns its rule steps; Motif always asks.</param>
/// <param name="MaxSeeds">Caps the allomorph choices tried, or null for the parser's default.</param>
/// <param name="MaxTraceSteps">Caps the trace nodes kept across every seed, or null for the parser's default.</param>
/// <param name="MaxWork">Lowers the parser's generation work cap for this request, or null.</param>
/// <param name="TimeoutMs">A wall-clock deadline for this request, or null.</param>
public sealed record ServeGenerateRequest(GenerateMorpheme Root, IReadOnlyList<GenerateMorpheme> Morphemes,
    bool Trace = true, int? MaxSeeds = null, int? MaxTraceSteps = null, long? MaxWork = null, long? TimeoutMs = null)
{
    /// <summary>The <c>generate --request</c> schema version Motif writes.</summary>
    public const int RequestSchemaVersion = 1;

    /// <summary>The protocol line for the request with the given id, without its newline.</summary>
    public string ToLine(string id)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("op", "generate");
            writer.WriteStartObject("request");
            writer.WriteNumber("schemaVersion", RequestSchemaVersion);
            writer.WritePropertyName("root");
            Write(writer, Root);
            writer.WriteStartArray("morphemes");
            foreach (var morpheme in Morphemes) Write(writer, morpheme);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteBoolean("trace", Trace);
            if (MaxSeeds is { } seeds) writer.WriteNumber("maxSeeds", seeds);
            if (MaxTraceSteps is { } steps) writer.WriteNumber("maxTraceSteps", steps);
            if (MaxWork is { } work) writer.WriteNumber("maxWork", work);
            if (TimeoutMs is { } timeout) writer.WriteNumber("timeoutMs", timeout);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, GenerateMorpheme morpheme)
    {
        writer.WriteStartObject();
        writer.WriteString("entryGuid", morpheme.EntryGuid);
        writer.WriteString("msaGuid", morpheme.MsaGuid);
        writer.WriteString("msaClass", morpheme.MsaClass);
        writer.WriteEndObject();
    }
}
