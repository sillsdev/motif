namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Traces one word through <see cref="IPanGlossInvoker"/>: builds the <see cref="PanGlossRequest.Trace"/>,
/// reads the parity line and the verbatim tree back out of the process's own output, and derives the
/// one-line summary that travels with it. See <see cref="IPanGlossTracer"/> for the contract this fulfils.
/// </summary>
public sealed class PanGlossTracer : IPanGlossTracer
{
    /// <summary>
    /// PanGloss has no trace-specific bound of its own — <c>parse</c> takes no <c>--step-cap</c>, and its
    /// fixed internal cap (50,000,000 steps) is a runaway guard, not a wall-clock one — and tracing runs
    /// unmerged, so it costs more than the batch pass that measured the same word. Motif imposes a timeout
    /// well short of <see cref="PanGlossInvoker.DefaultWallClockCap"/> so a single traced word can never
    /// itself approach the bound a whole batch invocation is allowed.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly IPanGlossInvoker _invoker;

    public PanGlossTracer(IPanGlossInvoker invoker) => _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    /// <inheritdoc />
    public async Task<PanGlossTraceOutcome> TraceAsync(
        string grammarPath, string word, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(grammarPath)) throw new ArgumentException("Required.", nameof(grammarPath));
        if (string.IsNullOrEmpty(word)) throw new ArgumentException("Required.", nameof(word));
        var cap = timeout ?? DefaultTimeout;
        if (cap <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), "A trace timeout must be positive.");

        var outcome = await _invoker.RunAsync(
            new PanGlossRequest.Trace(grammarPath, word), "trace:" + word, cancellationToken, cap).ConfigureAwait(false);

        return outcome switch
        {
            PanGlossOutcome.Completed completed => Interpret(word, completed),
            PanGlossOutcome.Cancelled => new PanGlossTraceOutcome.Cancelled(word),
            PanGlossOutcome.TimedOut timedOut => new PanGlossTraceOutcome.Incomplete(word, null, null, timedOut.Message),
            PanGlossOutcome.Refused refused => new PanGlossTraceOutcome.Declined(word, refused.Message),
            PanGlossOutcome.Unavailable unavailable => new PanGlossTraceOutcome.Unavailable(word, unavailable.Message),
            _ => new PanGlossTraceOutcome.Incomplete(word, null, null, outcome.Message),
        };
    }

    private static PanGlossTraceOutcome Interpret(string word, PanGlossOutcome.Completed completed)
    {
        if (!PanGlossTraceOutput.TryParse(completed.Output, out var signature, out var root))
            return new PanGlossTraceOutcome.Malformed(word, completed.Output,
                "pangloss parse --trace --trace-format json did not write the word\\tsignature line and trace tree this module reads.");
        var summary = PanGlossTraceSummary.Derive(signature, root, completed: true);
        return new PanGlossTraceOutcome.Completed(word, root, summary);
    }
}
