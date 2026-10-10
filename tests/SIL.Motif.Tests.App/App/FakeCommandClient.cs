using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A deterministic <see cref="ICommandClient"/> for view-model tests: no process, no Avalonia, and no
/// wall-clock race. Each of the four commands is independently scripted to complete, refuse, replay
/// progress steps first, or block until its caller cancels — the block resolves purely from the
/// <see cref="CancellationToken"/> passed to the call, never from a timer.
/// </summary>
public sealed partial class FakeCommandClient : ICommandClient
{
    private readonly List<UsageLogEntry> _usageEntries = [];
    private readonly UsageRecorder _usageRecorder;

    public FakeCommandClient()
    {
        _usageRecorder = new UsageRecorder(new TestUsageLogSink(_usageEntries));
        _setDefaultSelection = SaveDefaultSelection;
        _setSelectionLimits = SaveSelectionLimits;
    }

    public IReadOnlyList<UsageLogEntry> UsageEntries => _usageEntries;

    public IDisposable BeginUsageAction(string command, params string[] argumentShape) =>
        _usageRecorder.BeginAction(command, argumentShape);

    private Func<BaselineCaptureRequest, CancellationToken, Task<CommandOutcome<BaselineCaptureResponse>>>
        _captureBaseline = (_, _) => throw NotConfigured(nameof(CaptureBaselineAsync));

    private Func<AssessRequest, IProgress<AssessmentProgress>, CancellationToken,
        Task<CommandOutcome<AssessCommandResponse>>> _assess =
        (_, _, _) => throw NotConfigured(nameof(AssessAsync));

    private Func<StatsRequest, CancellationToken, Task<CommandOutcome<StatsCommandResponse>>> _stats =
        (_, _) => throw NotConfigured(nameof(StatsAsync));

    private Func<HandoffRequest, IProgress<AssessmentProgress>, CancellationToken,
        Task<CommandOutcome<HandoffCommandResponse>>> _handoff =
        (_, _, _) => throw NotConfigured(nameof(HandoffAsync));

    private Func<CancellationToken, Task<IReadOnlyList<KnownProjectSummary>>> _listKnownProjects =
        _ => throw NotConfigured(nameof(ListKnownProjectsAsync));

    private Func<CurrentBaselineRequest, CancellationToken, Task<CommandOutcome<CurrentBaselineResponse>>>
        _currentBaseline = (_, _) => throw NotConfigured(nameof(GetCurrentBaselineAsync));

    private Func<string, CommandOutcome<RestoredBackup>> _restoreBackup =
        _ => throw NotConfigured(nameof(RestoreBackupAsync));

    /// <summary>The backups the window asked to restore, in order.</summary>
    public List<string> RestoredBackups { get; } = [];

    /// <summary>Answers every backup restore with <paramref name="behavior"/>.</summary>
    public void RestoreBackupIs(Func<string, CommandOutcome<RestoredBackup>> behavior) => _restoreBackup = behavior;

    public Task<CommandOutcome<RestoredBackup>> RestoreBackupAsync(string backupPath, CancellationToken cancellationToken)
    {
        RestoredBackups.Add(backupPath);
        return Task.FromResult(_restoreBackup(backupPath));
    }

    // A project with no stored Parsimony Report answers this refusal, so a default fake is a project with none.
    private Func<ReadLatestParsimonyReportRequest, CancellationToken,
        Task<CommandOutcome<ParsimonyLatestReportResponse>>> _readLatestParsimony = (_, _) =>
            Refused<ParsimonyLatestReportResponse>(new Refusal("parsimony.no-report", FailureReason.NotFound,
                "No Parsimony Report yet."));

    private Func<ShowParsimonyReportRequest, CancellationToken, Task<CommandOutcome<ParsimonyReportResponse>>>
        _readParsimonyReport = (_, _) => throw NotConfigured(nameof(ReadParsimonyReportAsync));

    private Func<TextInventoryRequest, CancellationToken, Task<CommandOutcome<TextInventoryResponse>>>
        _listTexts = (_, _) => Completed(new TextInventoryResponse([], HasBaseline: true));

