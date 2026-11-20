using System.Runtime.ExceptionServices;

namespace SIL.Motif.RecordStore;

/// <summary>Bounded fan-out for the verified read and the history scan.</summary>
internal static class ParallelWork
{
    /// <summary>Environment variable that overrides <see cref="DefaultWorkers"/>; a positive integer, 1 meaning sequential.</summary>
    internal const string WorkersVariable = "MOTIF_RECORD_STORE_WORKERS";

    /// <summary>The environment override, else min(4, processors): the same budget the content load already uses.</summary>
    internal static int DefaultWorkers { get; } =
        int.TryParse(Environment.GetEnvironmentVariable(WorkersVariable), out var configured) && configured > 0
            ? Math.Min(configured, 64) : Math.Min(4, Environment.ProcessorCount);

    /// <summary>
    /// Runs <paramref name="body"/> once for each chunk on at most <paramref name="workers"/> threads. A failure is
    /// reported as the sequential loop would report it: the lowest failing chunk's own first error, never an aggregate.
    /// Errors thrown outside the chunks (such as a read error) are not ordered against chunk errors. Cancellation throws <see cref="OperationCanceledException"/> and stops new chunks.
    /// </summary>
    public static void ForEachChunk(int chunks, int workers, CancellationToken ct, Action<int> body)
    {
        ct.ThrowIfCancellationRequested();
        var failures = new Exception?[chunks];
        Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers), CancellationToken = ct }, (chunk, state) =>
        {
            try { body(chunk); }
            // The cancelled token already stops new chunks; Stop after Break would throw.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception failure)
            {
                failures[chunk] = failure;
                // Break still runs every lower chunk, so the lowest failing chunk is always found.
                state.Break();
            }
        });
        ct.ThrowIfCancellationRequested();
        foreach (var failure in failures)
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
