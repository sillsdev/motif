using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Parser;

/// <summary>The outcome of asking the parser to analyse a batch.</summary>
/// <param name="Analysis">The results, when the run completed. <c>null</c> otherwise.</param>
/// <param name="Refusal">Why the FST path declined, when the parser's exit was a recognised refusal.</param>
/// <param name="Outcome">The invocation's own outcome, which explains every case where <paramref name="Analysis"/> is null.</param>
public sealed record ParserRunResult(BatchAnalysis? Analysis, ParserRefusal? Refusal, PanGlossOutcome Outcome)
{
    public bool Succeeded => Analysis is not null;
}

/// <summary>
/// Reads one <c>pangloss batch</c> invocation back as typed analyses.
/// </summary>
/// <remarks>
/// <para>
/// <b>The project file is the input, and that is the whole point.</b> PanGloss reads <c>.fwdata</c> directly,
/// which needs no FieldWorks assemblies, and answers in FieldWorks GUIDs where the HermitCrab-XML route
/// answers in synthetic keys that cannot be tied back to the entry or rule a Proposal edited.
/// </para>
/// <para>
/// <b>Motif must save before calling this.</b> The parser reads the file, so anything uncommitted in an open
/// cache is invisible to it, the same precondition as the Dry Run's scratch copy.
/// <see cref="FwDataProjectLoader.Save"/> waits for the write to reach disk.
/// </para>
/// </remarks>
public sealed class PanGlossParser
{
    private readonly IPanGlossInvoker _invoker;

    public PanGlossParser(IPanGlossInvoker invoker) =>
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    /// <summary>Analyses <paramref name="words"/> against the grammar in <paramref name="projectFilePath"/>.</summary>
    /// <param name="perWordLimit">
    /// The per-word deadline. Words that hit it come back as <see cref="WordOutcome.TimedOut"/> and are never
    /// counted as analysis failures; see <see cref="BatchAnalysis.IsLowerBound"/>. Required: without one, a
    /// single hard word can take the process down.
    /// </param>
    /// <param name="label">Names this run in the machine queue's diagnostics.</param>
    public async Task<ParserRunResult> AnalyseBatchAsync(
        string projectFilePath, IReadOnlyList<string> words, ParserEngine engine, TimeSpan perWordLimit,
        string label, CancellationToken cancellationToken)
    {
        var outcome = await _invoker.RunAsync(
            new PanGlossRequest.Batch(projectFilePath, words, perWordLimit), label, cancellationToken)
            .ConfigureAwait(false);

        switch (outcome)
        {
            case PanGlossOutcome.Completed completed:
                var analysis = new BatchAnalysis(
                    Words: BatchTsvParser.Parse(completed.Output),
                    Engine: engine,
                    PerWordTimeoutMs: (int)perWordLimit.TotalMilliseconds,
                    ProjectPath: projectFilePath,
                    Warnings: ExtractWarnings(completed.StandardError));
                return new ParserRunResult(analysis, null, outcome);
            case PanGlossOutcome.Refused refused when ParserRefusalRecognizer.Recognize(refused.StandardError) is { } refusal:
                return new ParserRunResult(null, refusal, outcome);
            default:
                return new ParserRunResult(null, null, outcome);
        }
    }

    /// <summary>Keeps the parser's warnings, ignored elsewhere they'd taint the coverage figure.</summary>
    private static IReadOnlyList<string> ExtractWarnings(string stdErr) =>
        stdErr.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("warning:", StringComparison.OrdinalIgnoreCase)
                        || l.StartsWith("capability:", StringComparison.OrdinalIgnoreCase))
            .ToList();
}