    private Func<ReadDefaultSelectionRequest, CancellationToken,
        Task<CommandOutcome<DefaultSelectionResponse>>> _readDefaultSelection =
        (_, _) => Completed(new DefaultSelectionResponse(null));

    private Func<SetDefaultSelectionRequest, CancellationToken,
        Task<CommandOutcome<DefaultSelectionResponse>>> _setDefaultSelection = null!;

    private Func<SetSelectionLimitsRequest, CancellationToken,
        Task<CommandOutcome<NamedSelectionProjection>>> _setSelectionLimits = null!;
    private NamedSelectionProjection? _currentSelection;

    private Func<SkipSetupRequest, CancellationToken, Task<CommandOutcome<ProjectSetupResponse>>> _skipSetup =
        (_, _) => Completed(new ProjectSetupResponse(true));

    private Func<ShowConfigRequest, CancellationToken,
        Task<CommandOutcome<ProjectConfigurationProjection>>> _showConfig =
        (_, _) => Completed(new ProjectConfigurationProjection(true, true,
            [new AssessmentScopeProjection("default", "all words", "pangloss", [], 1000,
                SIL.Motif.Contract.Assess.StepCap.Default)]));

    private Func<string, CancellationToken, Task<CommandOutcome<ParserStepRate>>> _readParserStepRate =
        (_, _) => Completed(StepLimitEstimator.TypicalMachineRate);

    public List<BaselineCaptureRequest> CaptureBaselineRequests { get; } = [];
    public List<AssessRequest> AssessRequests { get; } = [];
    public List<StatsRequest> StatsRequests { get; } = [];
    public List<HandoffRequest> HandoffRequests { get; } = [];
    public List<CurrentBaselineRequest> CurrentBaselineRequests { get; } = [];
    public List<TextInventoryRequest> ListTextsRequests { get; } = [];
    public List<ReadDefaultSelectionRequest> DefaultSelectionRequests { get; } = [];
    public List<SetDefaultSelectionRequest> SetDefaultSelectionRequests { get; } = [];
    public List<SetSelectionLimitsRequest> SetSelectionLimitsRequests { get; } = [];
    public List<SkipSetupRequest> SkipSetupRequests { get; } = [];
    public List<ShowConfigRequest> ShowConfigRequests { get; } = [];
    public List<string> ParserStepRateRequests { get; } = [];
    public Func<ReadDefaultSelectionRequest, CancellationToken,
        Task<CommandOutcome<DefaultSelectionResponse>>>? DefaultSelectionHandler { get; set; }

    public void DefaultSelectionCompletesWith(NamedSelectionProjection selection)
    {
        _currentSelection = selection;
        _readDefaultSelection = (_, _) => Completed(new DefaultSelectionResponse(selection));
    }

    public void OnSetSelectionLimits(
        Func<SetSelectionLimitsRequest, CancellationToken, Task<CommandOutcome<NamedSelectionProjection>>> behavior) =>
        _setSelectionLimits = behavior;

    public void OnCaptureBaseline(
        Func<BaselineCaptureRequest, CancellationToken, Task<CommandOutcome<BaselineCaptureResponse>>> behavior) =>
        _captureBaseline = behavior;

    public void CaptureBaselineCompletesWith(BaselineCaptureResponse response) =>
        OnCaptureBaseline((_, _) => Completed(response));

    public void CaptureBaselineRefusesWith(Refusal refusal) =>
        OnCaptureBaseline((_, _) => Refused<BaselineCaptureResponse>(refusal));

    public void CaptureBaselineBlocksUntilCancelled(Refusal onCancelled) =>
        OnCaptureBaseline((_, cancellationToken) => BlockUntilCancelled<BaselineCaptureResponse>(
            cancellationToken, onCancelled));

    public void OnAssess(
        Func<AssessRequest, IProgress<AssessmentProgress>, CancellationToken,
            Task<CommandOutcome<AssessCommandResponse>>> behavior) =>
        _assess = behavior;

    public void AssessCompletesWith(
        AssessCommandResponse response, params AssessmentProgress[] progressSteps) =>
        OnAssess((_, progress, _) => ReportThenComplete(progress, progressSteps, response));

    public void AssessRefusesWith(
        Refusal refusal, params AssessmentProgress[] progressSteps) =>
        OnAssess((_, progress, _) => ReportThenRefuse<AssessCommandResponse>(progress, progressSteps, refusal));

