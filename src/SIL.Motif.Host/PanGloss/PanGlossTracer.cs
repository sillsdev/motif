namespace SIL.Motif.Host.PanGloss;

/// <summary>Traces one word and preserves the parser diagnostic document.</summary>
public sealed class PanGlossTracer : IPanGlossTracer
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly IPanGlossInvoker _invoker;

    public PanGlossTracer(IPanGlossInvoker invoker) => _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

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
        if (!PanGlossTraceOutput.TryRead(completed.Output, out var document, out var error))
            return new PanGlossTraceOutcome.Malformed(word, completed.Output, error);

        if (!string.Equals(document!.Word, word, StringComparison.Ordinal))
            return new PanGlossTraceOutcome.Malformed(
                word,
                completed.Output,
                $"The trace document records word '{document.Word}', but the requested word was '{word}'.");

        var details = document.Details;
        var stopped = details.Capped || details.TimedOut;
        var summary = PanGlossTraceSummary.Derive(document.Signature, document.Root, completed: !stopped);
        if (!stopped)
            return new PanGlossTraceOutcome.Completed(word, document.Root, summary)
            {
                Details = details,
                Document = document,
            };

        var reason = details.Capped
            ? $"The parser stopped at its step cap after {details.Steps:N0} steps, so this trace is not the whole search."
            : "The parser stopped at its own time limit, so this trace is not the whole search.";
        return new PanGlossTraceOutcome.Incomplete(word, document.Root, summary, reason)
        {
            Details = details,
            Document = document,
        };
    }
}
