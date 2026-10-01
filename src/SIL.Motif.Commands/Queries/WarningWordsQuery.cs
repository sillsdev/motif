using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Worker.Store;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Finds the Selection's words each grammar finding touches, from what its subjects reach and the stored Parse all
/// words: exact identity uses, membership candidates, and spelling candidates, kept separate in that order.
/// </summary>
public static class WarningWordsQuery
{
    /// <summary>
    /// <paramref name="check"/> with each finding's words from stored Parse all words matching the checked
    /// Baseline and default Selection; words remain unknown when matching context is unavailable.
    /// </summary>
    internal static GrammarCheckResponse WithYourWords(MotifDatabase database, ProjectLocator project,
        GrammarCheckResponse check, BaselineToken checkedBaseline)
    {
        var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project, includeResolvedReadings: false);
        if (!current.Succeeded || current.Value!.Baseline?.Token != checkedBaseline ||
            current.Value.Assessment is not { } assessment)
            return check with { Findings = check.Findings.Select(finding => finding with { YourWords = null }).ToArray() };
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
    /// what it reaches. Exact uses take precedence over membership candidates, then spelling candidates.
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

        var routes = reaches.Where(reach => reach.IsRoute).ToArray();
        if (routes.Length > 0)
        {
            var exactRoutes = routes.Where(reach => reach.Path is not
                (WarningWordsPath.Spelling or WarningWordsPath.Membership)).ToArray();
            var membershipRoutes = routes.Where(reach => reach.Path == WarningWordsPath.Membership)
                .Concat(routes.Where(reach => reach.MembershipAllomorphIds.Count > 0 ||
                    reach.MembershipGrammaticalInfoIds.Count > 0 || reach.MembershipTimingKeys.Count > 0)
                    .Select(reach => new WarningReach(WarningWordsPath.Membership)
                    {
                        AllomorphIds = reach.MembershipAllomorphIds,
                        GrammaticalInfoIds = reach.MembershipGrammaticalInfoIds,
                        TimingKeys = reach.MembershipTimingKeys,
                    })).ToArray();
            var exact = ByIdentity(words, timings, exactRoutes).ToArray();
            var stronger = exact.Select(word => word.Row.Word).ToHashSet(StringComparer.Ordinal);
            var members = ByIdentity(words, timings, membershipRoutes)
                .Where(word => stronger.Add(word.Row.Word)).ToArray();
            var spellings = routes.SelectMany(reach => reach.Spellings).Select(Fold)
                .Where(spelling => spelling.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            var spelled = words.Where(word => !stronger.Contains(word.Word) && spellings.Any(Fold(word.Word).Contains))
                .Select(word => new ObjectUseWord(WordRowProjection.Of(word))).ToArray();
            var paths = routes.Select(reach => reach.Path);
            if (exactRoutes.Length > 0)
                return Found(WarningWordsMatch.Identity, words, exact, paths) with
                {
                    MembershipCandidates = Found(WarningWordsMatch.Membership, words, members, []).Words,
                    SpellingCandidates = spelled,
                };
            if (membershipRoutes.Length > 0)
                return Found(WarningWordsMatch.Membership, words, members, paths) with { SpellingCandidates = spelled };
            return Found(WarningWordsMatch.Spelling, words, spelled, paths);
        }

        var unattributed = WarningReach.Unattributed(reaches);
        var path = unattributed?.Path ?? WarningWordsPath.UnresolvedIdentity;
        return new WarningWords(path switch
        {
            WarningWordsPath.MissingObject => WarningWordsMatch.MissingObject,
            WarningWordsPath.ProjectWide => WarningWordsMatch.ProjectWide,
            _ => WarningWordsMatch.UnresolvedIdentity,
        }, [], [])
        {
            Reason = unattributed?.Reason ?? WarningAttributionReason.NoSubject,
            Paths = [path],
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
        var rows = new Dictionary<string, (ObjectUseWord Word, int Strength)>(StringComparer.Ordinal);
        foreach (var yours in findings.Select(finding => finding.YourWords!))
        {
            Add(yours.Words, yours.Match switch
            {
                WarningWordsMatch.Identity => 3,
                WarningWordsMatch.Membership => 2,
                _ => 1,
            });
            Add(yours.MembershipCandidates, 2);
            Add(yours.SpellingCandidates, 1);
        }
        var touched = rows.Values.Where(row => row.Strength == 3).Select(row => row.Word).ToArray();
        return new WarningWordsTouched(touched.Length,
            touched.Count(word => word.Row.Outcome == WordRowOutcome.NoParse),
            ObjectUsesQuery.Split(touched).ByMeaning)
        {
            ByMembershipOnly = rows.Values.Count(row => row.Strength == 2),
            BySpellingOnly = rows.Values.Count(row => row.Strength == 1),
        };

        void Add(IReadOnlyList<ObjectUseWord> words, int strength)
        {
            foreach (var word in words)
                if (!rows.TryGetValue(word.Row.Word, out var seen) || seen.Strength < strength)
                    rows[word.Row.Word] = (word, strength);
        }
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