    public void AssessBlocksUntilCancelled(
        Refusal onCancelled, params AssessmentProgress[] progressSteps) =>
        OnAssess((_, progress, cancellationToken) =>
        {
            Report(progress, progressSteps);
            return BlockUntilCancelled<AssessCommandResponse>(cancellationToken, onCancelled);
        });

    public void OnStats(
        Func<StatsRequest, CancellationToken, Task<CommandOutcome<StatsCommandResponse>>> behavior) =>
        _stats = behavior;

    public void StatsCompletesWith(StatsCommandResponse response) =>
        OnStats((_, _) => Completed(response));

    public void StatsRefusesWith(Refusal refusal) =>
        OnStats((_, _) => Refused<StatsCommandResponse>(refusal));

    public void OnHandoff(
        Func<HandoffRequest, IProgress<AssessmentProgress>, CancellationToken,
            Task<CommandOutcome<HandoffCommandResponse>>> behavior) =>
        _handoff = behavior;

    public void HandoffCompletesWith(
        HandoffCommandResponse response, params AssessmentProgress[] progressSteps) =>
        OnHandoff((_, progress, _) => ReportThenComplete(progress, progressSteps, response));

    public void HandoffRefusesWith(
        Refusal refusal, params AssessmentProgress[] progressSteps) =>
        OnHandoff((_, progress, _) => ReportThenRefuse<HandoffCommandResponse>(progress, progressSteps, refusal));

    public void HandoffBlocksUntilCancelled(
        Refusal onCancelled, params AssessmentProgress[] progressSteps) =>
        OnHandoff((_, progress, cancellationToken) =>
        {
            Report(progress, progressSteps);
            return BlockUntilCancelled<HandoffCommandResponse>(cancellationToken, onCancelled);
        });

    public void OnListKnownProjects(
        Func<CancellationToken, Task<IReadOnlyList<KnownProjectSummary>>> behavior) =>
        _listKnownProjects = behavior;

    public void KnownProjectsListIs(IReadOnlyList<KnownProjectSummary> projects) =>
        OnListKnownProjects(_ => Task.FromResult(projects));

    public void OnGetCurrentBaseline(
        Func<CurrentBaselineRequest, CancellationToken, Task<CommandOutcome<CurrentBaselineResponse>>> behavior) =>
        _currentBaseline = behavior;

    public void CurrentBaselineCompletesWith(CurrentBaselineResponse response) =>
        OnGetCurrentBaseline((_, _) => Completed(response));

    public void CurrentBaselineRefusesWith(Refusal refusal) =>
        OnGetCurrentBaseline((_, _) => Refused<CurrentBaselineResponse>(refusal));

    public void OnReadLatestParsimony(
        Func<ReadLatestParsimonyReportRequest, CancellationToken, Task<CommandOutcome<ParsimonyLatestReportResponse>>>
            behavior) => _readLatestParsimony = behavior;

    public void OnReadParsimonyReport(
        Func<ShowParsimonyReportRequest, CancellationToken, Task<CommandOutcome<ParsimonyReportResponse>>> behavior) =>
        _readParsimonyReport = behavior;

    public List<ReadLatestParsimonyReportRequest> ReadLatestParsimonyRequests { get; } = [];

    public List<ShowParsimonyReportRequest> ShowParsimonyReportRequests { get; } = [];

    public void OnListTexts(
        Func<TextInventoryRequest, CancellationToken, Task<CommandOutcome<TextInventoryResponse>>> behavior) =>
        _listTexts = behavior;

    public void ListTextsCompletesWith(TextInventoryResponse response) =>
        OnListTexts((_, _) => Completed(response));

    public void ListTextsRefusesWith(Refusal refusal) =>
        OnListTexts((_, _) => Refused<TextInventoryResponse>(refusal));

    public void DefaultSelectionIs(NamedSelectionProjection? selection)
    {
        _currentSelection = selection;
        _readDefaultSelection = (_, _) => Completed(new DefaultSelectionResponse(selection));
        _setDefaultSelection = SaveDefaultSelection;
        _setSelectionLimits = SaveSelectionLimits;
    }

