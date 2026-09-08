using System.Diagnostics;

namespace SIL.Motif.Host.Parser;

/// <summary>
/// Contains a just-started parser process under whatever resource governor a caller has configured.
/// </summary>
/// <remarks>
/// <para>
/// Every seam that starts a PanGloss process accepts one of these as an optional, trailing parameter, so
/// a caller with no governor configured — every test, and any host that has not built one — passes null and
/// the seam runs exactly as it did before this existed. <see cref="Contain"/> is called immediately after
/// <c>Process.Start</c> succeeds, before the caller waits on the process or the process has had a chance to
/// spawn a child of its own.
/// </para>
/// <para>
/// SIL.Motif.Host owns this interface rather than naming a concrete governor because the real one —
/// a Windows Job Object — lives in SIL.Motif.Worker, which Host does not and must not reference. Worker
/// implements this interface over its own governor and hands the implementation down to Host's seams; Host
/// never needs to know what kind of governor it was given, only that it can be asked to contain a process.
/// </para>
/// </remarks>
public interface IParserProcessGovernor
{
    /// <summary>Brings <paramref name="process"/> — and so its whole future process tree — under this governor.</summary>
    void Contain(Process process);
}
