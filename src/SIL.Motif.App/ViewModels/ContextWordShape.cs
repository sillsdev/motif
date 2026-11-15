using System.Text;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One written shape in the whole Context: the identical normalized spelling within one writing system. Every
/// wordform the Context records under that spelling and writing system stands for one row, so the row's places add
/// up all of them and never count a place twice. Two writing systems that happen to spell a word alike stay two
/// shapes, and no shape is inferred from a stem, an affix or a sound pattern.
/// </summary>
internal sealed class ContextWordShape
{
    private ContextWordShape(string form, string? writingSystem, IReadOnlyList<SelectionWordSummary> members,
        SelectionWordSummary row)
    {
        Form = form;
        WritingSystem = writingSystem;
        Members = members;
        Row = row;
    }

    /// <summary>The spelling, in the same normalization form the Selection index already uses.</summary>
    public string Form { get; }

    /// <summary>The writing system the spelling is in; never merged with another.</summary>
    public string? WritingSystem { get; }

    /// <summary>The Selection's own rows this shape stands for, one for each wordform recorded under it.</summary>
    public IReadOnlyList<SelectionWordSummary> Members { get; }

    /// <summary>The row the list shows: the shape's counts over all members, keyed by its busiest member.</summary>
    public SelectionWordSummary Row { get; }

    /// <summary>Whether more than one wordform stands behind this spelling, so no single wordform may be acted on.</summary>
    public bool HasSeveralWordforms => Members.Select(member => member.Key.WordformId).Distinct().Skip(1).Any();

    /// <summary>Groups the Selection's rows by normalized spelling within each writing system, keeping their order.</summary>
    public static IReadOnlyList<ContextWordShape> Group(IEnumerable<SelectionWordSummary> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var groups = new Dictionary<(string Form, string? WritingSystem), List<SelectionWordSummary>>();
        var order = new List<(string Form, string? WritingSystem)>();
        foreach (var word in words)
        {
            var key = (Normalize(word.Key.Form), word.Key.WritingSystem);
            if (!groups.TryGetValue(key, out var members))
            {
                groups.Add(key, members = []);
                order.Add(key);
            }
            members.Add(word);
        }
        return order.Select(key => Of(key.Form, key.WritingSystem, groups[key])).ToArray();
    }

    /// <summary>The spelling's identity: trimmed and decomposed, so NFC and NFD spellings are one.</summary>
    internal static string Normalize(string form) => (form ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);

    private static ContextWordShape Of(string form, string? writingSystem, List<SelectionWordSummary> members)
    {
        if (members.Count == 1) return new(form, writingSystem, members, members[0]);
        var primary = members.OrderByDescending(member => member.OccurrenceCount).First();
        var chosen = members.SelectMany(member => member.ChosenAnalysisIds).Distinct().ToArray();
        var ordered = members.SelectMany(member => new[] { member.FirstOccurrence, member.LastOccurrence })
            .OfType<SelectionOccurrenceLocation>()
            .OrderBy(location => location.TextOrder).ThenBy(location => location.LineNumber)
            .ThenBy(location => location.TokenOffset).ToArray();
        var statuses = new Dictionary<string, int>();
        foreach (var pair in members.SelectMany(member => member.StatusCounts))
            statuses[pair.Key] = statuses.GetValueOrDefault(pair.Key) + pair.Value;
        var row = primary with
        {
            ProjectStanding = StandingOf(members),
            OccurrenceCount = members.Sum(member => member.OccurrenceCount),
            StatusCounts = statuses,
            CandidateCount = members.Sum(member => member.CandidateCount),
            IncorrectSpelling = members.Any(member => member.IncorrectSpelling),
            IsAddedWord = members.All(member => member.IsAddedWord),
            FirstOccurrence = ordered.FirstOrDefault(),
            LastOccurrence = ordered.LastOrDefault(),
            Actions = MergeActions(members, primary),
            ReadOccurrenceCount = Sum(members, member => member.ReadOccurrenceCount),
            UnreadOccurrenceCount = Sum(members, member => member.UnreadOccurrenceCount),
            ApprovedAnalysisCount = members.Sum(member => member.ApprovedAnalysisCount),
            ChosenAnalysisIds = chosen,
            ChosenAnalysisCount = chosen.Length > 0 ? chosen.Length : members.Sum(member => member.ChosenAnalysisCount),
            HasTextPlaces = members.Any(member => member.HasTextPlaces),
        };
        return new(form, writingSystem, members, row);
    }

    private static int? Sum(List<SelectionWordSummary> members, Func<SelectionWordSummary, int?> pick) =>
        members.All(member => pick(member) is not null) ? members.Sum(member => pick(member)!.Value) : null;

    // FieldWorks' own order: a spelling marked wrong first, then any approval, any candidate, any rejection.
    private static string StandingOf(IEnumerable<SelectionWordSummary> members)
    {
        var standings = members.Select(member => member.ProjectStanding).ToHashSet(StringComparer.Ordinal);
        foreach (var standing in new[] { ProjectStanding.IncorrectSpelling, ProjectStanding.Approved,
            ProjectStanding.Candidate, ProjectStanding.Rejected })
            if (standings.Contains(standing)) return standing;
        return ProjectStanding.NotPresent;
    }

    // Several wordforms leave the target ambiguous, so only the actions every wordform allows remain.
    private static SelectionWordActionFacts MergeActions(List<SelectionWordSummary> members, SelectionWordSummary primary)
    {
        var actions = members.Select(member => member.Actions).ToArray();
        return primary.Actions with
        {
            CanMarkRead = actions.All(action => action.CanMarkRead),
            CanChangeOpinion = actions.All(action => action.CanChangeOpinion),
            CanRemoveAnalysis = actions.All(action => action.CanRemoveAnalysis),
            CanAddParserReading = actions.All(action => action.CanAddParserReading),
            CanAcceptNewSet = actions.All(action => action.CanAcceptNewSet),
            StoredAnalysisIds = actions.SelectMany(action => action.StoredAnalysisIds).Distinct().ToArray(),
            CandidateWordformIds = actions.SelectMany(action => action.CandidateWordformIds).Distinct().ToArray(),
            StoredOpinions = actions.SelectMany(action => action.StoredOpinions).GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.First().Value),
            NeedsALook = actions.Any(action => action.NeedsALook),
        };
    }
}
