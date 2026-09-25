using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Records project setup choices that are not part of the saved Selection.</summary>
public static class ProjectSetupCommands
{
    /// <summary>Records that first-time setup was skipped for the project.</summary>
    public static CommandOutcome<ProjectSetupResponse> Skip(SkipSetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            new ProjectSetupRepository(database).MarkSkipped();
            return CommandOutcome<ProjectSetupResponse>.Success(new ProjectSetupResponse(true));
        });
    }
}
