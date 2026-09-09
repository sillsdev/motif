using System.Diagnostics;
using System.Runtime.Versioning;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Adapts <see cref="WindowsCpuJob"/> to the Host-owned <see cref="IParserProcessGovernor"/> seam.
/// </summary>
/// <remarks>Bridges the job object to the governor seam the launchers still take.</remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsCpuJobGovernor : IParserProcessGovernor
{
    private readonly WindowsCpuJob _job;

    /// <param name="job">The job every contained process is assigned to.</param>
    public WindowsCpuJobGovernor(WindowsCpuJob job) => _job = job ?? throw new ArgumentNullException(nameof(job));

    /// <inheritdoc />
    public void Contain(Process process) => _job.AssignProcess(process);
}
