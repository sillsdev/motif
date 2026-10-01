using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Finds the Selection's words each grammar finding touches, from what its subjects reach and the stored Parse all
/// words: by identity through stored analyses and stored per-word rule times, by spelling only for letters.
/// </summary>
public static class WarningWordsQuery
{
    /// <summary>
    /// <paramref name="check"/> with each finding's words, from the stored Parse all words matching the current
    /// Baseline and default Selection; unchanged when none matches.
    /// </summary>
    internal static GrammarCheckResponse WithYourWords(MotifDatabase database, ProjectLocator project,
        GrammarCheckResponse check)
    {
        var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project, includeResolvedReadings: false);
        if (!current.Succeeded || current.Value!.Assessment is not { } assessment) return check;
        return WithYourWords(check, assessment.Words, current.Value.EffectiveObjectTimings);
    }

    /// <summary><paramref name="check"/> with each finding's words in <paramref name="words"/>.</summary>
    public static GrammarCheckResponse WithYourWords(GrammarCheckResponse check,
        IReadOnlyList<AssessmentWordResult> words, IReadOnlyList<AssessmentObjectTiming> timings)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check with
        {
            Findings = check.Findings.Select(finding =>
                finding with { YourWords = YourWordsOf(finding, words, timings) }).ToArray(),
        };
    }

    /// <summary>
    /// The words <paramref name="finding"/> touches, or <see langword="null"/> when a subject was stored without
    /// what it reaches. Subjects matched by identity win over subjects matched only by spelling.
    /// </summary>
    public static WarningWords? YourWordsOf(GrammarWarning finding, IReadOnlyList<AssessmentWordResult> words,
        IReadOnlyList<AssessmentObjectTiming> timings)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(timings);
        if (finding.Subject.Any(part => part.ObjectId is not null && part.Reach is null)) return null;
        var named = finding.Subject.Where(part => part.Reach is not null).ToArray();
        var reaches = named.Select(part => part.Reach!).ToArray();

        var byIdentity = reaches.Where(reach => reach.Path is not (WarningWordsPath.Spelling or WarningWordsPath.CantTell))
            .ToArray();
        if (byIdentity.Length > 0)
            return Found(WarningWordsMatch.Identity, words, ByIdentity(words, timings, byIdentity),
                byIdentity.Select(reach => reach.Path));

        var bySpelling = reaches.Where(reach => reach.Path == WarningWordsPath.Spelling).ToArray();
        if (bySpelling.Length > 0)
        {
            var spellings = bySpelling.SelectMany(reach => reach.Spellings).Select(Fold)
                .Where(spelling => spelling.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            var spelled = words.Where(word => spellings.Any(Fold(word.Word).Contains))
                .Select(word => new ObjectUseWord(WordRowProjection.Of(word)));
            return Found(WarningWordsMatch.Spelling, words, spelled, [WarningWordsPath.Spelling]);
        }

        return new WarningWords(WarningWordsMatch.CantTell, [], [])
        {
            CantTell = named.Length == 0
                ? WarningCantTell.NothingNamed
                : reaches.Select(reach => reach.CantTell).FirstOrDefault(reason => reason is not null)
                  ?? WarningCantTell.KindNotFollowed,
            Paths = [WarningWordsPath.CantTell],
        };
    }

    /// <summary>
    /// The words <paramref name="findings"/> touch, each counted once; <see langword="null"/> when some finding's
    /// words are not known. With no findings, no word uses anything a finding names.
    /// </summary>
    public static WarningWordsTouched? Touched(IReadOnlyList<GrammarWarning> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Any(finding => finding.YourWords is null)) return null;
        var rows = new Dictionary<string, (ObjectUseWord Word, bool ByIdentity)>(StringComparer.Ordinal);
        foreach (var yours in findings.Select(finding => finding.YourWords!))
            foreach (var word in yours.Words)
            {
                var byIdentity = yours.Match == WarningWordsMatch.Identity;
                rows[word.Row.Word] = rows.TryGetValue(word.Row.Word, out var seen)
                    ? (seen.Word, seen.ByIdentity || byIdentity) : (word, byIdentity);
            }
        var touched = rows.Values.Select(row => row.Word).ToArray();
        return new WarningWordsTouched(touched.Length,
            touched.Count(word => word.Row.Outcome == WordRowOutcome.NoParse),
            ObjectUsesQuery.Split(touched).ByMeaning)
        {
            BySpellingOnly = rows.Values.Count(row => !row.ByIdentity),
        };
    }

    private static IEnumerable<ObjectUseWord> ByIdentity(IReadOnlyList<AssessmentWordResult> words,
        IReadOnlyList<AssessmentObjectTiming> timings, IReadOnlyList<WarningReach> reaches)
    {
        var found = new Dictionary<string, ObjectUseWord>(StringComparer.Ordinal);
        foreach (var reach in reaches)
        {
            foreach (var id in reach.AllomorphIds)
                Add(ObjectUsesQuery.UsesOf(words, new ObjectUseRef { AllomorphId = id }));
            foreach (var id in reach.GrammaticalInfoIds)
                Add(ObjectUsesQuery.UsesOf(words, new ObjectUseRef { GrammaticalInfoId = id }));
            foreach (var key in reach.TimingKeys)
                Add(ObjectUsesQuery.RanIn(words, timings, ObjectUseRef.ForTimingKey(key)));
        }
        return found.Values;

        // A word a rule ran in keeps the rule's calls and time, which a use carries none of.
        void Add(ObjectUseWords uses)
        {
            foreach (var word in uses.Words)
                if (!found.TryGetValue(word.Row.Word, out var seen) || !Measured(seen) && Measured(word))
                    found[word.Row.Word] = word;
        }

        static bool Measured(ObjectUseWord word) => word.Calls is not null || word.ElapsedNs is not null;
    }

    private static WarningWords Found(WarningWordsMatch match, IReadOnlyList<AssessmentWordResult> words,
        IEnumerable<ObjectUseWord> found, IEnumerable<WarningWordsPath> paths)
    {
        var order = words.Select((word, index) => (word.Word, index))
            .ToDictionary(pair => pair.Word, pair => pair.index, StringComparer.Ordinal);
        var split = ObjectUsesQuery.Split(found.OrderBy(word => order[word.Row.Word]).ToArray());
        return new WarningWords(match, split.Words, split.ByMeaning) { Paths = paths.Distinct().ToArray() };
    }

    // Spelling compares canonically decomposed and case-folded, so a sentence-initial capital still matches.
    private static string Fold(string text) =>
        text.Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant();
}