    public void DefaultSelectionResponseIs(DefaultSelectionResponse response)
    {
        _currentSelection = response.Selection;
        _readDefaultSelection = (_, _) => Completed(response);
        _setDefaultSelection = SaveDefaultSelection;
        _setSelectionLimits = SaveSelectionLimits;
    }

    public void OnSetDefaultSelection(
        Func<SetDefaultSelectionRequest, CancellationToken,
            Task<CommandOutcome<DefaultSelectionResponse>>> behavior) =>
        _setDefaultSelection = behavior;

    public void OnSkipSetup(
        Func<SkipSetupRequest, CancellationToken, Task<CommandOutcome<ProjectSetupResponse>>> behavior) =>
        _skipSetup = behavior;

    public void SetDefaultSelectionRefusesWith(Refusal refusal) =>
        OnSetDefaultSelection((_, _) => Refused<DefaultSelectionResponse>(refusal));

    public void OnShowConfig(
        Func<ShowConfigRequest, CancellationToken,
            Task<CommandOutcome<ProjectConfigurationProjection>>> behavior) =>
        _showConfig = behavior;

    public void OnReadParserStepRate(
        Func<string, CancellationToken, Task<CommandOutcome<ParserStepRate>>> behavior) =>
        _readParserStepRate = behavior;

