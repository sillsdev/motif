using SIL.Motif.Host.Corpus;

namespace SIL.Motif.Host.Parser;

/// <summary>
/// A coverage figure, computed from one <see cref="BatchAnalysis"/> over one <see cref="Selection"/>,
/// carrying everything ADR 0032 §4 requires a
/// coverage number to cite. There is deliberately no path in this codebase that produces a bare coverage
/// percentage — that omission is the defect this type exists to make impossible rather than merely
/// discouraged.
/// </summary>
/// <param name="SelectionName">Which selection this was measured over. See <see cref="Selection.Name"/>.</param>
/// <param name="SelectionSha256">
/// The selection's content hash at measurement time. A figure whose selection no longer hashes to this value
/// describes a selection that has since changed and must not be trusted as still describing it.
/// </param>
/// <param name="GrammarSourceSha256">
/// The grammar's identity. Always taken from an <see cref="AssessReport"/>'s own field — this type never
/// hashes a grammar itself, because <see cref="AssessReport.GrammarSourceSha256"/> already carries the
/// parser's own hash of exactly what it read (<see cref="GrammarCoverageFigure"/>'s remarks on <c>Compute</c>).
/// </param>
/// <param name="PerWordTimeoutMs">
/// The per-word time limit recorded for the run.
/// </param>
/// <param name="TimedOutCount">
/// How many words reached their time limit without finishing their searches.
/// </param>
/// <param name="Analysed">Words the parser produced at least one analysis for — the numerator.</param>
/// <param name="Adjudicated">
/// The denominator: analysed plus no-analysis, exactly <see cref="BatchAnalysis.Adjudicated"/>. Timed-out
/// capped and skipped words are excluded because they carry no completed search, so including them would let a
/// figure move for reasons that have nothing to do with the grammar.
/// </param>
/// <param name="CappedCount">Words whose searches exhausted the step budget.</param>
/// <param name="SkippedCount">Words the parser did not attempt.</param>
public sealed record GrammarCoverageFigure(
    string SelectionName,
    string SelectionSha256,
    string GrammarSourceSha256,
    int? PerWordTimeoutMs,
    int TimedOutCount,
    int Analysed,
    int Adjudicated,
    int CappedCount,
    int SkippedCount)
{
    /// <summary>
    /// The coverage fraction — <see cref="Analysed"/> divided by <see cref="Adjudicated"/>, or <c>null</c>
    /// when no search completed. A fraction over completed searches does not bound the whole selection's
    /// grammar coverage; the omitted searches may have either outcome.
    /// </summary>
    public double? Fraction => Adjudicated == 0 ? null : (double)Analysed / Adjudicated;

    /// <summary>
    /// Whether an attempted word's search stopped at a time or step limit.
    /// </summary>
    public bool IsIncomplete => IncompleteCount > 0;

    public int IncompleteCount => TimedOutCount + CappedCount;

    /// <summary>
    /// Whether this figure still describes the current world.
    /// </summary>
    public bool IsCurrent(string currentSelectionSha256, string currentGrammarSourceSha256) =>
        string.Equals(SelectionSha256, currentSelectionSha256, StringComparison.Ordinal)
        && string.Equals(GrammarSourceSha256, currentGrammarSourceSha256, StringComparison.Ordinal);

    /// <summary>
    /// The sentence a report prints. <b>The only way to render this figure, and deliberately so.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no parameterless overload, and that omission is the whole design.</b> A caller cannot
    /// produce a bare number without saying what the current selection and grammar are, so a figure can never be
    /// stated without the identifiers that give it meaning (ADR 0038 decision 8).
    /// </para>
    /// <para>
    /// <b>Staleness is handled by tense, not by a caveat.</b> A stale figure is not wrong — it is a correct
    /// measurement of a state that has since changed, and it is genuine evidence about whether a change
    /// helped. What makes a stale number dangerous is stating it in the <i>present</i> tense with an asterisk,
    /// because the asterisk is what gets dropped when somebody quotes it. Putting the date inside the claim
    /// instead of beside it cannot be paraphrased away.
    /// </para>
    /// <para>
    /// This is deliberately unlike the two things Motif does not state. An accuracy figure over
    /// unvetted text is a number about nothing, and a causal attribution across a proposal that moved both
    /// rules and expectations is unsupported by the evidence. A stale coverage figure is neither: its meaning
    /// is exact, provided the state it describes is named.
    /// </para>
    /// </remarks>
    public string Describe(string currentSelectionSha256, string currentGrammarSourceSha256)
    {
        var subject = $"selection '{SelectionName}' ({Short(SelectionSha256)}) under grammar {Short(GrammarSourceSha256)}";
        var current = IsCurrent(currentSelectionSha256, currentGrammarSourceSha256);

        var verb = current ? "is" : "was";
        var counts = $"{Adjudicated:N0} searches completed; {IncompleteCount:N0} incomplete " +
                     $"({CappedCount:N0} capped, {TimedOutCount:N0} timed out); {SkippedCount:N0} skipped.";
        var measure = Fraction is null
            ? "no search completed, so grammar coverage is not computable"
            : $"grammar coverage {verb} {Fraction.Value:P1} ({Analysed:N0} of {Adjudicated:N0} completed searches)";

        // Present tense is licensed only when both hashes still match.
        if (current) return $"{counts} For {subject}, {measure}.";

        var moved = new List<string>();
        if (!string.Equals(SelectionSha256, currentSelectionSha256, StringComparison.Ordinal))
            moved.Add($"the selection has changed (now {Short(currentSelectionSha256)})");
        if (!string.Equals(GrammarSourceSha256, currentGrammarSourceSha256, StringComparison.Ordinal))
            moved.Add($"the grammar has changed (now {Short(currentGrammarSourceSha256)})");

        return $"{counts} As of the assessment over {subject}, {measure}. Since then, {string.Join(" and ", moved)}, " +
               "so this describes a state that no longer exists. Rerun to measure the current one.";
    }

    /// <summary>A hash short enough to read in a sentence. The full value stays on the record.</summary>
    private static string Short(string sha256)
    {
        if (string.IsNullOrEmpty(sha256)) return "(unrecorded)";
        var body = sha256.StartsWith("sha256:", StringComparison.Ordinal) ? sha256[7..] : sha256;
        return body.Length <= 12 ? body : body[..12] + "...";
    }

    /// <summary>
    /// Computes a coverage figure from one parser run and the selection it was declared to run over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller supplies the grammar identity separately from the batch outcomes so that the figure
    /// describes the measured input without opening a project or invoking a parser while rendering.
    /// </para>
    /// <para>
    /// <b>Throws rather than silently mismeasuring</b> when <paramref name="analysis"/> did not in fact run
    /// over <paramref name="selection"/>'s word set (pinned by `Compute_ThrowsWhenTheBatchsWordsDoNotMatchTheCorpus`)
    /// — a coverage figure's provenance is only honest if it really describes the words it cites, and a
    /// caller passing the wrong selection for a batch is a bug to surface immediately, not a mismatch to paper
    /// over with whichever count happens to be smaller.
    /// </para>
    /// </remarks>
    public static GrammarCoverageFigure Compute(BatchAnalysis analysis, Selection selection, string grammarSourceSha256)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        if (selection is null) throw new ArgumentNullException(nameof(selection));
        if (string.IsNullOrWhiteSpace(grammarSourceSha256))
            throw new ArgumentException("Required.", nameof(grammarSourceSha256));

        var analysedWords = new HashSet<string>(analysis.Words.Select(w => w.Word), StringComparer.Ordinal);
        var selectionWords = new HashSet<string>(selection.Words, StringComparer.Ordinal);
        if (!analysedWords.SetEquals(selectionWords))
            throw new ArgumentException(
                $"The batch analysed {analysedWords.Count} distinct word(s) but selection '{selection.Name}' " +
                $"describes {selectionWords.Count}. A coverage figure must be measured over exactly the selection " +
                "it cites; passing a mismatched pair would make the provenance this type exists to carry " +
                "false.",
                nameof(analysis));

        return new GrammarCoverageFigure(
            SelectionName: selection.Name,
            SelectionSha256: selection.Sha256,
            GrammarSourceSha256: grammarSourceSha256,
            PerWordTimeoutMs: analysis.PerWordTimeoutMs,
            TimedOutCount: analysis.TimedOut,
            Analysed: analysis.Analysed,
            Adjudicated: analysis.Adjudicated,
            CappedCount: analysis.Capped,
            SkippedCount: analysis.Skipped);
    }

    /// <summary>
    /// Convenience overload taking the whole <see cref="AssessReport"/> the grammar hash came free with,
    /// rather than making every caller spell out <c>report.GrammarSourceSha256</c>.
    /// </summary>
    public static GrammarCoverageFigure Compute(BatchAnalysis analysis, Selection selection, AssessReport grammarSource)
    {
        if (grammarSource is null) throw new ArgumentNullException(nameof(grammarSource));
        return Compute(analysis, selection, grammarSource.GrammarSourceSha256);
    }
}
