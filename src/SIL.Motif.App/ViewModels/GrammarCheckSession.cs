using System.ComponentModel;
using System.Globalization;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns the current project's stored grammar check and its cancellation.</summary>
internal sealed class GrammarCheckSession : IProjectStateParticipant
{
    private readonly WorkspaceContext _context;
    private int _readGeneration;
    private CancellationTokenSource? _checkCancellation;
    private Task _checking = Task.CompletedTask;
    private BaselineToken? _checkingBaseline;

    public GrammarCheckSession(WorkspaceContext context)
    {
        _context = context;
        Grammar = new GrammarViewModel(context.Commands, context.Clock, CheckAsync);
        Grammar.Warnings.UseViewMemory(context.ViewMemory);
        Grammar.Warnings.ConfigureActions(
            row => context.HandOffWarning(WordsFor(row), new WarningHandoffScope(
                row.Message, row.GroupCode, row.AttributionState, row.IsPartialReach)),
            row => context.Assess.RerunAsync(WordsFor(row), 30_000),
            new WordRowRoutes { OpenInText = context.OpenWord });
        Grammar.PropertyChanged += OnGrammarChanged;
        Grammar.Warnings.PropertyChanged += OnGrammarChanged;
        context.PropertyChanged += OnContextChanged;
    }

    public GrammarViewModel Grammar { get; }

    private static IReadOnlyList<string> WordsFor(GrammarWarningRowViewModel row)
    {
        var rows = row.AttributionState switch
        {
            WarningDisplayState.MembershipCandidates => row.MembershipCandidateRows,
            WarningDisplayState.SpellingCandidates => row.SpellingCandidateRows,
            _ => row.WordRows,
        };
        return rows.Select(word => word.Word).Distinct(StringComparer.Ordinal).ToArray();
    }

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Independent;

    void IProjectStateParticipant.ClearProject()
    {
        _readGeneration++;
        _checkCancellation?.Cancel();
        Grammar.Warnings.SetProjectPath(null);
        Grammar.Clear();
    }

    async Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken)
    {
        Grammar.Warnings.SetProjectPath(projectPath);
        Grammar.Clear();
        var generation = ++_readGeneration;
        var baseline = _context.Baseline?.Token;
        var stored = await _context.Commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(projectPath), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _readGeneration || projectPath != _context.ProjectPath ||
            baseline != _context.Baseline?.Token || cancellationToken.IsCancellationRequested || !stored.Succeeded) return;
        Grammar.LoadStored(projectPath, stored.Value?.Check, stored.Value?.LastParserRefusal);
    }

    internal async Task EvidencePublishedAsync(CancellationToken cancellationToken)
    {
        if (Grammar.IsLoading || _context.ProjectPath is not { } path) return;
        var generation = ++_readGeneration;
        var baseline = _context.Baseline?.Token;
        var stored = await _context.Commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _readGeneration || path != _context.ProjectPath || baseline != _context.Baseline?.Token ||
            cancellationToken.IsCancellationRequested || !stored.Succeeded) return;
        Grammar.LoadStored(path, stored.Value?.Check, stored.Value?.LastParserRefusal);
    }

    internal async Task BaselineCapturedAsync(CancellationToken cancellationToken)
    {
        var path = _context.ProjectPath;
        var baseline = _context.Baseline?.Token;
        var stopping = StopAsync();
        var generation = _readGeneration;
        await stopping.ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _readGeneration || path is null || path != _context.ProjectPath ||
            baseline is null || baseline != _context.Baseline?.Token) return;
        if (!_context.IsOpeningProject) _ = StartCheck(path);
    }

    internal Task StopAsync()
    {
        _readGeneration++;
        _checkCancellation?.Cancel();
        return _checking;
    }

    private Task CheckAsync() => !_checking.IsCompleted ? _checking :
        _context.ProjectPath is { } path ? StartCheck(path) : Task.CompletedTask;

    private Task StartCheck(string path)
    {
        var generation = ++_readGeneration;
        var baseline = _context.Baseline?.Token;
        var cancellation = new CancellationTokenSource();
        _checkCancellation = cancellation;
        _checkingBaseline = baseline;
        _checking = CheckAndPublishAsync(path, baseline, generation, cancellation);
        return _checking;
    }

    private async Task CheckAndPublishAsync(string path, BaselineToken? baseline, int generation,
        CancellationTokenSource cancellation)
    {
        bool IsCurrent() => generation == _readGeneration && path == _context.ProjectPath &&
            baseline == _context.Baseline?.Token && !cancellation.IsCancellationRequested;
        try
        {
            await Grammar.SetProjectAsync(path, cancellation.Token).ConfigureAwait(true);
            if (IsCurrent()) await _context.PublishGrammarCheckedAsync(cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrent()) Grammar.LoadStored(path, null, new Refusal(
                RefusalCodes.GrammarCheckFailed, FailureReason.Refused,
                string.Format(CultureInfo.CurrentCulture, MotifAppStrings.GrammarCheckFailedFormat, exception.Message)));
        }
        finally
        {
            if (ReferenceEquals(_checkCancellation, cancellation)) _checkCancellation = null;
            cancellation.Dispose();
        }
    }

    private void OnGrammarChanged(object? sender, PropertyChangedEventArgs e) =>
        _context.GrammarSummary = new GrammarSummary(Grammar.SummaryText, Grammar.ShowTodayFindings,
            Grammar.Warnings.TodayBreakdownText) { Findings = Grammar.Warnings.Findings };

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceContext.Baseline) || _checkCancellation is null ||
            _checkingBaseline == _context.Baseline?.Token) return;
        _readGeneration++;
        _checkCancellation.Cancel();
        Grammar.Clear();
    }
}
