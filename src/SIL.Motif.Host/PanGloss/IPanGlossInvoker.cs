namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// The one way a <c>pangloss</c> process is started. The interface exists so a command or Assessor can be
/// exercised without a process; <see cref="PanGlossInvoker"/> is the implementation that really launches one.
/// </summary>
public interface IPanGlossInvoker
{
    /// <summary>
    /// Runs one request after machine-queue admission, inside the machine's job object, under
    /// <paramref name="wallClockCap"/> (the invoker's default when null). Never throws for anything the
    /// parser did; <paramref name="label"/> names the work in the queue's diagnostics.
    /// </summary>
    Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null);
}
