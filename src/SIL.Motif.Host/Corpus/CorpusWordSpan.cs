namespace SIL.Motif.Host.Corpus;

/// <summary>A source Wordform and its exact offsets in both UTF-16 code units and UTF-8 bytes.</summary>
public sealed record CorpusWordSpan(string Surface, int Utf16Start, int Utf16Length, int Utf8Start, int Utf8Length);
