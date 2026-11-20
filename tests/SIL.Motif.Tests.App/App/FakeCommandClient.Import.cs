using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public Func<RecordImportRequest, CancellationToken, Task<CommandOutcome<RecordImportResponse>>>? StageRecordImportHandler { get; set; }
    public List<RecordImportRequest> StageRecordImportRequests { get; } = [];
    public Task<CommandOutcome<RecordImportResponse>> StageRecordImportAsync(
        RecordImportRequest request, CancellationToken cancellationToken)
    {
        StageRecordImportRequests.Add(request);
        return StageRecordImportHandler?.Invoke(request, cancellationToken) ?? Refused<RecordImportResponse>(
            new Refusal("record-import.unconfigured", FailureReason.Refused, "No record import configured."));
    }
}
