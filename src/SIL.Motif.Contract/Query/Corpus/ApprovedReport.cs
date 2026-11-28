namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>One word of an approved-readings file compared with the readings the grammar gives it.</summary>
/// <param name="Word">The word, as the corpus spells it.</param>
/// <param name="Tokens">Its occurrences in the corpus; 0 when it is not in the corpus.</param>
/// <param name="InCorpus">False when the corpus does not hold the word; then every approved reading is missing.</param>
/// <param name="ParseState">The word's parse state; <c>absent</c> when it is not in the corpus. A capped, timed-out or failed parse may hide readings.</param>
/// <param name="Kept">The approved readings, as the file wrote them, that the grammar still produces.</param>
/// <param name="Missing">The approved readings, as the file wrote them, that no produced reading renders to; a gloss the rendering cannot give shows here, never silently.</param>
/// <param name="Extra">The produced readings no approved reading matches, as the rendered gloss (<c>text/gloss</c> per morph joined by <c> + </c>), once each however many copies.</param>
/// <param name="AllApprovedKept">Every approved reading is produced; null when the parse was capped, timed out or failed, because the readings are then incomplete and the word is excluded from the share numerators.</param>
/// <param name="NoExtras">The grammar gives the word no unapproved reading; null when the parse was capped, timed out or failed.</param>
public sealed record ApprovedWord(string Word, long Tokens, bool InCorpus, string ParseState, IReadOnlyList<string> Kept,
    IReadOnlyList<string> Missing, IReadOnlyList<string> Extra, bool? AllApprovedKept, bool? NoExtras);

/// <summary>The approved-readings comparison in total.</summary>
/// <param name="ApprovedWords">Words in the file with at least one reading whose class is correct.</param>
/// <param name="ApprovedWordsInCorpus">Of them, words the corpus holds; the token shares are over these.</param>
/// <param name="ApprovedWordsNotInCorpus">Of them, words the corpus does not hold: no tokens, so outside the shares.</param>
/// <param name="WordsWithoutApprovedReading">Words in the file with no correct reading (wrong-only or none); they are not compared and are in no count here.</param>
/// <param name="ApprovedReadings">Approved readings in the file.</param>
/// <param name="Kept">Approved readings still produced.</param>
/// <param name="Missing">Approved readings not produced, over the words whose parse finished (a capped, timed-out or failed word may be hiding them, so it is left out, as from the shares).</param>
/// <param name="Extra">Produced readings that are not approved, over the words whose parse finished.</param>
/// <param name="IncompleteWords">Words whose parse was capped, timed out or failed: kept, missing and extra are not final, and they are left out of the share numerators.</param>
/// <param name="IncompleteTokens">Corpus tokens of those words; they stay in <paramref name="Tokens"/>, so the shares are lower bounds.</param>
/// <param name="Tokens">Corpus tokens of the in-corpus words in the file (the shares' denominator).</param>
/// <param name="TokensAllKept">Tokens of words whose approved readings are all produced.</param>
/// <param name="TokensAllKeptNoExtras">Tokens of words whose approved readings are all produced and that have no extra reading.</param>
/// <param name="ApprovedShareTokens"><paramref name="TokensAllKeptNoExtras"/> over <paramref name="Tokens"/>; null when there are no tokens.</param>
/// <param name="ApprovedShareTokensIgnoringExtras"><paramref name="TokensAllKept"/> over <paramref name="Tokens"/>, which ignores extra readings; null when there are no tokens.</param>
public sealed record ApprovedTotals(int ApprovedWords, int ApprovedWordsInCorpus, int ApprovedWordsNotInCorpus, int WordsWithoutApprovedReading, int ApprovedReadings, int Kept, int Missing, int Extra,
    int IncompleteWords, long IncompleteTokens, long Tokens,
    long TokensAllKept, long TokensAllKeptNoExtras, double? ApprovedShareTokens, double? ApprovedShareTokensIgnoringExtras);

/// <summary>What <c>--approved</c> found: each word of the file against the grammar's readings, and the totals.</summary>
/// <param name="Complete">Mirrors <c>readingsInfo.complete</c>: false when a capped, timed-out or unanswered word may hide readings, so kept and missing are then not final.</param>
/// <param name="Totals">The totals and the two shares.</param>
/// <param name="Words">One row per word of the file, by tokens then word.</param>
public sealed record ApprovedReport(bool Complete, ApprovedTotals Totals, IReadOnlyList<ApprovedWord> Words);
