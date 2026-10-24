using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Reads or changes a project's machine-local tag through the person CLI.</summary>
public static class ProjectTagCommands
{
    /// <summary>Reads a project's tag, assigning its default when the project is new to this machine.</summary>
    public static CommandOutcome<ProjectTagResponse> Read(ReadProjectTagRequest request) =>
        Read(request, RunnerOptions.ResolveRoot());

    /// <summary>Reads a project's tag under a test's isolated managed root.</summary>
    public static CommandOutcome<ProjectTagResponse> Read(ReadProjectTagRequest request, string managedRoot)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.ProjectPath, project =>
        {
            var known = KnownProjectRecorder.EnsureKnown(managedRoot, project);
            return CommandOutcome<ProjectTagResponse>.Success(new ProjectTagResponse(known.ProjectTag!));
        });
    }

    /// <summary>Sets a project's tag after checking its validity and uniqueness.</summary>
    public static CommandOutcome<ProjectTagResponse> Set(SetProjectTagRequest request) =>
        Set(request, RunnerOptions.ResolveRoot());

    /// <summary>Sets a project's tag under a test's isolated managed root.</summary>
    public static CommandOutcome<ProjectTagResponse> Set(SetProjectTagRequest request, string managedRoot)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.ProjectPath, project =>
        {
            var known = KnownProjectRecorder.SetTag(managedRoot, project, request.Tag);
            return CommandOutcome<ProjectTagResponse>.Success(new ProjectTagResponse(known.ProjectTag!));
        });
    }

    private static CommandOutcome<ProjectTagResponse> Run(
        string projectPath, Func<ProjectLocator, CommandOutcome<ProjectTagResponse>> action)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            return CommandOutcome<ProjectTagResponse>.Refused(new Refusal(RefusalCodes.ProjectInvalid,
                FailureReason.InvalidArgument, $"Project file not found: '{projectPath}'."));

        var fullPath = Path.GetFullPath(projectPath);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        try
        {
            return action(project);
        }
        catch (ProjectTagException exception)
        {
            var code = exception.Problem switch
            {
                ProjectTagProblem.Invalid => RefusalCodes.ProjectTagInvalid,
                ProjectTagProblem.Taken => RefusalCodes.ProjectTagTaken,
                _ => RefusalCodes.ProjectTagUnavailable,
            };
            var reason = exception.Problem == ProjectTagProblem.Invalid
                ? FailureReason.InvalidArgument : FailureReason.Refused;
            return CommandOutcome<ProjectTagResponse>.Refused(new Refusal(code, reason, exception.Message,
                exception.Tag.Length == 0 ? null : new Dictionary<string, string> { ["tag"] = exception.Tag }));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or Microsoft.Data.Sqlite.SqliteException or ArgumentException)
        {
            return CommandOutcome<ProjectTagResponse>.Refused(new Refusal(RefusalCodes.ProjectTagUnavailable,
                FailureReason.Refused, "Motif could not use its machine store to read or set this project tag: " +
                exception.Message));
        }
    }
}
