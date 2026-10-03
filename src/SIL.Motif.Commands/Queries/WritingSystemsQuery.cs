using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

public static class WritingSystemsQuery
{
    public static CommandOutcome<WritingSystemsResponse> Query(WritingSystemsRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var evidence = new BaselineRepository(database).GetCurrentEvidence(ProjectWorkspaceKey.Compute(project));
            return CommandOutcome<WritingSystemsResponse>.Success(new(evidence is not null,
                evidence?.Summary.WritingSystems ?? []) { Baseline = evidence?.Baseline.Token });
        });
}
