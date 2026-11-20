using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Dry-runs a record file, or with a Draft name stages it; never applies.</summary>
    Task<CommandOutcome<RecordImportResponse>> StageRecordImportAsync(
        RecordImportRequest request, CancellationToken cancellationToken);
}
