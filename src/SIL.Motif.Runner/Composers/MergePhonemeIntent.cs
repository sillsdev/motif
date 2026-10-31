using System.Text.Json;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Composers;

/// <summary>Merges one phoneme into another: its written representations move across and the phoneme is removed.</summary>
/// <param name="Source">The phoneme that is merged away.</param>
/// <param name="Target">The phoneme that keeps its identity and gains the source's representations.</param>
public sealed record MergePhonemeIntent(CanonicalId Source, CanonicalId Target);

/// <summary>Parses the closed phoneme merge document.</summary>
public static class MergePhonemeIntentParser
{
    /// <summary>Parses <c>{ source, target }</c>.</summary>
    /// <exception cref="SIL.Motif.Contract.Parsing.ContractParseException">The document has unknown fields or malformed ids.</exception>
    public static MergePhonemeIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "MergePhoneme", "source", "target");
        return new(SoundSystemIntentParsing.Id(authored, "source"), SoundSystemIntentParsing.Id(authored, "target"));
    }
}

/// <summary>Splits one written representation of a phoneme out into a new phoneme.</summary>
/// <param name="Source">The phoneme that has the representation now.</param>
/// <param name="Representation">The representation to move, which the phoneme must keep at least one other than.</param>
/// <param name="Name">The name of the new phoneme.</param>
public sealed record SplitPhonemeIntent(CanonicalId Source, string Representation, string Name);

/// <summary>Parses the closed phoneme split document.</summary>
public static class SplitPhonemeIntentParser
{
    /// <summary>Parses <c>{ source, representation, name }</c>.</summary>
    /// <exception cref="SIL.Motif.Contract.Parsing.ContractParseException">The document has unknown fields or malformed values.</exception>
    public static SplitPhonemeIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "SplitPhoneme", "source", "representation", "name");
        return new(SoundSystemIntentParsing.Id(authored, "source"),
            SoundSystemIntentParsing.Text(authored, "representation"), SoundSystemIntentParsing.Text(authored, "name"));
    }
}
