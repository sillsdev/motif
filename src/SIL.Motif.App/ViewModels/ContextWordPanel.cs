using System.Globalization;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Builds the shared word occurrence panel for one written shape of the whole Context. It only assembles the
/// panel's source and actions; the panel, its check boxes and its commands are the Passage's own.
/// </summary>
internal static class ContextWordPanel
{
    /// <summary>Makes the panel for the shape's open row, or null when the shape has no places in a Text to offer.</summary>
    public static WordOccurrencesViewModel? Build(TextWordRowViewModel row, ContextWordShape shape,
        SelectionReader reader, IReadOnlyList<SelectionTextSummary> texts, ChangesViewModel changes,
        ResultsTokenViewModel? token,
        Func<OccurrenceAnchor, bool> isStaged)
    {
        if (!shape.Row.HasTextPlaces || shape.Row.OccurrenceCount == 0) return null;
        var options = new List<WordReadingOption>();
        var parser = new Dictionary<string, WordActionReading>(StringComparer.Ordinal);
        var owners = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var shared = shape.HasSeveralWordforms;
        // A shared row offers each wordform's own readings, named by the homograph that holds them.
        var stored = shared
            ? row.Homographs.SelectMany(homograph => homograph.Analyses.Select(analysis => ((HomographAnalyses?)homograph, analysis)))
            : row.StoredAnalyses.Select(analysis => ((HomographAnalyses?)null, analysis));
        foreach (var (homograph, analysis) in stored.Where(item => item.analysis.StoredAnalysisId is not null &&
            item.analysis.StoredAnalysisOpinion != ReadingGrade.Disapproved))
        {
            var label = LabelOf(analysis.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray());
            if (homograph is not null)
            {
                label = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesHomographReadingFormat,
                    homograph.Ordinal, label);
                owners[analysis.StoredAnalysisId!] = homograph.WordformId;
            }
            options.Add(new WordReadingOption(analysis.StoredAnalysisId!, label, false));
        }
        foreach (var reading in (token?.Readings ?? []).Where(reading => !reading.IsDisapproved && reading.Analysis is not null))
        {
            var label = reading.Morphs.Count > 0 ? LabelOf(reading.Morphs) : reading.Text;
            if (options.Any(option => option.Label == label)) continue;
            var id = "parser:" + reading.Index.ToString(CultureInfo.InvariantCulture);
            parser[id] = new WordActionReading(reading.Analysis, reading.Text, reading.Index);
            options.Add(new WordReadingOption(id, label, true));
        }
        var assessment = shape.Row.Assessment?.AssessmentId;
        var source = new ContextWordOccurrenceSource(reader, shape.Members,
            texts.ToDictionary(text => text.TextId), shape.WritingSystem, isStaged);
        var actions = new ContextWordActions(changes, source, id => new WordOccurrenceTarget(row.Form, id, assessment, parser),
            owners, shared);
        var panel = new WordOccurrencesViewModel(row.Form, options, source, actions) { FormWritingSystem = shape.WritingSystem };
        if (shared && row.HomographsUnread) panel.StatusSentence = MotifAppStrings.OccurrencesHomographsUnreadStatus;
        // The panel words its own status when an approve ends, so a note about skipped places follows it.
        panel.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(WordOccurrencesViewModel.IsBusy) && !panel.IsBusy &&
                actions.TakeNote() is { } note) panel.StatusSentence = note;
        };
        return panel;
    }

    private static string LabelOf(IReadOnlyList<ParserReadingMorphViewModel> morphs) =>
        string.Join(" ", morphs.Select(morph => morph.Form)) + " · " +
        string.Join(" ", morphs.Select(morph => morph.GlossOrPlaceholder));
}

