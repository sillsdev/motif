namespace SIL.Motif.Worker.Jobs;

/// <summary>Bounds one parser stage of a job by wall-clock time.</summary>
internal static class StageBound
{
    /// <summary>
    /// Runs one parser stage under a wall-clock bound; a stop the job did not ask for is reported, not thrown. The
    /// report names who stopped it: the bound (which counts time waiting for parser capacity) or the parser queue closing.
    /// </summary>
    /// <returns>The stage's value, or why it did not finish within <paramref name="bound"/>.</returns>
    internal static async Task<(T? Value, string? TimedOut)> RunAsync<T>(string stage, TimeSpan bound,
        Func<CancellationToken, Task<T>> run, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(bound);
        try
        {
            return (await run(linked.Token).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (default, linked.IsCancellationRequested
                ? $"The {stage} did not finish within {bound.TotalSeconds:0.###} seconds and was cancelled by that stage time limit, " +
                    "which counts time waiting for parser capacity. Words this Trial parsed are unaffected."
                : $"The {stage} was cancelled by the parser queue closing, not by a job cancel or the stage time limit, " +
                    "so the worker was stopping or restarting. Words this Trial parsed are unaffected.");
        }
    }
}
