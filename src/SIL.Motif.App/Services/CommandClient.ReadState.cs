using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<WordReadStateResponse>> ReadWordStateAsync(
        WordReadStateRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ReadStateCommands.Execute(request));
}
