using SIL.Motif.Contract.Responses;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>What a Passage keep did: whether it handled the key, what to say, and which places it staged.</summary>
public sealed record PassageKeepResult(bool Handled, string? Message, IReadOnlyList<OccurrenceAnchor> Staged);

public sealed partial class ResultsInTextViewModel
{
    /// <summary>The Passage state of a place, from its stored status.</summary>
    public PassageWordState PassageStateOf(OccurrenceAnchor anchor) => _nativePositions.TryGetValue(anchor, out var position)
        ? PassageWordStates.From(position.Location.Status) : PassageWordState.NeedsAnalysis;

    private bool IsStagedKeep(SelectionSourcePosition position) => _changes.Items.Any(change =>
        change.Kind == ChangeKinds.Approve && change.Occurrence == position.Location.Anchor);

    private bool IsKeepGap(SelectionSourcePosition position) =>
        PassageWordStates.IsGap(PassageWordStates.From(position.Location.Status)) && !IsStagedKeep(position);

    /// <summary>The next place after <paramref name="anchor"/> in the selected Text that is not approved or staged.</summary>
    public OccurrenceAnchor? NextGapAfter(OccurrenceAnchor anchor)
    {
        var positions = (_nativeSummary?.SourcePositions ?? []).Where(position =>
            position.Location.Anchor.TextId == anchor.TextId && position.Word.Form.Length > 0).ToArray();
        var index = Array.FindIndex(positions, position => position.Location.Anchor == anchor);
        if (index < 0) return null;
        for (var next = index + 1; next < positions.Length; next++)
            if (IsKeepGap(positions[next])) return positions[next].Location.Anchor;
        return null;
    }

    /// <summary>The reading a keep writes and the stored analysis it keeps, if any.</summary>
    private readonly record struct KeepReading(WordActionReading Reading, string? StoredAnalysisId);

    private ResultsTokenViewModel? TokenAt(OccurrenceAnchor anchor) =>
        (LinePages?.RealizedLines ?? []).SelectMany(line => line.Tokens)
            .FirstOrDefault(token => token.IsWord && token.Occurrence == anchor);

    /// <summary>The panel's chosen reading, else the stored one, else the parser's only one; or why none.</summary>
    private (KeepReading? Reading, string? Refusal) ChooseKeepReading(SelectionSourcePosition position,
        ResultsTokenViewModel? token)
    {
        if (OccurrencePanel is { ChosenReading: { } chosen } && _panelTarget is { } target && _panelKey == position.Word)
            return (target.ParserReadings.TryGetValue(chosen.Id, out var parser)
                ? new KeepReading(new WordActionReading(parser.Analysis, chosen.Label, parser.Index), null)
                : new KeepReading(new WordActionReading(null, chosen.Label, null), chosen.Id), null);
        if (position.Location.AnalysisId is { } analysisId)
        {
            var stored = CanonicalId.FromGuid(analysisId).Value;
            var shown = token?.FieldWorksAnalyses.FirstOrDefault(analysis => analysis.StoredAnalysisId == stored);
            return (new KeepReading(new WordActionReading(null, shown is null ? null : ReadingLabel(shown.Morphs), null),
                stored), null);
        }
        if (token is null) return (null, MotifAppStrings.OccurrencesChangedStatus);
        var offered = ParserKeepReadings(token);
        return offered.Count switch
        {
            0 => (null, MotifAppStrings.OccurrencesNoReadingsReason),
            1 => (new KeepReading(new WordActionReading(offered[0].Analysis, offered[0].Text, offered[0].Index), null), null),
            _ => (null, MotifAppStrings.PassageSeveralReadingsStatus),
        };
    }

    private static IReadOnlyList<ResultsReadingViewModel> ParserKeepReadings(ResultsTokenViewModel token)
    {
        var offered = new List<ResultsReadingViewModel>();
        foreach (var reading in token.Readings.Where(reading => !reading.IsDisapproved && reading.Analysis is not null))
        {
            var label = reading.Morphs.Count > 0 ? ReadingLabel(reading.Morphs) : reading.Text;
            if (offered.All(other => (other.Morphs.Count > 0 ? ReadingLabel(other.Morphs) : other.Text) != label))
                offered.Add(reading);
        }
        return offered;
    }

