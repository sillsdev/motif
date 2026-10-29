using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Host.PanGloss;

/// <summary>A tracer that can use a project's resident parser, given the project and the state its answer depends on.</summary>
public interface IWarmPanGlossTracer : IPanGlossTracer
{
    /// <summary>
    /// Traces on the resident parser keyed by <paramref name="projectKey"/> and <paramref name="stateToken"/> (the
    /// Baseline token), and on the cold path when that session cannot answer.
    /// </summary>
    Task<PanGlossTraceOutcome> TraceAsync(string projectKey, string stateToken, string grammarPath, string word,
        CancellationToken cancellationToken, TimeSpan? timeout = null);
}

/// <summary>
/// Asks the resident parser first and runs the cold tracer for any word it cannot answer. The reply is the same
/// document and exit code the cold <c>parse</c> produces, so both are read by <see cref="PanGlossTracer"/>'s own
/// interpretation. <paramref name="stepLimit"/> is the cap the cold tracer passes as <c>--step-cap</c>; the resident
/// parser starts with the same cap and is a different session from one started with another.
/// </summary>
public sealed class WarmFirstTracer(WarmPanGlossPool pool, IPanGlossTracer cold, PanGlossInvoker? ownedInvoker = null,
    StepCap? stepLimit = null)
    : IWarmPanGlossTracer, IDisposable
{
    public Task<PanGlossTraceOutcome> TraceAsync(string grammarPath, string word, CancellationToken cancellationToken,
        TimeSpan? timeout = null) => cold.TraceAsync(grammarPath, word, cancellationToken, timeout);

    public async Task<PanGlossTraceOutcome> TraceAsync(string projectKey, string stateToken, string grammarPath,
        string word, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var result = await pool.TryWordAsync(projectKey, stateToken, grammarPath, word, traceDetails: true,
            cancellationToken, stepLimit).ConfigureAwait(false);
        switch (result)
        {
            case WarmParseResult.Cancelled:
                return new PanGlossTraceOutcome.Cancelled(word);
            case WarmParseResult.Completed completed:
                var interpreted = PanGlossTracer.Interpret(word, completed.ExitCode, completed.Json, completed.Elapsed);
                // A reply Motif cannot read is the resident parser's fault, not the word's: the cold path decides.
                if (interpreted is not PanGlossTraceOutcome.Malformed)
                    return interpreted with { Steps = completed.Steps, WorkSteps = completed.WorkSteps };
                break;
        }
        return await cold.TraceAsync(grammarPath, word, cancellationToken, timeout).ConfigureAwait(false);
    }

    public void Dispose() => ownedInvoker?.Dispose();
}
