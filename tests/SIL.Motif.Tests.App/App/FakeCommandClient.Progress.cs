using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public Func<string, CancellationToken, Task<CommandOutcome<ProgressRequestCount>>>? ProgressRequestCountHandler { get; set; }

    public Task<CommandOutcome<ProgressRequestCount>> ReadProgressOpenRequestCountAsync(string projectPath, CancellationToken cancellationToken) =>
        ProgressRequestCountHandler?.Invoke(projectPath, cancellationToken) ??
        Completed(new ProgressRequestCount(_requests.Count(request => request.Status == "open")));
}
