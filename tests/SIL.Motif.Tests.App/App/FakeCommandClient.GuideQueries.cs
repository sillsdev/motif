using SIL.Motif.Contract.Requests;
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

    public int GuideSessionReleases { get; private set; }

    public void ReleaseGuideSession() => GuideSessionReleases++;

    public Func<GdrRefreshTablesRequest, CancellationToken, Task<CommandOutcome<GdrRefreshTablesResponse>>>? RefreshGuideTablesHandler { get; set; }
    public List<GdrRefreshTablesRequest> RefreshGuideTablesRequests { get; } = [];

    public Task<CommandOutcome<GdrRefreshTablesResponse>> RefreshGuideTablesAsync(GdrRefreshTablesRequest request, CancellationToken cancellationToken)
    {
        RefreshGuideTablesRequests.Add(request);
        return RefreshGuideTablesHandler?.Invoke(request, cancellationToken) ?? Refused<GdrRefreshTablesResponse>(
            new Refusal("guide.not-configured", FailureReason.NotFound, "No table saving configured."));
    }

    public Func<string, string, CancellationToken, Task<GuideEvidenceResult>>? GuideEvidenceHandler { get; set; }
    public List<(string Project, string Line)> GuideEvidenceRequests { get; } = [];

    public Task<GuideEvidenceResult> RunGuideEvidenceAsync(string projectPath, string line, CancellationToken cancellationToken)
    {
        GuideEvidenceRequests.Add((projectPath, line));
        return GuideEvidenceHandler?.Invoke(projectPath, line, cancellationToken) ?? Task.FromResult(new GuideEvidenceResult(
            GuideEvidenceStatus.Refused, null, "guide-query.not-configured: No evidence configured.", "guide-query.not-configured", null, 0, new Dictionary<string, double>()));
    }
}
