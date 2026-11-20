using SIL.Motif.Contract.Responses;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public Func<GuideQueryRunRequest, CancellationToken, Task<CommandOutcome<FormsQueryResponse>>>? GuideQueryHandler { get; set; }
    public List<GuideQueryRunRequest> GuideQueryRequests { get; } = [];

    public Task<CommandOutcome<FormsQueryResponse>> RunGuideQueryAsync(GuideQueryRunRequest request, CancellationToken cancellationToken)
    {
        GuideQueryRequests.Add(request);
        return GuideQueryHandler?.Invoke(request, cancellationToken) ?? Refused<FormsQueryResponse>(
            new Refusal("guide-query.not-configured", FailureReason.NotFound, "No Guide query configured."));
    }
}
