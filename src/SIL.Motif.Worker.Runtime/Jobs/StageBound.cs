namespace SIL.Motif.Worker.Jobs;

/// <summary>Bounds one parser stage of a job by wall-clock time.</summary>
internal static class StageBound
{
    /// <summary>Runs one parser stage under a wall-clock bound; a timeout is reported, not thrown.</summary>
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
            return (default, $"The {stage} did not finish within {bound.TotalSeconds:0.###} seconds.");
        }
    }
}
