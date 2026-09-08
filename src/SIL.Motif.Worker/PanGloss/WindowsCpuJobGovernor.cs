using System.Diagnostics;
using System.Runtime.Versioning;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Worker.PanGloss;

/// <summary>
/// Adapts <see cref="WindowsCpuJob"/> to the Host-owned <see cref="IParserProcessGovernor"/> seam.
/// </summary>
/// <remarks>
/// SIL.Motif.Host has no project reference to SIL.Motif.Worker and so cannot name <see cref="WindowsCpuJob"/>
/// itself; this type is the one place that bridges the two, letting a caller that already holds a job (one
/// admitted by <see cref="MachinePanGlossQueue"/>) hand it to a Host seam without Host ever seeing a Windows
/// Job Object.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsCpuJobGovernor : IParserProcessGovernor
{
    private readonly WindowsCpuJob _job;

    /// <param name="job">The job every contained process is assigned to.</param>
    public WindowsCpuJobGovernor(WindowsCpuJob job) => _job = job ?? throw new ArgumentNullException(nameof(job));

    /// <inheritdoc />
    public void Contain(Process process) => _job.AssignProcess(process);
}
