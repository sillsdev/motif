namespace SIL.Motif.Host.Corpus;

/// <summary>Supplies source-preserving Wordform spans for a Document.</summary>
public interface IWordSpanTokeniser
{
    /// <summary>Immutable segmentation evidence, or null when the tokeniser cannot certify its configuration.</summary>
    WordSpanProfile? Profile => null;

    /// <summary>Returns spans in Document order with UTF-8 and UTF-16 offsets.</summary>
    IEnumerable<CorpusWordSpan> TokeniseSpans(string text);
}

/// <summary>The configuration digest and, for a writing-system-dependent profile, its writing system identity.</summary>
public sealed record WordSpanProfile(string? WritingSystemId, string Sha256);
