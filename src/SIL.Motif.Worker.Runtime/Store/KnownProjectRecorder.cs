using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Worker.Store;

/// <summary>Shares the machine-store registration policy used by command and CLI entry points.</summary>
public static class KnownProjectRecorder
{
    /// <summary>Records a project, returning the storage cause when the machine store is unavailable.</summary>
    public static Exception? TryRecord(string managedRoot, ProjectLocator project)
    {
        try
        {
            using var machine = MachineDatabase.Open(managedRoot);
            new KnownProjectRegistry(machine).Record(
                ProjectWorkspaceKey.Compute(project), project.FullFwDataPath, DateTimeOffset.UtcNow);
            return null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            InvalidDataException or NotSupportedException or ProjectTagException)
        {
            return exception;
        }
    }

    /// <summary>Records a project with its default tag, or returns the existing tag unchanged.</summary>
    public static KnownProjectRecord EnsureKnown(string managedRoot, ProjectLocator project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(project);
        using var machine = MachineDatabase.Open(managedRoot);
        return new KnownProjectRegistry(machine).EnsureTag(
            ProjectWorkspaceKey.Compute(project), project.FullFwDataPath, DateTimeOffset.UtcNow);
    }

    /// <summary>Records or updates a project with a tag chosen by a person.</summary>
    public static KnownProjectRecord SetTag(string managedRoot, ProjectLocator project, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        ArgumentNullException.ThrowIfNull(project);
        using var machine = MachineDatabase.Open(managedRoot);
        return new KnownProjectRegistry(machine).RecordWithTag(
            ProjectWorkspaceKey.Compute(project), project.FullFwDataPath, DateTimeOffset.UtcNow, tag);
    }
}
