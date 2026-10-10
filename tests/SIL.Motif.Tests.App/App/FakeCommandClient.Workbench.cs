using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

// Unconfigured queries return an empty value or a refusal, depending on the query.
public sealed partial class FakeCommandClient
{
    private SIL.Motif.App.ViewModels.WorkspaceSelection? _readerOwner;
    internal SIL.Motif.App.ViewModels.WorkspaceSelection ReaderOwner =>
        _readerOwner ??= new SIL.Motif.App.ViewModels.WorkspaceSelection(this);

    public Func<WordContextRequest, CancellationToken, Task<CommandOutcome<WordContextResponse>>>? WordContextHandler
        { get; set; }
    public List<WordContextRequest> WordContextRequests { get; } = [];
    public Task<CommandOutcome<WordContextResponse>> ReadWordContextAsync(
        WordContextRequest request, CancellationToken cancellationToken)
    {
        WordContextRequests.Add(request);
        return WordContextHandler?.Invoke(request, cancellationToken) ?? Completed(new WordContextResponse(request.Word, false));
    }

    private Func<OverviewRequest, CancellationToken, Task<CommandOutcome<OverviewResponse>>>
        _overview = (_, _) => Refused<OverviewResponse>(
            new Refusal("overview.not-configured", FailureReason.Refused, "No Overview configured."));

    private Func<TimingRequest, CancellationToken, Task<CommandOutcome<TimingResponse>>>
        _timing = (_, _) => Refused<TimingResponse>(
            new Refusal("timing.not-configured", FailureReason.Refused, "No Timing configured."));

    private Func<string, CancellationToken, Task<CommandOutcome<CurrentEvidenceSnapshot>>> _currentEvidence =
        (_, _) => Refused<CurrentEvidenceSnapshot>(
            new Refusal("evidence.not-configured", FailureReason.Refused, "No current evidence configured."));

    private Func<ProjectHistoryRequest, CancellationToken, Task<CommandOutcome<ProjectHistoryResponse>>>
        _projectHistory = (_, _) => Completed(new ProjectHistoryResponse([]));

    private Func<GrammarCheckRequest, CancellationToken, Task<CommandOutcome<GrammarCheckResponse>>>
        _checkGrammar = (_, _) => Completed(new GrammarCheckResponse([], HasBaseline: true));

    private GrammarCheckResponse? _storedGrammarCheck;

    private Func<WordTraceRequest, CancellationToken, Task<CommandOutcome<WordTraceResponse>>>
        _traceWord = (_, _) => Refused<WordTraceResponse>(
            new Refusal("trace.not-configured", FailureReason.Refused, "No trace configured."));

    private Func<InspectRequest, CancellationToken, Task<CommandOutcome<InspectResponse>>>
        _inspect = (_, _) => Refused<InspectResponse>(
            new Refusal("inspect.not-configured", FailureReason.Refused, "No inspection configured."));

    public List<InspectRequest> InspectRequests { get; } = [];
    public List<ProjectHistoryRequest> ProjectHistoryRequests { get; } = [];
    public List<OverviewRequest> OverviewRequests { get; } = [];
    public List<GrammarCheckRequest> CheckGrammarRequests { get; } = [];
    public List<GrammarCheckRequest> StoredGrammarCheckRequests { get; } = [];
    public List<WordTraceRequest> TraceWordRequests { get; } = [];
    public List<TimingRequest> TimingRequests { get; } = [];
    public List<string> CurrentEvidenceRequests { get; } = [];
    public Func<string, CancellationToken, Task<CommandOutcome<CurrentEvidenceSnapshot>>>? CurrentEvidenceHandler
        { get; set; }

    public void OnProjectHistory(
        Func<ProjectHistoryRequest, CancellationToken, Task<CommandOutcome<ProjectHistoryResponse>>> behavior) =>
        _projectHistory = behavior;

    public void OnOverview(
        Func<OverviewRequest, CancellationToken, Task<CommandOutcome<OverviewResponse>>> behavior) =>
        _overview = behavior;

    public void OverviewCompletesWith(OverviewResponse response) => OnOverview((_, _) => Completed(response));

    public void OnTiming(
        Func<TimingRequest, CancellationToken, Task<CommandOutcome<TimingResponse>>> behavior) =>
        _timing = behavior;

    public void TimingCompletesWith(TimingResponse response) => OnTiming((_, _) => Completed(response));

