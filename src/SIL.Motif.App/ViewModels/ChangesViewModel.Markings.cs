using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ChangesViewModel
{
    public async Task StageAnalysisRemovalAsync(ResultsTokenViewModel token, AnalysisMarkingAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind != AnalysisMarkingActionKind.RemoveAnalysis ||
            action.ChangeKind != ChangeKinds.RemoveAnalysis || action.StoredAnalysisId is not { } storedAnalysisId)
            throw new ArgumentException("A stored analysis removal action is required.", nameof(action));
        if (token.WordformId is not { } wordformId) return;
        await RemoveAnalysesAsync((path, revision) => new RemoveAnalysisRequest(
            path, MotifProductVersion.CurrentText, revision, ChangeId: CanonicalId.Mint().Value,
            WordformId: CanonicalId.FromGuid(wordformId).Value, Word: token.Form,
            AnalysisId: storedAnalysisId), cancellationToken).ConfigureAwait(true);
    }

    public Task StageAnalysisRemovalListAsync(IReadOnlyList<string> storedAnalysisIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storedAnalysisIds);
        var ids = storedAnalysisIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return Task.CompletedTask;
        return RemoveAnalysesAsync((path, revision) => new RemoveAnalysisRequest(
            path, MotifProductVersion.CurrentText, revision, AnalysisIds: ids), cancellationToken);
    }

    public Task StageAnalysisRemovalForTextAsync(Guid textId, CancellationToken cancellationToken = default) =>
        RemoveAnalysesAsync((path, revision) => new RemoveAnalysisRequest(
            path, MotifProductVersion.CurrentText, revision, TextId: textId), cancellationToken);

    public Task AcceptNewSetForWordAsync(ResultsTokenViewModel token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.WordformId is not { } wordformId) return Task.CompletedTask;
        return AcceptNewSetAsync(CanonicalId.FromGuid(wordformId).Value, null, false, cancellationToken);
    }

    public Task AcceptNewSetForTextAsync(Guid textId, CancellationToken cancellationToken = default) =>
        AcceptNewSetAsync(null, textId, false, cancellationToken);

    public Task AcceptNewSetForSelectionAsync(CancellationToken cancellationToken = default) =>
        AcceptNewSetAsync(null, null, true, cancellationToken);

    private async Task AcceptNewSetAsync(string? wordformId, Guid? textId, bool selection,
        CancellationToken cancellationToken)
    {
        if (ProjectPath is not { } path || AssessmentId is not { } assessmentId) return;
        var generation = _projectGeneration;
        var outcome = await _client.AcceptNewSetAsync(new AcceptNewSetRequest(
            path, MotifProductVersion.CurrentText, Snapshot.Revision, assessmentId,
            wordformId, textId, selection), cancellationToken).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken).ConfigureAwait(true);
    }

    private async Task RemoveAnalysesAsync(Func<string, string, RemoveAnalysisRequest> requestFor,
        CancellationToken cancellationToken)
    {
        if (ProjectPath is not { } path) return;
        var generation = _projectGeneration;
        var outcome = await _client.RemoveAnalysisAsync(
            requestFor(path, Snapshot.Revision), cancellationToken).ConfigureAwait(true);
        if (!IsCurrentProject(path, generation)) return;
        Accept(outcome, path, generation);
        if (outcome.Refusal?.Code == RefusalCodes.ChangeRevisionConflict)
            await ReloadAfterConflictAsync(outcome.Refusal, path, generation, cancellationToken).ConfigureAwait(true);
    }
}
