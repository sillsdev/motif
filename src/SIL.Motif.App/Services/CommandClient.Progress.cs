using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public async Task<CommandOutcome<ProgressRequestCount>> ReadProgressOpenRequestCountAsync(
        string projectPath, CancellationToken cancellationToken)
    {
        var read = await ListRequestsAsync(new ListRequestsRequest(
            projectPath, MotifProductVersion.CurrentText, "open", Limit: 1), cancellationToken).ConfigureAwait(false);
        return read.Succeeded ? CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(read.Value!.OpenCount))
            : CommandOutcome<ProgressRequestCount>.Refused(read.Refusal!);
    }
}
