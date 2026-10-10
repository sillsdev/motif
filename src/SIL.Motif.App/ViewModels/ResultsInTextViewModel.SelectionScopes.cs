using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ResultsInTextViewModel
{
    private IEnumerable<SelectionSourcePosition> NativePositionsFor(AnalysisOperationScope scope) =>
        (_nativeSummary?.SourcePositions ?? []).Where(position => scope switch
        {
            AnalysisOperationScope.SelectedText => position.Location.Anchor.TextId == SelectedText?.TextId,
            AnalysisOperationScope.ChosenTexts => true,
            AnalysisOperationScope.CheckedWords => NativeIsChecked(position.Location.Anchor),
            _ => false,
        });

    private SelectionActionFacts NativeActionScope(AnalysisOperationScope scope)
    {
        if (_selectionOwner.Reader is not { } reader || _nativeSummary is null) return new SelectionActionFacts([]);
        IReadOnlySet<Guid>? texts = scope switch
        {
            AnalysisOperationScope.SelectedText => SelectedText is { } text ? new HashSet<Guid> { text.TextId } : new HashSet<Guid>(),
            AnalysisOperationScope.AssessmentSelection => null,
            _ => _nativeSummary.Texts.Select(text => text.TextId).ToHashSet(),
        };
        var keys = scope == AnalysisOperationScope.AssessmentSelection ? _nativeWords.Keys.ToArray() :
            _nativeSummary.SourcePositions.Select(position => position.Word).Distinct().ToArray();
        return reader.ResolveActionScope(keys, texts,
            excluded: scope == AnalysisOperationScope.CheckedWords && _selectAllOccurrences ? _uncheckedOccurrences : null,
            included: scope == AnalysisOperationScope.CheckedWords && !_selectAllOccurrences ? _checkedOccurrences : null);
    }

    private SelectionWordSummary[] NativeWordsFor(AnalysisOperationScope scope) =>
        NativeActionScope(scope).Words.Keys.Select(key => _nativeWords[key]).DistinctBy(word =>
            word.Actions.CandidateWordformIds.Count == 1 ? "id:" + word.Actions.CandidateWordformIds[0] :
            "form:" + word.Key.Form).ToArray();

    private WordActionTarget NativeTarget(SelectionWordSummary word, OccurrenceAnchor? occurrence = null) =>
        new(word.Key.Form, word.Actions.CandidateWordformIds.Count == 1 ? word.Actions.CandidateWordformIds[0] : null,
            occurrence, word.Assessment?.AssessmentId, _selectionOwner.Reader!.Context.ExpectedWriteContext());

    private WordActionTarget[] NativeReadTargets(AnalysisOperationScope scope)
    {
        var expected = _selectionOwner.Reader?.Context.ExpectedWriteContext();
        return NativeActionScope(scope).Occurrences.Select(anchor => _nativePositions[anchor]).Select(position => new WordActionTarget(position.Word.Form,
            position.Word.WordformId, position.Location.Anchor, _nativeWords[position.Word].Assessment?.AssessmentId,
            expected)).ToArray();
    }

    private HashSet<string> NativeAnalysisIds(AnalysisOperationScope scope) => scope == AnalysisOperationScope.SelectedText
        ? NativeActionScope(scope).ChosenAnalysisIds.Select(id => id.ToString("D")).ToHashSet(StringComparer.Ordinal)
        : NativeWordsFor(scope).SelectMany(word => word.Actions.StoredAnalysisIds).ToHashSet(StringComparer.Ordinal);

    private string NativeRemovalPreview(AnalysisOperationScope scope)
    {
        var ids = NativeAnalysisIds(scope);
        var uses = (_nativeSummary?.SourcePositions ?? []).Count(position => position.Location.AnalysisId is { } id &&
            ids.Contains(id.ToString("D")));
        var words = NativeWordsFor(scope).Count(word => word.Actions.StoredAnalysisIds.Any(ids.Contains));
        return $"{ids.Count:N0} stored analyses on {words:N0} words with {uses:N0} occurrences in the chosen Texts. " +
            "These analyses will be removed everywhere in the project.";
    }

    private ChangeViewModel[] NativeChangesFor(AnalysisOperationScope scope)
    {
        var positions = NativePositionsFor(scope).ToArray();
        var anchors = positions.Select(position => position.Location.Anchor).ToHashSet();
        var wordforms = positions.Select(position => position.Word.WordformId).OfType<Guid>()
            .Select(id => CanonicalId.FromGuid(id).Value).ToHashSet(StringComparer.Ordinal);
        return _changes.Items.Where(change => (change.WordformId is null || wordforms.Contains(change.WordformId)) &&
            (change.Occurrence is { } anchor ? anchors.Contains(anchor) : change.WordformId is not null))
            .DistinctBy(change => change.ChangeId).ToArray();
    }

    private bool CanNativeAcceptNewSet(AnalysisOperationScope scope)
    {
        if (_selectionOwner.Reader is null || scope == AnalysisOperationScope.ChosenTexts) return false;
        var words = NativeWordsFor(scope);
        return words.Length > 0 && words.All(word => word.Actions.CandidateWordformIds.Count == 1 &&
            word.Assessment?.Morphology is { InvalidShape: false } && word.Actions.Classification?.Class is not
                (null or SelectionAnalysisClass.Capped or SelectionAnalysisClass.NotAssessed or SelectionAnalysisClass.Refused));
    }

    private async Task AddNativeParserReadingsAsync(AnalysisOperationScope scope, string? groupId = null)
    {
        var actions = NativeWordsFor(scope).Where(word => word.Actions.CanAddParserReading).SelectMany(word =>
            (word.Actions.Classification?.Readings ?? []).Where(reading => reading.IsParserOnly).Select(reading =>
                (Target: NativeTarget(word), Reading: new WordActionReading(
                    word.Assessment!.Morphology!.Analyses[reading.Index], null, reading.Index)))).ToArray();
        foreach (var action in actions)
            if (!await _changes.AddFromTextAsync(ChangeKinds.AddCandidate, action.Target, action.Reading, groupId)
                .ConfigureAwait(true)) return;
    }

    private async Task MarkNativeSpellingsAsync(AnalysisOperationScope scope)
    {
        var targets = NativeWordsFor(scope).Where(word => word.Actions.CandidateWordformIds.Count == 1)
            .Select(NativeTargetWithoutOccurrence).ToArray();
        foreach (var target in targets)
            if (!await _changes.AddFromTextAsync(ChangeKinds.IncorrectSpelling, target).ConfigureAwait(true)) return;
    }

    private WordActionTarget NativeTargetWithoutOccurrence(SelectionWordSummary word) => NativeTarget(word);

    private async Task RemoveNativeAnalysesAsync(AnalysisOperationScope scope)
    {
        var ids = NativeAnalysisIds(scope);
        var removals = NativeWordsFor(scope).Where(word => word.Actions.CanRemoveAnalysis)
            .SelectMany(word => word.Actions.StoredAnalysisIds.Where(ids.Contains).Select(id =>
                (Target: NativeTarget(word), Id: Guid.TryParse(id, out var guid) ? CanonicalId.FromGuid(guid).Value : id)))
            .DistinctBy(removal => removal.Id).ToArray();
        foreach (var removal in removals)
            if (!await _changes.RemoveCapturedAnalysisAsync(removal.Target, removal.Id).ConfigureAwait(true)) return;
    }

    private async Task UndoNativeChangesAsync(AnalysisOperationScope scope)
    {
        var changes = NativeChangesFor(scope);
        foreach (var change in changes) await _changes.RemoveCommand.ExecuteAsync(change).ConfigureAwait(true);
    }
}
