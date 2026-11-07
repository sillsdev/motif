using System.Text;

namespace SIL.Motif.Host.Corpus;

/// <summary>A frequency-ranked Wordform and its source occurrences.</summary>
public sealed record CorpusViewerWordform(string Wordform, int Frequency, IReadOnlyList<CorpusWordSpan> Occurrences);

/// <summary>A source snapshot and the interim boundaries computed from that exact snapshot.</summary>
public sealed record CorpusViewerDocument(string CorpusId, string DocumentId, string Text, string Sha256,
    string BoundaryDescription, IReadOnlyList<CorpusWordSpan> Boundaries, IReadOnlyList<CorpusViewerWordform> Wordforms);

/// <summary>Shared corpus viewer operations for native and external hosts.</summary>
public interface ICorpusViewerService
{
    /// <summary>Opens a source snapshot and computes interim Wordform boundaries.</summary>
    CorpusViewerDocument Open(string corpusId, string documentId);
    /// <summary>Reads bytes only when the current Document matches the requested hash.</summary>
    byte[] ReadRange(string corpusId, string documentId, string expectedSha256, long offset, int length);
}

/// <summary>Serves file snapshots through a caller-supplied span tokeniser.</summary>
public sealed class CorpusViewerService(CorpusFileService files, IWordSpanTokeniser tokeniser) : ICorpusViewerService
{
    /// <inheritdoc />
    public CorpusViewerDocument Open(string corpusId, string documentId)
    {
        var (text, sha256) = files.ReadDocument(corpusId, documentId);
        var spans = tokeniser.TokeniseSpans(text).ToArray();
        var words = spans.GroupBy(span => span.Surface.Normalize(NormalizationForm.FormD), StringComparer.Ordinal)
            .Select(group => new CorpusViewerWordform(group.Key, group.Count(), group.ToArray()))
            .OrderByDescending(word => word.Frequency).ThenBy(word => word.Wordform, StringComparer.Ordinal).ToArray();
        return new CorpusViewerDocument(corpusId, documentId, text, sha256,
            "Interim boundaries — not CEB-compatible", spans, words);
    }

    /// <inheritdoc />
    public byte[] ReadRange(string corpusId, string documentId, string expectedSha256, long offset, int length) =>
        files.ReadRange(corpusId, documentId, expectedSha256, offset, length);
}
