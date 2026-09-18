using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>One of the four agreed sources that contributed to a composed Selection, and how many words it added.</summary>
/// <param name="Source">A stable, lower-case-with-hyphens name for the source, such as <c>all-wordforms</c>.</param>
/// <param name="Count">
/// How many words this source contributed on its own, before union with any other requested source. A
/// composed Selection's final word count can be smaller than the sum of every entry here — that gap is
/// itself informative: it says how much the requested sources overlapped.
/// </param>
public sealed record SelectionProvenanceEntry(string Source, int Count);

/// <summary>
/// The Selection a Baseline Assessment or Handoff measured: the final word list plus which of the four
/// agreed sources produced it and how many words each contributed. A Handoff's own record of which words
/// were actually run lives in <c>assessment.json</c>, keyed by word; this projection is what produced it.
/// </summary>
/// <param name="Words">
/// The final, de-duplicated, ordinally-sorted word list — the same list a corresponding
/// <c>SIL.Motif.Host.Corpus.Selection</c> carries as <c>Words</c>.
/// </param>
/// <param name="Provenance">One entry per source that was actually requested, in the order the composer considered them.</param>
public sealed record SelectionProjection(
    IReadOnlyList<string> Words,
    IReadOnlyList<SelectionProvenanceEntry> Provenance);
