using System.Diagnostics;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Records the process id of every process a seam hands it, so a test can assert containment actually
/// happened rather than merely that a governor was configured to accept one.
/// </summary>
internal sealed class RecordingGovernor : IParserProcessGovernor
{
    private readonly List<int> _containedProcessIds = new();

    public IReadOnlyList<int> ContainedProcessIds => _containedProcessIds;

    public void Contain(Process process) => _containedProcessIds.Add(process.Id);
}
