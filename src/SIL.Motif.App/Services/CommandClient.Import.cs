using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<RecordImportResponse>> StageRecordImportAsync(
        RecordImportRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => RecordImportCommand.Run(request), cancellationToken);
}
