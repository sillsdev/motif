using SIL.LCModel;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>Reads the saved project through a private copy and reports Drift for each collected change.</summary>
    public static CommandOutcome<PreflightResponse> Preflight(PreflightRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var id = NormalizeId(request.ProposalId);
                var (_, proposal) = new ProposalRepository(database).GetFinalized(CanonicalId.Parse(id));
                var currentBaseline = new BaselineRepository(database)
                    .GetCurrent(ProjectWorkspaceKey.Compute(project));
                return ProjectReadCache.ReadCurrent(project, currentBaseline,
                    File.GetLastWriteTimeUtc(project.FullFwDataPath).Ticks, (cache, _) =>
                        CommandOutcome<PreflightResponse>.Success(new PreflightResponse(id,
                            ChangeFitPreflight.Check(cache, proposal, currentBaseline?.Token))));
            }
            // A save interrupted while copying remains retryable at the store boundary.
            catch (Exception ex) when (ex is not (LcmFileLockedException or ProjectSavingException
                or ProjectBaselineBusyException))
            {
                return CommandOutcome<PreflightResponse>.Refused(new Refusal(
                    "preflight.unavailable", FailureReason.Refused, ex.Message,
                    Fact(("proposalId", request.ProposalId))));
            }
        });
    }
}