    public void ReadCurrentEvidenceCompletesWith(CurrentEvidenceSnapshot snapshot) =>
        _currentEvidence = (_, _) => Completed(snapshot);

    public void OnReadCurrentEvidence(
        Func<string, CancellationToken, Task<CommandOutcome<CurrentEvidenceSnapshot>>> behavior) =>
        _currentEvidence = behavior;

    public void ProjectHistoryIs(ProjectHistoryResponse response) => OnProjectHistory((_, _) => Completed(response));

    public void OnCheckGrammar(
        Func<GrammarCheckRequest, CancellationToken, Task<CommandOutcome<GrammarCheckResponse>>> behavior) =>
        _checkGrammar = behavior;

    public void CheckGrammarCompletesWith(GrammarCheckResponse response) =>
        OnCheckGrammar((_, _) => Completed(response));

    public void CheckGrammarRefusesWith(Refusal refusal) =>
        OnCheckGrammar((_, _) => Refused<GrammarCheckResponse>(refusal));

    /// <summary>What the stored grammar check read answers; nothing stored until a test says otherwise.</summary>
    public void StoredGrammarCheckIs(GrammarCheckResponse? response) => _storedGrammarCheck = response;

    public Func<GrammarCheckRequest, CancellationToken, Task<CommandOutcome<StoredGrammarCheckResponse>>>?
        StoredGrammarCheckHandler { get; set; }

    public void OnTraceWord(
        Func<WordTraceRequest, CancellationToken, Task<CommandOutcome<WordTraceResponse>>> behavior) =>
        _traceWord = behavior;

    public void TraceWordCompletesWith(WordTraceResponse response) => OnTraceWord((_, _) => Completed(response));

    public void OnInspect(Func<InspectRequest, CancellationToken, Task<CommandOutcome<InspectResponse>>> behavior) =>
        _inspect = behavior;

    public Task<CommandOutcome<InspectResponse>> InspectAsync(InspectRequest request, CancellationToken cancellationToken)
    {
        InspectRequests.Add(request);
        return _inspect(request, cancellationToken);
    }

    public Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken)
    {
        ProjectHistoryRequests.Add(request);
        return _projectHistory(request, cancellationToken);
    }

    public Task<CommandOutcome<OverviewResponse>> OverviewAsync(
        OverviewRequest request, CancellationToken cancellationToken)
    {
        OverviewRequests.Add(request);
        return _overview(request, cancellationToken);
    }

    public Task<CommandOutcome<CurrentEvidenceSnapshot>> ReadCurrentEvidenceAsync(
        string projectPath, CancellationToken cancellationToken)
    {
        CurrentEvidenceRequests.Add(projectPath);
        if (CurrentEvidenceHandler is { } handler) return handler(projectPath, cancellationToken);
        return _currentEvidence(projectPath, cancellationToken);
    }

    public Task<CommandOutcome<TimingResponse>> TimingAsync(
        TimingRequest request, CancellationToken cancellationToken)
    {
        TimingRequests.Add(request);
        return _timing(request, cancellationToken);
    }

    public Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken)
    {
        CheckGrammarRequests.Add(request);
        return _checkGrammar(request, cancellationToken);
    }

    public Task<CommandOutcome<StoredGrammarCheckResponse>> ReadStoredGrammarCheckAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken)
    {
        StoredGrammarCheckRequests.Add(request);
        if (StoredGrammarCheckHandler is { } handler) return handler(request, cancellationToken);
        return Completed(new StoredGrammarCheckResponse(_storedGrammarCheck));
    }

    public Func<OpenSelectionReaderRequest, CancellationToken, Task<CommandOutcome<SelectionReader>>>?
        SelectionReaderHandler { get; set; }
    public List<OpenSelectionReaderRequest> OpenSelectionReaderRequests { get; } = [];

    public Task<CommandOutcome<SelectionReader>> OpenSelectionReaderAsync(
        OpenSelectionReaderRequest request, CancellationToken cancellationToken)
    {
        OpenSelectionReaderRequests.Add(request);
        return SelectionReaderHandler?.Invoke(request, cancellationToken) ??
            Refused<SelectionReader>(new Refusal("selection-reader.no-baseline", FailureReason.Refused,
                "Capture a Baseline before opening its Selection reader."));
    }

    public Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken, IProgress<AssessmentProgress>? progress = null)
    {
        TraceWordRequests.Add(request);
        return _traceWord(request, cancellationToken);
    }
}
