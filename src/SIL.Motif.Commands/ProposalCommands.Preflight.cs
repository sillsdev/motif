using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>The fit of all collected changes in one Proposal.</summary>
public sealed record PreflightResponse(string ProposalId, IReadOnlyList<ChangeFitResult> Changes);

public static partial class ProposalCommands
{
    /// <summary>Reads the live project and reports Drift for each collected change.</summary>
    public static CommandOutcome<PreflightResponse> Preflight(PreflightRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var id = NormalizeId(request.ProposalId);
                var (_, proposal) = new ProposalRepository(database).GetFinalized(CanonicalId.Parse(id));
                using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
                return CommandOutcome<PreflightResponse>.Success(
                    new PreflightResponse(id, ChangeFitPreflight.Check(cache, proposal)));
            }
            catch (Exception ex)
            {
                return CommandOutcome<PreflightResponse>.Refused(new Refusal(
                    "preflight.unavailable", FailureReason.Refused, ex.Message,
                    Fact(("proposalId", request.ProposalId))));
            }
        });
    }
}
