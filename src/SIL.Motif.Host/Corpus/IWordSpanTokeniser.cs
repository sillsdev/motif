namespace SIL.Motif.Host.Corpus;

/// <summary>Supplies source-preserving Wordform spans for a Document.</summary>
public interface IWordSpanTokeniser
{
    /// <summary>Returns spans in Document order with UTF-8 and UTF-16 offsets.</summary>
    IEnumerable<CorpusWordSpan> TokeniseSpans(string text);
}
