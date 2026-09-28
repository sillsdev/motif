namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// The one way a <c>pangloss</c> process is started. The interface exists so a command or Assessor can be
/// exercised without a process; <see cref="PanGlossInvoker"/> is the implementation that really launches one.
/// </summary>
public interface IPanGlossInvoker
{
    /// <summary>Whether this invoker could not locate the parser executable.</summary>
    bool ExecutableMissing => false;

    /// <summary>
    /// Runs one request after machine-queue admission, inside the machine's job object, under
    /// <paramref name="wallClockCap"/> or the request's default when null. Never throws for anything the
    /// parser did; a batch without a per-word time limit has no wall-clock deadline and remains cancellable.
    /// <paramref name="label"/> names the work in the queue's diagnostics.
    /// </summary>
    Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null);
}