    public Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken)
    {
        CaptureBaselineRequests.Add(request);
        return _captureBaseline(request, cancellationToken);
    }

    public Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(CancellationToken cancellationToken) =>
        _listKnownProjects(cancellationToken);

    public Task<CommandOutcome<ParsimonyLatestReportResponse>> ReadLatestParsimonyReportAsync(
        ReadLatestParsimonyReportRequest request, CancellationToken cancellationToken)
    {
        ReadLatestParsimonyRequests.Add(request);
        return _readLatestParsimony(request, cancellationToken);
    }

    public Task<CommandOutcome<ParsimonyReportResponse>> ReadParsimonyReportAsync(
        ShowParsimonyReportRequest request, CancellationToken cancellationToken)
    {
        ShowParsimonyReportRequests.Add(request);
        return _readParsimonyReport(request, cancellationToken);
    }

    public Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken)
    {
        CurrentBaselineRequests.Add(request);
        return _currentBaseline(request, cancellationToken);
    }

    public Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken)
    {
        ListTextsRequests.Add(request);
        return _listTexts(request, cancellationToken);
    }

    public Task<CommandOutcome<DefaultSelectionResponse>> ReadDefaultSelectionAsync(
        ReadDefaultSelectionRequest request, CancellationToken cancellationToken)
    {
        DefaultSelectionRequests.Add(request);
        if (DefaultSelectionHandler is { } handler) return handler(request, cancellationToken);
        return _readDefaultSelection(request, cancellationToken);
    }

    public Task<CommandOutcome<DefaultSelectionResponse>> SetDefaultSelectionAsync(
        SetDefaultSelectionRequest request, CancellationToken cancellationToken)
    {
        SetDefaultSelectionRequests.Add(request);
        return _setDefaultSelection(request, cancellationToken);
    }

    public Task<CommandOutcome<NamedSelectionProjection>> SetSelectionLimitsAsync(
        SetSelectionLimitsRequest request, CancellationToken cancellationToken)
    {
        SetSelectionLimitsRequests.Add(request);
        return _setSelectionLimits(request, cancellationToken);
    }

    public Task<CommandOutcome<ProjectSetupResponse>> SkipSetupAsync(
        SkipSetupRequest request, CancellationToken cancellationToken)
    {
        SkipSetupRequests.Add(request);
        return _skipSetup(request, cancellationToken);
    }

    public Task<CommandOutcome<ProjectConfigurationProjection>> ShowConfigAsync(
        ShowConfigRequest request, CancellationToken cancellationToken)
    {
        ShowConfigRequests.Add(request);
        return _showConfig(request, cancellationToken);
    }

    public Task<CommandOutcome<ParserStepRate>> ReadParserStepRateAsync(
        string projectPath, CancellationToken cancellationToken)
    {
        ParserStepRateRequests.Add(projectPath);
        return _readParserStepRate(projectPath, cancellationToken);
    }

    public Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        AssessRequests.Add(request);
        return _assess(request, progress, cancellationToken);
    }

    public Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken)
    {
        StatsRequests.Add(request);
        return _stats(request, cancellationToken);
    }

    public Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        HandoffRequests.Add(request);
        return _handoff(request, progress, cancellationToken);
    }

    private static void Report(IProgress<AssessmentProgress> progress, IReadOnlyList<AssessmentProgress> steps)
    {
        foreach (var step in steps) progress.Report(step);
    }

    private Task<CommandOutcome<DefaultSelectionResponse>> SaveDefaultSelection(
        SetDefaultSelectionRequest request, CancellationToken cancellationToken)
    {
        if (_currentSelection is { } current &&
            (StringComparer.Ordinal.Equals(current.Name, request.Name)
                ? !StringComparer.Ordinal.Equals(current.Revision, request.ExpectedRevision)
                : request.ExpectedRevision is not null))
            return Refused<DefaultSelectionResponse>(new Refusal("selection.revision-conflict",
                FailureReason.Refused, "The Selection changed before these inputs could be saved."));

        var nextRevision = _currentSelection is { } previous &&
            StringComparer.Ordinal.Equals(previous.Name, request.Name) &&
            long.TryParse(previous.Revision, out var revision)
                ? checked(revision + 1L) : 1L;
        var saved = new NamedSelectionProjection(request.Name, request.TextIds, request.AddedWords,
            _currentSelection?.CreatedUtc ?? string.Empty, string.Empty, request.Limits,
            nextRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _currentSelection = saved;
        _readDefaultSelection = (_, _) => Completed(new DefaultSelectionResponse(saved));
        return Completed(new DefaultSelectionResponse(saved));
    }

    private Task<CommandOutcome<NamedSelectionProjection>> SaveSelectionLimits(
        SetSelectionLimitsRequest request, CancellationToken cancellationToken)
    {
        if (_currentSelection is not { } current ||
            !StringComparer.Ordinal.Equals(current.Name, request.SelectionName) ||
            !StringComparer.Ordinal.Equals(current.Revision, request.ExpectedRevision))
            return Refused<NamedSelectionProjection>(new Refusal("selection.revision-conflict",
                FailureReason.Refused, "The Selection changed before its limits could be saved."));

        var revision = long.TryParse(current.Revision, out var value) ? checked(value + 1L) : 1L;
        var saved = current with
        {
            Limits = request.Limits,
            UpdatedUtc = string.Empty,
            Revision = revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        _currentSelection = saved;
        _readDefaultSelection = (_, _) => Completed(new DefaultSelectionResponse(saved));
        return Completed(saved);
    }

    private sealed class TestUsageLogSink(List<UsageLogEntry> entries) : IUsageLogSink
    {
        public void Append(UsageLogEntry entry) => entries.Add(entry);
    }

    private static Task<CommandOutcome<T>> ReportThenComplete<T>(
        IProgress<AssessmentProgress> progress, IReadOnlyList<AssessmentProgress> steps, T response)
        where T : class
    {
        Report(progress, steps);
        return Completed(response);
    }

    private static Task<CommandOutcome<T>> ReportThenRefuse<T>(
        IProgress<AssessmentProgress> progress, IReadOnlyList<AssessmentProgress> steps, Refusal refusal)
        where T : class
    {
        Report(progress, steps);
        return Refused<T>(refusal);
    }

    private static Task<CommandOutcome<T>> Completed<T>(T response) where T : class =>
        Task.FromResult(CommandOutcome<T>.Success(response));

    private static Task<CommandOutcome<T>> Refused<T>(Refusal refusal) where T : class =>
        Task.FromResult(CommandOutcome<T>.Refused(refusal));

    // Resolves only from cancellationToken, never a timer: the same call is safe under any test speed.
    private static Task<CommandOutcome<T>> BlockUntilCancelled<T>(
        CancellationToken cancellationToken, Refusal onCancelled) where T : class
    {
        var completion = new TaskCompletionSource<CommandOutcome<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetResult(CommandOutcome<T>.Refused(onCancelled)));
        return completion.Task;
    }

    private static InvalidOperationException NotConfigured(string method) =>
        new($"FakeCommandClient.{method} was called with no behavior configured.");
}
