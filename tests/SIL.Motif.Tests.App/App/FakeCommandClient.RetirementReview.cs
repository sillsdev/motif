using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public List<ReadRetirementReviewRequest> RetirementReviewRequests { get; } = [];

    public Func<ReadRetirementReviewRequest, CancellationToken,
        Task<CommandOutcome<RetirementReviewQueryResponse>>>? RetirementReviewHandler { get; set; }

    public void RetirementReviewCompletesWith(RetirementReviewQueryResponse response) =>
        RetirementReviewHandler = (_, _) => Completed(response);

    public Task<CommandOutcome<RetirementReviewQueryResponse>> ReadRetirementReviewAsync(
        ReadRetirementReviewRequest request, CancellationToken cancellationToken)
    {
        RetirementReviewRequests.Add(request);
        return RetirementReviewHandler?.Invoke(request, cancellationToken) ??
            Completed(new RetirementReviewQueryResponse(false, request.DraftId, "not-applicable",
                null, null, null, []));
    }
}
