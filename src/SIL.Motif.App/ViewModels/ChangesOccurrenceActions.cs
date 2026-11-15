using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>Stages a word's keep, Not a Word and Ask Claude through the pending Changes and the Request seam.</summary>
internal sealed class ChangesOccurrenceActions(
    ChangesViewModel changes, Func<WordOccurrenceTarget> target,
    Func<string, CancellationToken, Task<string>> ask) : IWordOccurrenceActions
{
    public async Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
        IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken cancellationToken)
    {
        var word = target();
        var frozen = places.Select(place => new WordActionTarget(word.Form, word.WordformId, place.Anchor,
            word.AssessmentId, place.ExpectedContext)).ToArray();
        var isParserReading = word.ParserReadings.TryGetValue(reading.Id, out var found);
        var stored = isParserReading ? null : reading.Id;
        var action = isParserReading
            ? new WordActionReading(found!.Analysis, reading.Label, found.Index)
            : new WordActionReading(null, reading.Label, null);
        // Changes is the truth: a place whose Approve is already staged is reported staged and never written twice.
        var already = frozen.Where(place => changes.Items.Any(change =>
            change.Kind == ChangeKinds.Approve && change.Occurrence == place.Occurrence))
            .Select(place => place.Occurrence!).ToHashSet();
        var written = await changes.StageOccurrenceApprovalsAsync(
            frozen.Where(place => !already.Contains(place.Occurrence!)).ToArray(), action, stored, cancellationToken)
            .ConfigureAwait(true);
        var done = written.Concat(already).ToHashSet();
        return places.Select(place => place.Anchor).Where(done.Contains).ToArray();
    }

    public Task<bool> StageNotAWordAsync(string form, CancellationToken cancellationToken)
    {
        var word = target();
        return changes.AddFromTextAsync(ChangeKinds.IncorrectSpelling,
            new WordActionTarget(form, word.WordformId, null, word.AssessmentId));
    }

    public Task<string> AskAsync(string form, CancellationToken cancellationToken) => ask(form, cancellationToken);
}

/// <summary>The word identity a keep acts on, frozen when the panel opens.</summary>
internal sealed record WordOccurrenceTarget(
    string Form, Guid? WordformId, string? AssessmentId,
    IReadOnlyDictionary<string, WordActionReading> ParserReadings);
