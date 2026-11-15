using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Lists the Grammar Design Records in the current Baseline.</summary>
    Task<CommandOutcome<GdrListResponse>> ListGrammarGuideAsync(
        GdrListRequest request, CancellationToken cancellationToken);

    /// <summary>Reads one Grammar Design Record and its discussion from the current Baseline.</summary>
    Task<CommandOutcome<GdrShowResponse>> ShowGrammarGuideAsync(
        GdrShowRequest request, CancellationToken cancellationToken);
}