    /// <summary>
    /// Stages the chosen reading at this one place; the other places with the same spelling are untouched. It
    /// always answers with a sentence, whether it staged, found the place already kept, or refused.
    /// </summary>
    public async Task<PassageKeepResult> KeepAnalysisAsync(OccurrenceAnchor anchor, CancellationToken cancellationToken = default,
        ResultsTokenViewModel? token = null)
    {
        if (!_nativePositions.TryGetValue(anchor, out var position) || _selectionOwner.Reader is null)
            return new PassageKeepResult(false, MotifAppStrings.OccurrencesChangedStatus, []);
        if (PassageWordStates.From(position.Location.Status) == PassageWordState.Approved)
            return new PassageKeepResult(true, MotifAppStrings.OccurrencesAlreadyApprovedStatus, []);
        if (IsStagedKeep(position))
            return new PassageKeepResult(true, MotifAppStrings.OccurrencesAlreadyStagedStatus, []);
        var (reading, refusal) = ChooseKeepReading(position, token ?? TokenAt(anchor));
        if (reading is not { } keep) return new PassageKeepResult(false, refusal, []);
        return await StageKeepAsync([position], keep, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Stages the same chosen analysis at every eligible place in the chosen Texts. A place is eligible when it is
    /// the same wordform with the same chosen analysis and is neither approved nor already staged; a homograph with
    /// another analysis is never touched.
    /// </summary>
    public async Task<PassageKeepResult> KeepAllLikeThisAsync(OccurrenceAnchor anchor,
        CancellationToken cancellationToken = default, ResultsTokenViewModel? token = null)
    {
        if (!_nativePositions.TryGetValue(anchor, out var position) || _selectionOwner.Reader is null)
            return new PassageKeepResult(false, MotifAppStrings.OccurrencesChangedStatus, []);
        var (reading, refusal) = ChooseKeepReading(position, token ?? TokenAt(anchor));
        if (reading is not { } keep) return new PassageKeepResult(false, refusal, []);
        var places = EligibleLikeThis(position).ToArray();
        if (places.Length == 0)
            return new PassageKeepResult(true, MotifAppStrings.OccurrencesNothingToKeepStatus, []);
        return await StageKeepAsync(places, keep, cancellationToken).ConfigureAwait(true);
    }

    internal IEnumerable<SelectionSourcePosition> EligibleLikeThis(SelectionSourcePosition position) =>
        (_nativeSummary?.SourcePositions ?? []).Where(other => other.Word == position.Word &&
            other.Location.AnalysisId == position.Location.AnalysisId && IsKeepGap(other));

    private async Task<PassageKeepResult> StageKeepAsync(IReadOnlyList<SelectionSourcePosition> places, KeepReading keep,
        CancellationToken cancellationToken)
    {
        var targets = places.Select(place => NativeTarget(_nativeWords[place.Word], place.Location.Anchor)).ToArray();
        var written = await _changes.StageOccurrenceApprovalsAsync(targets, keep.Reading, keep.StoredAnalysisId,
            cancellationToken).ConfigureAwait(true);
        if (written.Count > 0 && OccurrencePanel is { } open) _ = ReloadOccurrencePanelAsync(open);
        var message = written.Count == places.Count
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesStagedStatusFormat, written.Count)
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesPartlyStagedStatusFormat, written.Count, places.Count);
        return new PassageKeepResult(written.Count > 0, message, written);
    }

    private static async Task ReloadOccurrencePanelAsync(WordOccurrencesViewModel panel)
    {
        try { await panel.LoadPageAsync(panel.PageOffset).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException refusal) { panel.StatusSentence = refusal.Message; }
    }

    private bool IsStagedAnchor(OccurrenceAnchor anchor) => _changes.Items.Any(change =>
        change.Kind == ChangeKinds.Approve && change.Occurrence == anchor);

    private ResultsTokenViewModel? _panelToken;
    private TextWordKey? _panelKey;
    private WordOccurrenceTarget? _panelTarget;

    /// <summary>Asks Claude about a word; set by the page that owns the Request seam.</summary>
    public Func<string, CancellationToken, Task<string>>? AskAboutWord { get; set; }

    /// <summary>The shared panel for the open word, or null when no word with real places is open.</summary>
    [ObservableProperty]
    private WordOccurrencesViewModel? _occurrencePanel;

    private void RefreshOccurrencePanel(ResultsTokenViewModel? token)
    {
        if (ReferenceEquals(token, _panelToken) && (token is null || OccurrencePanel is not null)) return;
        OccurrencePanel?.Cancel();
        _panelToken = token;
        var panel = token is null ? null : BuildOccurrencePanel(token);
        OccurrencePanel = panel;
        if (panel is not null) _ = LoadOccurrencePanelAsync(panel);
    }

    private static async Task LoadOccurrencePanelAsync(WordOccurrencesViewModel panel)
    {
        try { await panel.LoadPageAsync(0).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException refusal) { panel.StatusSentence = refusal.Message; }
    }