/// <summary>Pages every place a written shape occurs, wordform by wordform, remembering which wordform owns each.</summary>
internal sealed class ContextWordOccurrenceSource(SelectionReader reader, IReadOnlyList<SelectionWordSummary> members,
    IReadOnlyDictionary<Guid, SelectionTextSummary> texts, string? writingSystem, Func<OccurrenceAnchor, bool> isStaged)
    : IWordOccurrenceSource
{
    private readonly Dictionary<OccurrenceAnchor, Guid?> _owners = [];

    public int Count { get; } = members.Sum(member => member.OccurrenceCount);

    /// <summary>The wordform whose place this anchor is, as far as pages read so far show.</summary>
    public Guid? WordformOf(OccurrenceAnchor anchor) => _owners.GetValueOrDefault(anchor);

    /// <summary>Every wordform recorded under the shape.</summary>
    public IReadOnlyList<Guid?> Wordforms => members.Select(member => member.Key.WordformId).Distinct().ToArray();

    public async Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int size,
        CancellationToken cancellationToken)
    {
        var result = new List<OccurrenceChoiceViewModel>();
        var start = 0;
        foreach (var member in members)
        {
            var end = start + member.OccurrenceCount;
            var from = Math.Max(offset, start);
            var to = Math.Min(offset + size, end);
            if (to > from) result.AddRange(await ReadMemberAsync(member, from - start, to - from, cancellationToken)
                .ConfigureAwait(true));
            start = end;
        }
        return result;
    }

    private async Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadMemberAsync(SelectionWordSummary member, int offset,
        int size, CancellationToken cancellationToken)
    {
        var outcome = await reader.ReadOccurrencesAsync(member.Key, new OccurrenceRange(offset, size), cancellationToken)
            .ConfigureAwait(true);
        for (var attempt = 0; !outcome.Succeeded && outcome.Refusal?.Reason == FailureReason.Busy && attempt < 40; attempt++)
        {
            await Task.Delay(25, cancellationToken).ConfigureAwait(true);
            outcome = await reader.ReadOccurrencesAsync(member.Key, new OccurrenceRange(offset, size), cancellationToken)
                .ConfigureAwait(true);
        }
        if (!outcome.Succeeded) throw new InvalidOperationException(outcome.Refusal?.Message ?? MotifAppStrings.OccurrencesChangedStatus);
        using var read = outcome.Value!;
        var expected = reader.Context.ExpectedWriteContext();
        return read.Value.Occurrences.Select(item =>
        {
            _owners[item.Location.Anchor] = member.Key.WordformId;
            var choice = new OccurrenceChoiceViewModel(item.Location.Anchor,
                texts.TryGetValue(item.Location.Anchor.TextId, out var text) ? text.Title : string.Empty,
                item.Location.LineNumber, item.Sentence, item.Location.Status,
                item.Location.AnalysisId is { } id ? CanonicalId.FromGuid(id).Value : null, expected)
            {
                SentenceWritingSystem = item.SentenceWritingSystem ?? writingSystem,
                SentenceStyle = item.SentenceStyle,
            };
            if (isStaged(item.Location.Anchor)) choice.MarkStaged();
            return choice;
        }).ToArray();
    }
}

/// <summary>
/// Stages a shape's keep at each place against that place's own wordform. A stored reading is staged only at the
/// places of the wordform that holds it, never at another's.
/// </summary>
internal sealed class ContextWordActions(ChangesViewModel changes, ContextWordOccurrenceSource source,
    Func<Guid?, WordOccurrenceTarget> targetOf,
    IReadOnlyDictionary<string, Guid?> readingOwners, bool sharedShape) : IWordOccurrenceActions
{
    private string? _note;

    /// <summary>The sentence the last keep or Not a Word needs beyond the panel's own, once.</summary>
    public string? TakeNote()
    {
        var note = _note;
        _note = null;
        return note;
    }

    public async Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
        IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken cancellationToken)
    {
        var written = new List<OccurrenceAnchor>();
        var skipped = 0;
        // On a shared shape a stored reading is only ever staged at the wordform proven to hold it.
        var owned = sharedShape && !reading.IsGuess;
        var owner = owned && readingOwners.TryGetValue(reading.Id, out var holder) ? holder : null;
        foreach (var group in places.GroupBy(place => source.WordformOf(place.Anchor)))
        {
            var batch = group.ToArray();
            if (owned && (owner is null || group.Key != owner))
            {
                skipped += batch.Length;
                continue;
            }
            var inner = new ChangesOccurrenceActions(changes, () => targetOf(group.Key));
            var done = await inner.StageKeepAsync(reading, batch, cancellationToken).ConfigureAwait(true);
            written.AddRange(done);
            // The first group that is not fully written ends the keep, so no later place is touched.
            if (done.Count < batch.Length) break;
        }
        if (skipped > 0)
            _note = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesOtherHomographStatusFormat,
                written.Count, skipped);
        return written;
    }

    public async Task<bool> StageNotAWordAsync(string form, CancellationToken cancellationToken)
    {
        var staged = 0;
        foreach (var wordform in source.Wordforms)
        {
            var inner = new ChangesOccurrenceActions(changes, () => targetOf(wordform));
            if (!await inner.StageNotAWordAsync(form, cancellationToken).ConfigureAwait(true))
            {
                if (staged > 0)
                    _note = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesNotAWordPartlyStagedFormat,
                        staged, source.Wordforms.Count);
                return false;
            }
            staged++;
        }
        return true;
    }
}
