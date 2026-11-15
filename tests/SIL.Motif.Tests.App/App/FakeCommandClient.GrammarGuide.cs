using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public Func<GdrListRequest, CancellationToken, Task<CommandOutcome<GdrListResponse>>>? GrammarGuideListHandler { get; set; }
    public Func<GdrShowRequest, CancellationToken, Task<CommandOutcome<GdrShowResponse>>>? GrammarGuideShowHandler { get; set; }
    public List<GdrListRequest> GrammarGuideListRequests { get; } = [];
    public List<GdrShowRequest> GrammarGuideShowRequests { get; } = [];

    public Task<CommandOutcome<GdrListResponse>> ListGrammarGuideAsync(
        GdrListRequest request, CancellationToken cancellationToken)
    {
        GrammarGuideListRequests.Add(request);
        return GrammarGuideListHandler?.Invoke(request, cancellationToken) ?? Completed(new GdrListResponse(false, []));
    }

    public Task<CommandOutcome<GdrShowResponse>> ShowGrammarGuideAsync(
        GdrShowRequest request, CancellationToken cancellationToken)
    {
        GrammarGuideShowRequests.Add(request);
        return GrammarGuideShowHandler?.Invoke(request, cancellationToken) ?? Refused<GdrShowResponse>(
            new Refusal("gdr.not-configured", FailureReason.NotFound, "No Grammar Design Record configured."));
    }
}
