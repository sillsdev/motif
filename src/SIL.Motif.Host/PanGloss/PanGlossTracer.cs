using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Traces one word and preserves the parser diagnostic document.</summary>
public sealed class PanGlossTracer : IPanGlossTracer
{
    public static readonly TimeSpan DefaultTimeout = Timeout.InfiniteTimeSpan;

    private readonly IPanGlossInvoker _invoker;
    private readonly StepCap? _stepLimit;

    /// <summary>Traces through <paramref name="invoker"/>; <paramref name="stepLimit"/> is passed to the parser
    /// when given, and otherwise PanGloss's own runaway guard applies.</summary>
    public PanGlossTracer(IPanGlossInvoker invoker, StepCap? stepLimit = null)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _stepLimit = stepLimit;
    }

    public async Task<PanGlossTraceOutcome> TraceAsync(
        string grammarPath, string word, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(grammarPath)) throw new ArgumentException("Required.", nameof(grammarPath));
        if (string.IsNullOrEmpty(word)) throw new ArgumentException("Required.", nameof(word));
        var cap = timeout ?? DefaultTimeout;
        if (cap != Timeout.InfiniteTimeSpan && cap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "A trace timeout must be positive or infinite.");

        var outcome = await _invoker.RunAsync(
            new PanGlossRequest.Trace(grammarPath, word) { StepLimit = _stepLimit }, "trace:" + word, cancellationToken, cap).ConfigureAwait(false);

        return outcome switch
        {
            PanGlossOutcome.Completed completed => Interpret(word, completed),
            PanGlossOutcome.Cancelled => new PanGlossTraceOutcome.Cancelled(word),
            PanGlossOutcome.TimedOut timedOut => new PanGlossTraceOutcome.Incomplete(word, null, null, timedOut.Message),
            PanGlossOutcome.Refused refused => new PanGlossTraceOutcome.Declined(word, refused.Message)
                { ParserRefusal = refused },
            PanGlossOutcome.Unavailable unavailable => new PanGlossTraceOutcome.Unavailable(word, unavailable.Message)
            {
                ExecutableMissing = unavailable.ExecutableMissing,
            },
            _ => new PanGlossTraceOutcome.Incomplete(word, null, null, outcome.Message),
        };
    }

    /// <summary>
    /// Reads what a resident parser returned as if it were a cold process's exit code and standard output: a nonzero exit
    /// is accepted only when the output reports a stop at a limit, exactly as <see cref="PanGlossRequest.Trace"/> accepts it;
    /// any other nonzero exit is <see cref="PanGlossTraceOutcome.Malformed"/>, which the warm caller hands to the cold path.
    /// </summary>
    internal static PanGlossTraceOutcome Interpret(string word, int exitCode, string output, TimeSpan elapsed)
    {
        if (exitCode != 0 && !new PanGlossRequest.Trace(string.Empty, word).AcceptsNonzeroExit(output))
            return new PanGlossTraceOutcome.Malformed(word, output, $"The resident parser reported exit code {exitCode}.");
        return Interpret(word, new PanGlossOutcome.Completed(output, string.Empty, elapsed));
    }

    internal static PanGlossTraceOutcome Interpret(string word, PanGlossOutcome.Completed completed)
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
            ? $"The parser stopped at a search limit after {details.Steps:N0} analysis attempts, so this trace is not the whole search."
            : "The parser stopped at its own time limit, so this trace is not the whole search.";
        return new PanGlossTraceOutcome.Incomplete(word, document.Root, summary, reason)
        {
            Details = details,
            Document = document,
        };
    }
}