    private WordOccurrencesViewModel? BuildOccurrencePanel(ResultsTokenViewModel token)
    {
        if (!token.IsWord || token.Occurrence is null || _selectionOwner.Reader is not { } reader) return null;
        var key = new TextWordKey(token.WordformId, token.Form, token.FormWritingSystem);
        if (!_nativeWords.TryGetValue(key, out var word) || _nativeSummary is not { } summary) return null;
        var options = new List<WordReadingOption>();
        var parser = new Dictionary<string, WordActionReading>(StringComparer.Ordinal);
        foreach (var stored in token.FieldWorksAnalyses.Where(analysis => analysis.Opinion != ReadingGrade.Disapproved))
            options.Add(new WordReadingOption(stored.StoredAnalysisId, ReadingLabel(stored.Morphs), false));
        foreach (var reading in token.Readings.Where(reading => !reading.IsDisapproved && reading.Analysis is not null))
        {
            var label = reading.Morphs.Count > 0 ? ReadingLabel(reading.Morphs) : reading.Text;
            if (options.Any(option => option.Label == label)) continue;
            var id = "parser:" + reading.Index.ToString(CultureInfo.InvariantCulture);
            parser[id] = new WordActionReading(reading.Analysis, reading.Text, reading.Index);
            options.Add(new WordReadingOption(id, label, true));
        }
        var target = new WordOccurrenceTarget(token.Form, word.Actions.CandidateWordformIds.Count == 1
            ? word.Actions.CandidateWordformIds[0] : token.WordformId, word.Assessment?.AssessmentId, parser);
        var texts = summary.Texts.ToDictionary(text => text.TextId);
        var source = new NativeOccurrenceSource(reader, key, word.OccurrenceCount, texts, token.FormWritingSystem,
            IsStagedAnchor);
        _panelKey = key;
        _panelTarget = target;
        var actions = new ChangesOccurrenceActions(_changes, () => target, (form, cancellation) =>
            AskAboutWord is { } ask ? ask(form, cancellation) : Task.FromResult(MotifAppStrings.ClaudeUnsupportedTarget));
        return new WordOccurrencesViewModel(token.Form, options, source, actions) { FormWritingSystem = token.FormWritingSystem };
    }

    private static string ReadingLabel(IReadOnlyList<ParserReadingMorphViewModel> morphs) =>
        string.Join(" ", morphs.Select(morph => morph.Form)) + " \u00b7 " +
        string.Join(" ", morphs.Select(morph => morph.GlossOrPlaceholder));

    /// <summary>Pages one word's places from the captured Selection.</summary>
    private sealed class NativeOccurrenceSource(SelectionReader reader, TextWordKey key, int count,
        IReadOnlyDictionary<Guid, SelectionTextSummary> texts, string? formWritingSystem,
        Func<OccurrenceAnchor, bool> isStaged) : IWordOccurrenceSource
    {
        public int Count => count;

        public async Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int size,
            CancellationToken cancellationToken)
        {
            // The reader serves two reads at once, so a busy answer is retried; any other refusal is shown, not hidden.
            var outcome = await reader.ReadOccurrencesAsync(key, new OccurrenceRange(offset, size), cancellationToken)
                .ConfigureAwait(true);
            for (var attempt = 0; !outcome.Succeeded && outcome.Refusal?.Reason == FailureReason.Busy && attempt < 40; attempt++)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(true);
                outcome = await reader.ReadOccurrencesAsync(key, new OccurrenceRange(offset, size), cancellationToken)
                    .ConfigureAwait(true);
            }
            if (!outcome.Succeeded) throw new InvalidOperationException(outcome.Refusal?.Message ?? MotifAppStrings.OccurrencesChangedStatus);
            using var read = outcome.Value!;
            var expected = reader.Context.ExpectedWriteContext();
            return read.Value.Occurrences.Select(item =>
            {
                var choice = new OccurrenceChoiceViewModel(item.Location.Anchor,
                texts.TryGetValue(item.Location.Anchor.TextId, out var text) ? text.Title : string.Empty,
                item.Location.LineNumber, item.Sentence, item.Location.Status,
                item.Location.AnalysisId is { } id ? CanonicalId.FromGuid(id).Value : null, expected)
            {
                SentenceWritingSystem = item.SentenceWritingSystem ?? formWritingSystem,
                SentenceStyle = item.SentenceStyle,
            };
                // Changes is the truth about what is staged, so a reopened word never offers a place twice.
                if (isStaged(item.Location.Anchor)) choice.MarkStaged();
                return choice;
            }).ToArray();
        }
    }
}
