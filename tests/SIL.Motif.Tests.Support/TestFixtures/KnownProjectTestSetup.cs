using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Prepares machine-local Known project records for tests of other product behavior.</summary>
public static class KnownProjectTestSetup
{
    /// <summary>Assigns a unique valid tag so a fixture does not collide with another test project.</summary>
    public static KnownProjectRecord AssignUniqueTag(
        string managedRoot, string projectPath, string projectIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectIdentity);

        var fullPath = Path.GetFullPath(projectPath);
        var project = new ProjectLocator(fullPath, projectIdentity);
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        using var machine = MachineDatabase.Open(managedRoot);
        var registry = new KnownProjectRegistry(machine);
        var seen = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var tag = "t" + Guid.NewGuid().ToString("N")[..9];
            try
            {
                return registry.RecordWithTag(workspaceKey, fullPath, seen, tag);
            }
            catch (ProjectTagException exception) when (exception.Problem == ProjectTagProblem.Taken)
            {
            }
        }

        throw new InvalidOperationException("Could not choose a unique project tag for this test fixture.");
    }
}
