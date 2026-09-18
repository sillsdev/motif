namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Traces one word against one grammar. The caller names a grammar, a word, and — optionally — how long to
/// wait; it never builds an argument list, reads a process's output, or tells a timeout apart from a
/// refusal. <see cref="PanGlossTracer"/> is the implementation, over <see cref="IPanGlossInvoker"/> — the one
/// way a <c>pangloss</c> process is started.
/// </summary>
public interface IPanGlossTracer
{
    /// <summary>
    /// Runs <c>pangloss parse &lt;grammarPath&gt; &lt;word&gt; --trace --trace-format json</c> and returns
    /// what came of it. Never throws for anything the parser did. <paramref name="timeout"/> overrides
    /// <see cref="PanGlossTracer.DefaultTimeout"/>; the batch's own per-word or wall-clock limits are a
    /// separate concern this method does not read.
    /// </summary>
    Task<PanGlossTraceOutcome> TraceAsync(
        string grammarPath, string word, CancellationToken cancellationToken, TimeSpan? timeout = null);
}
